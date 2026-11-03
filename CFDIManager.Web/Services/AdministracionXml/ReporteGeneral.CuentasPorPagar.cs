using ClosedXML.Excel;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Dashboard compacto de cuentas por pagar (skill dashboard-cxp-compacto-v2) de CFDI recibidos: 4 KPIs (facturas,
/// notas de crédito, pagado y saldo), tabla por proveedor que cuadra con los KPIs y estado de cuenta con dropdown.
/// Lee de las hojas Factura (Table13), NCredito (Table1) y Pago (TablePago) o de una sola tabla agrupada
/// (Por Emisor) con Tipo Factura / NotaCredito / Pago. Todo importe es fórmula.
/// </summary>
public static partial class ReporteGeneral
{
    private sealed record Proveedor(string Rfc, string Nombre, decimal Facturado, int Facturas, int Notas, int Pagos);

    // Pagos distintos (por UUID) aunque el pago tenga varios documentos relacionados.
    private const string UnoPorPago = "COUNTIF(TablePago[UUID],TablePago[UUID]&\"\")";

    /// <summary>
    /// De dónde leen sus fórmulas el dashboard de cuentas por pagar. Unica: facturas, notas y pagos en una sola tabla
    /// distinguidos por Tipo, con notas y pagos en negativo y lo pagado en Total; si no, las hojas del reporte general,
    /// donde la hoja Pago trae un renglón por documento relacionado y lo pagado es ImpPagado.
    /// </summary>
    private sealed record FuenteCxp(string Facturas, string Notas, string Pagos, bool Unica, string Iva, string Origen)
    {
        public static readonly FuenteCxp General = new("Table13", "Table1", "TablePago", false, "IVA 16%", "hojas Factura, NCredito y Pago");
        public static readonly FuenteCxp PorEmisor = new(TablaPorEmisor, TablaPorEmisor, TablaPorEmisor, true, "IVA", "hoja Por Emisor");

        private string Signo => Unica ? "-" : "";

        public static string De(string tabla, string rfc) => $",{tabla}[RFC Emisor],{rfc}";

        public string SumaFacturas(string criterios = "") => Unica
            ? $"SUMIFS({Facturas}[Total],{Facturas}[Tipo],\"Factura\"{criterios})"
            : criterios == "" ? $"SUM({Facturas}[Total])" : $"SUMIFS({Facturas}[Total]{criterios})";

        public string CuentaFacturas(string criterios = "") => Unica
            ? $"COUNTIFS({Facturas}[Tipo],\"Factura\"{criterios})"
            : criterios == "" ? $"COUNTA({Facturas}[UUID])" : $"COUNTIFS({criterios[1..]})";

        public string SumaNotas(string criterios = "") => $"{Signo}SUMIFS({Notas}[Total],{Notas}[Tipo],\"NotaCredito\"{criterios})";

        public string CuentaNotas(string criterios = "") => $"COUNTIFS({Notas}[Tipo],\"NotaCredito\"{criterios})";

        public string SumaPagado(string criterios = "") => Unica
            ? $"-SUMIFS({Pagos}[Total],{Pagos}[Tipo],\"Pago\"{criterios})"
            : criterios == "" ? $"SUM({Pagos}[ImpPagado])" : $"SUMIFS({Pagos}[ImpPagado]{criterios})";

        public string CuentaPagos(string? rfc = null) => Unica
            ? $"COUNTIFS({Pagos}[Tipo],\"Pago\"{(rfc is null ? "" : De(Pagos, rfc))})"
            : rfc is null
                ? $"IFERROR(SUMPRODUCT((TablePago[UUID]<>\"\")/{UnoPorPago}),0)"
                : $"IFERROR(SUMPRODUCT((TablePago[RFC Emisor]={rfc})/{UnoPorPago}),0)";

        public string FiltroFacturas(string rfc) => Unica
            ? $"({Facturas}[RFC Emisor]={rfc})*({Facturas}[Tipo]=\"Factura\")"
            : $"{Facturas}[RFC Emisor]={rfc}";

        public string FiltroPagos(string rfc) => Unica
            ? $"({Pagos}[RFC Emisor]={rfc})*({Pagos}[Tipo]=\"Pago\")"
            : $"TablePago[RFC Emisor]={rfc}";

        public string FiltroNotas(string rfc) => $"({Notas}[RFC Emisor]={rfc})*({Notas}[Tipo]=\"NotaCredito\")";

        public string SignoNotas => Signo;
    }

    private static List<Proveedor> Proveedores(List<Leido> leidos)
    {
        string Rfc(Leido x) => x.A(x.Emisor, "Rfc").ToUpperInvariant();

        return OrdenarProveedores(leidos.Where(x => x.Tipo is "I" or "E" or "P").GroupBy(Rfc).Select(g =>
        {
            var nombre = g.OrderBy(x => x.Tipo == "I" ? 0 : 1).Select(x => x.A(x.Emisor, "Nombre")).FirstOrDefault(n => n.Length > 0) ?? g.Key;
            return new Proveedor(g.Key, nombre, g.Where(x => x.Tipo == "I").Sum(x => x.Total),
                g.Count(x => x.Tipo == "I"), g.Count(x => x.Tipo == "E"), g.Where(x => x.Tipo == "P").Sum(x => FilasPago(x).Count()));
        }));
    }

    /// <summary>Por facturado descendente; el dropdown elige por nombre, así que dos RFC con el mismo nombre se distinguen con el RFC.</summary>
    private static List<Proveedor> OrdenarProveedores(IEnumerable<Proveedor> proveedores)
    {
        var lista = proveedores.OrderByDescending(x => x.Facturado).ThenBy(x => x.Nombre, StringComparer.Ordinal).ToList();
        var repetidos = lista.GroupBy(x => x.Nombre, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return lista.Select(x => repetidos.Contains(x.Nombre) ? x with { Nombre = $"{x.Nombre} ({x.Rfc})" } : x).ToList();
    }

    private static void EscribirDashboardCxp(XLWorkbook wb, List<Proveedor> proveedores, string rfc, string periodo, FuenteCxp fu)
    {
        var ws = wb.Worksheets.Add("Dashboard", 1);
        ws.SetTabColor(XLColor.FromHtml("#1E3A8A"));
        ws.ShowGridLines = false;
        ws.Style.Font.SetFontName("Segoe UI").Font.SetFontSize(9);
        ws.Style.Fill.BackgroundColor = XLColor.White;

        ws.Column(1).Width = Ancho(14);
        ws.Column(17).Width = Ancho(14);
        foreach (var inicio in Grupos) ws.Columns(inicio, inicio + 2).Width = Ancho(72);
        ws.Column(3).Width = Ancho(110);
        foreach (var c in new[] { 5, 9, 13 }) ws.Column(c).Width = Ancho(16);
        (int Fila, double Alto)[] altos = [(1, 8), (2, 46), (3, 18), (4, 5), (5, 8), (6, 15), (7, 24), (8, 15), (9, 5), (10, 8), (11, 18), (12, 30)];
        foreach (var (renglon, alto) in altos) ws.Row(renglon).Height = alto;

        var n = proveedores.Count;
        const int primera = 13;
        var ultima = 12 + n;
        var totales = ultima + 1;
        string Col(string c) => $"${c}${primera}:${c}${ultima}";

        // ---------- Título y KPIs ----------
        var titulo = ws.Range("B2:P2").Merge();
        ws.Cell("B2").Value = $"CUENTAS POR PAGAR · CFDI RECIBIDOS {periodo}";
        titulo.Style.Fill.SetBackgroundColor(Color("1E3A8A")).Font.SetFontSize(20).Font.SetBold().Font.SetFontColor(XLColor.White)
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        var subtitulo = ws.Range("B3:P3").Merge();
        ws.Cell("B3").FormulaA1 = $"\"Receptor {rfc} · \"&{fu.CuentaFacturas()}&\" facturas · \"&{fu.CuentaNotas()}&\" notas de crédito · \"" +
                                  $"&{fu.CuentaPagos()}&\" pagos · {n} proveedores · fuente: {fu.Origen}\"";
        subtitulo.Style.Fill.SetBackgroundColor(Color("DBEAFE")).Font.SetFontColor(Color("1E3A8A"))
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);

        var tarjetas = new (string Titulo, string Valor, string Subtitulo, string[] Tonos)[]
        {
            ("FACTURAS", fu.SumaFacturas(), $"{fu.CuentaFacturas()}&\" facturas recibidas\"", ["0F2A6B", "17399A", "2563EB", "3B82F6"]),
            ("NOTAS DE CRÉDITO", fu.SumaNotas(),
                $"IFERROR(TEXT(F7/B7,\"0.0%\"),\"0.0%\")&\" del facturado · \"&{fu.CuentaNotas()}&\" NC\"", ["5B1E0A", "8A2D0C", "C2410C", "F97316"]),
            ("TOTAL PAGADO", fu.SumaPagado(),
                $"IFERROR(TEXT(J7/(B7-F7),\"0.0%\"),\"0.0%\")&\" del neto · \"&{fu.CuentaPagos()}&\" pagos\"", ["3B0F63", "5B1BA0", "7C3AED", "A855F7"]),
            ("SALDO PENDIENTE", "$B$7-$F$7-$J$7", $"COUNTIF({Col("K")},\">0.005\")&\" proveedores con saldo\"", ["854D0E", "A16207", "CA8A04", "EAB308"]),
        };
        for (var g = 0; g < 4; g++)
        {
            var t = tarjetas[g];
            var col = Grupos[g];
            ws.Range(4, col, 4, col + 2).Style.Fill.BackgroundColor = Color(t.Tonos[2]);
            for (var r = 6; r <= 9; r++)
            {
                ws.Range(r, col, r, col + 2).Merge().Style.Fill.SetBackgroundColor(Color(t.Tonos[r - 6])).Font.SetFontColor(XLColor.White)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            }
            ws.Cell(6, col).Value = t.Titulo;
            ws.Cell(6, col).Style.Font.SetFontSize(8).Font.SetBold();
            ws.Cell(7, col).FormulaA1 = t.Valor;
            ws.Cell(7, col).Style.Font.SetFontSize(14).Font.SetBold().NumberFormat.Format = Moneda;
            ws.Cell(8, col).FormulaA1 = t.Subtitulo;
            ws.Cell(8, col).Style.Font.SetFontSize(8);
        }
        // El blanco no se lee sobre amarillo.
        ws.Cell(8, Grupos[3]).Style.Font.FontColor = Color("422006");

        // ---------- Tabla por proveedor ----------
        BandaCxp(ws, "B11:L11", "RESUMEN POR PROVEEDOR", "1E3A8A");
        var banda = totales + 3;
        var enlace = ws.Range("N11:P11").Merge();
        ws.Cell("N11").Value = "↓ Ir al estado de cuenta";
        ws.Cell("N11").SetHyperlink(new XLHyperlink($"'Dashboard'!B{banda}"));
        enlace.Style.Fill.SetBackgroundColor(Color("1E3A8A")).Font.SetFontColor(XLColor.White).Font.SetUnderline(XLFontUnderlineValues.Single)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);

        (string Rango, string Texto)[] encabezados =
        [
            ("B12", "RFC"), ("C12:D12", "PROVEEDOR"), ("F12", "# FAC"), ("G12", "FACTURADO"), ("H12", "NOTAS CRÉD."),
            ("J12", "PAGADO"), ("K12", "SALDO"), ("L12", "% PART."), ("N12", "ESTADO"), ("O12", "% PAGADO"), ("P12", "# PAGOS"),
        ];
        foreach (var (rango, texto) in encabezados) EncabezadoCxp(ws, rango, texto);

        for (var i = 0; i < n; i++)
        {
            var r = primera + i;
            ws.Cell(r, 2).Value = proveedores[i].Rfc;
            ws.Range(r, 3, r, 4).Merge();
            ws.Cell(r, 3).Value = proveedores[i].Nombre;
            ws.Cell(r, 6).FormulaA1 = fu.CuentaFacturas(FuenteCxp.De(fu.Facturas, $"$B{r}"));
            ws.Cell(r, 7).FormulaA1 = fu.SumaFacturas(FuenteCxp.De(fu.Facturas, $"$B{r}"));
            ws.Cell(r, 8).FormulaA1 = fu.SumaNotas(FuenteCxp.De(fu.Notas, $"$B{r}"));
            ws.Cell(r, 10).FormulaA1 = fu.SumaPagado(FuenteCxp.De(fu.Pagos, $"$B{r}"));
            ws.Cell(r, 11).FormulaA1 = $"$G{r}-$H{r}-$J{r}";
            ws.Cell(r, 12).FormulaA1 = $"IFERROR($G{r}/$B$7,0)";
            ws.Cell(r, 14).FormulaA1 = $"IF(ROUND($K{r},2)<=0,\"PAGADO\",IF($J{r}>0,\"PARCIAL\",\"PENDIENTE\"))";
            ws.Cell(r, 15).FormulaA1 = $"IFERROR($J{r}/($G{r}-$H{r}),0)";
            ws.Cell(r, 16).FormulaA1 = fu.CuentaPagos($"$B{r}");
        }

        ws.Cell(totales, 2).Value = "TOTAL";
        ws.Range(totales, 3, totales, 4).Merge();
        ws.Cell(totales, 3).Value = $"{n} proveedores";
        foreach (var c in new[] { "F", "G", "H", "J", "K", "P" }) ws.Cell($"{c}{totales}").FormulaA1 = $"SUM({c}{primera}:{c}{ultima})";
        ws.Cell($"L{totales}").FormulaA1 = $"IFERROR(G{totales}/$B$7,0)";
        ws.Cell($"O{totales}").FormulaA1 = $"IFERROR(J{totales}/(G{totales}-H{totales}),0)";
        var filaTotal = ws.Range(totales, 2, totales, 16);
        filaTotal.Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetTopBorderColor(Color("1E3A8A"));
        foreach (var c in new[] { 5, 9, 13 }) ws.Cell(totales, c).Style.Border.TopBorder = XLBorderStyleValues.None;

        foreach (var c in new[] { "G", "H", "J", "K" }) ws.Range($"{c}{primera}:{c}{totales}").Style.NumberFormat.Format = Moneda;
        foreach (var c in new[] { "L", "O" }) ws.Range($"{c}{primera}:{c}{totales}").Style.NumberFormat.Format = "0.0%";
        foreach (var c in new[] { "F", "L", "N", "O", "P" }) ws.Range($"{c}{primera}:{c}{totales}").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.SheetView.FreezeRows(12);

        foreach (var rango in new[] { $"B{primera}:D{ultima}", $"F{primera}:H{ultima}", $"J{primera}:L{ultima}", $"N{primera}:P{ultima}" })
            ws.Range(rango).AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(Color("F1F5F9"));
        ws.Range(Col("G").Replace("$", "")).AddConditionalFormat().DataBar(Color("60A5FA")).LowestValue().HighestValue();
        ws.Range(Col("J").Replace("$", "")).AddConditionalFormat().DataBar(Color("6EE7B7")).LowestValue().HighestValue();
        ws.Range(Col("K").Replace("$", "")).AddConditionalFormat().ColorScale()
            .LowestValue(Color("DCFCE7")).Midpoint(XLCFContentType.Percentile, 50, Color("FEF3C7")).HighestValue(Color("FECACA"));
        ws.Range(Col("O").Replace("$", "")).AddConditionalFormat().ColorScale()
            .Minimum(XLCFContentType.Number, 0, Color("EDE9FE")).Midpoint(XLCFContentType.Number, 0.5, Color("D9F99D")).Maximum(XLCFContentType.Number, 1, Color("86EFAC"));
        EstadosCxp(ws.Range(Col("N").Replace("$", "")));

        // ---------- Estado de cuenta ----------
        BandaCxp(ws, $"B{banda}:P{banda}", "ESTADO DE CUENTA POR PROVEEDOR", "5B1BA0");
        var etiquetas = banda + 1;
        var valores = banda + 2;
        foreach (var (rango, texto) in new[] { ($"B{etiquetas}:D{etiquetas}", "SELECCIONA PROVEEDOR"), ($"F{etiquetas}:H{etiquetas}", "RFC"), ($"J{etiquetas}:L{etiquetas}", "ESTADO"), ($"N{etiquetas}:P{etiquetas}", "SALDO") })
            EncabezadoCxp(ws, rango, texto);

        var rfcElegido = $"$F${valores}";
        string Buscar(string col) => $"INDEX({Col(col)},MATCH({rfcElegido},{Col("B")},0))";
        var selector = ws.Range($"B{valores}:D{valores}").Merge();
        ws.Cell($"B{valores}").Value = proveedores[0].Nombre;
        selector.CreateDataValidation().List(Col("C"), true);
        ws.Range($"F{valores}:H{valores}").Merge();
        ws.Cell($"F{valores}").FormulaA1 = $"IFERROR(INDEX({Col("B")},MATCH($B${valores},{Col("C")},0)),\"\")";
        ws.Range($"J{valores}:L{valores}").Merge();
        ws.Cell($"J{valores}").FormulaA1 = $"IFERROR({Buscar("N")},\"\")";
        ws.Range($"N{valores}:P{valores}").Merge();
        ws.Cell($"N{valores}").FormulaA1 = $"IFERROR({Buscar("K")},0)";
        ws.Cell($"N{valores}").Style.NumberFormat.Format = Moneda;
        foreach (var rango in new[] { $"B{valores}:D{valores}", $"F{valores}:H{valores}", $"J{valores}:L{valores}", $"N{valores}:P{valores}" })
            ws.Range(rango).Style.Font.SetBold().Font.SetFontSize(10).Font.SetFontColor(Color("1E3A8A"))
                .Fill.SetBackgroundColor(rango.StartsWith('B') ? Color("FFF7D6") : Color("F5F3FF"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(valores).Height = 20;
        EstadosCxp(ws.Range($"J{valores}:L{valores}"));

        var mini = new (string Titulo, string Tono, string Formula, bool Porcentaje)[]
        {
            ("FACTURADO", "17399A", $"IFERROR({Buscar("G")},0)", false),
            ("NOTAS DE CRÉDITO", "C2410C", $"IFERROR({Buscar("H")},0)", false),
            ("PAGADO", "5B1BA0", $"IFERROR({Buscar("J")},0)", false),
            ("% PAGADO", "7C3AED", $"IFERROR({Buscar("O")},0)", true),
        };
        for (var g = 0; g < 4; g++)
        {
            var col = Grupos[g];
            var etiqueta = ws.Range(banda + 4, col, banda + 4, col + 2).Merge();
            ws.Cell(banda + 4, col).Value = mini[g].Titulo;
            etiqueta.Style.Fill.SetBackgroundColor(Color(mini[g].Tono)).Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(8)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            var valor = ws.Range(banda + 5, col, banda + 5, col + 2).Merge();
            ws.Cell(banda + 5, col).FormulaA1 = mini[g].Formula;
            valor.Style.Fill.SetBackgroundColor(Color("F8FAFC")).Font.SetBold().Font.SetFontSize(11).Font.SetFontColor(Color("1E3A8A"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .NumberFormat.Format = mini[g].Porcentaje ? "0.0%" : Moneda;
        }
        ws.Row(banda + 5).Height = 20;

        // ---------- Detalle del proveedor (una fórmula FILTER por columna, filtrado por RFC) ----------
        static string SerieFolio(Func<string, string> c) => $"{c("Serie")}&\"-\"&{c("Folio")}";
        string Texto(Func<string, string> c, string campo) => $"{c(campo)}&\"\"";
        // En la tabla agrupada la fecha es fecha de Excel (con formato); en las hojas del reporte general, texto.
        string Fecha(Func<string, string> c) => fu.Unica ? c("Fecha Emision") : Texto(c, "Fecha Emision");
        string Importe(Func<string, string> c, string campo, string signo) => $"{signo}{c(campo)}";

        (string Encabezado, Func<Func<string, string>, string> Valor, bool Dinero, bool EsFecha)[] Comprobantes(string signo) =>
        [
            ("FECHA", Fecha, false, fu.Unica), ("SERIE-FOLIO", SerieFolio, false, false), ("UUID", c => Texto(c, "UUID"), false, false),
            ("SUBTOTAL", c => Importe(c, "SubTotal", signo), true, false), (fu.Iva == "IVA" ? "IVA" : "IVA 16%", c => Importe(c, fu.Iva, signo), true, false),
            ("TOTAL", c => Importe(c, "Total", signo), true, false), ("ESTADO SAT", c => Texto(c, "Estado SAT"), false, false),
        ];

        (string Encabezado, Func<Func<string, string>, string> Valor, bool Dinero, bool EsFecha)[] pagos = fu.Unica
            ?
            [
                ("FECHA", Fecha, false, true), ("SERIE-FOLIO", SerieFolio, false, false), ("UUID", c => Texto(c, "UUID"), false, false),
                ("IMPORTE PAGADO", c => Importe(c, "Total", "-"), true, false), ("ESTADO SAT", c => Texto(c, "Estado SAT"), false, false),
            ]
            :
            [
                ("FECHA PAGO", c => $"LEFT({c("FechaPago")},10)", false, false), ("SERIE-FOLIO", SerieFolio, false, false), ("UUID", c => Texto(c, "UUID"), false, false),
                ("FORMA DE PAGO", c => Texto(c, "FormaDePagoP"), false, false), ("NUM. OPERACIÓN", c => Texto(c, "Num Operacion"), false, false),
                ("FACTURA PAGADA", c => $"{c("SerieRel")}&\"-\"&{c("FolioRel")}", false, false), ("IMPORTE PAGADO", c => c("ImpPagado"), true, false),
            ];

        (string Texto, string Color, string Vacio, int Filas, string Tabla, string Condicion, (string Encabezado, Func<Func<string, string>, string> Valor, bool Dinero, bool EsFecha)[] Columnas)[] bloques =
        [
            ("FACTURAS DEL PROVEEDOR", "17399A", "— sin facturas —", proveedores.Max(x => x.Facturas), fu.Facturas, fu.FiltroFacturas(rfcElegido), Comprobantes("")),
            ("PAGOS AL PROVEEDOR", "5B1BA0", "— sin pagos —", proveedores.Max(x => x.Pagos), fu.Pagos, fu.FiltroPagos(rfcElegido), pagos),
            ("NOTAS DE CRÉDITO DEL PROVEEDOR", "C2410C", "— sin notas de crédito —", proveedores.Max(x => x.Notas), fu.Notas, fu.FiltroNotas(rfcElegido), Comprobantes(fu.SignoNotas)),
        ];
        int[] columnas = [2, 3, 4, 6, 7, 8, 10];
        var fila = banda + 7;
        foreach (var (texto, color, vacio, maximo, tabla, condicion, campos) in bloques)
        {
            BandaCxp(ws, $"B{fila}:P{fila}", texto, color);
            for (var i = 0; i < campos.Length; i++) EncabezadoCxp(ws, ws.Cell(fila + 1, columnas[i]).Address.ToString()!, campos[i].Encabezado);
            var renglones = Math.Clamp(maximo + 3, 4, 1000);
            for (var k = 1; k <= renglones; k++)
            {
                var r = fila + 1 + k;
                for (var i = 0; i < campos.Length; i++)
                {
                    var celda = ws.Cell(r, columnas[i]);
                    var siVacio = i == 0 && k == 1 ? $"\"{vacio}\"" : "\"\"";
                    var indice = k;
                    celda.FormulaA1 = $"IFERROR({campos[i].Valor(campo => $"INDEX(_xlfn._xlws.FILTER({tabla}[{campo}],{condicion}),{indice})")},{siVacio})";
                    celda.Style.Font.SetFontSize(8);
                    if (campos[i].Dinero) celda.Style.NumberFormat.Format = Moneda;
                    else celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    if (campos[i].EsFecha) celda.Style.NumberFormat.Format = "dd/mm/yyyy";
                }
            }
            foreach (var rango in new[] { $"B{fila + 2}:D{fila + 1 + renglones}", $"F{fila + 2}:H{fila + 1 + renglones}", $"J{fila + 2}:J{fila + 1 + renglones}" })
                ws.Range(rango).AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(Color("F8FAFC"));
            fila += renglones + 3;
        }

        var nota = ws.Range($"B{fila}:P{fila}").Merge();
        ws.Cell($"B{fila}").Value = $"Generado por CFDI Manager el {DateTime.Now:dd/MM/yyyy HH:mm} · Fuente: {fu.Origen} · " +
                                    (fu.Unica ? "Pagado = monto total de los complementos de pago" : "Pagado = ImpPagado de los complementos de pago") +
                                    " · Un saldo negativo suele deberse a pagos de facturas de periodos anteriores.";
        nota.Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));

        ws.SetTabActive().Select();
    }

    private static void BandaCxp(IXLWorksheet ws, string rango, string texto, string fondo)
    {
        var r = ws.Range(rango).Merge();
        r.FirstCell().Value = texto;
        r.Style.Fill.SetBackgroundColor(Color(fondo)).Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(9)
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }

    private static void EncabezadoCxp(IXLWorksheet ws, string rango, string texto)
    {
        var r = ws.Range(rango);
        if (r.Cells().Count() > 1) r.Merge();
        r.FirstCell().Value = texto;
        r.Style.Fill.SetBackgroundColor(Color("E0E7FF")).Font.SetBold().Font.SetFontSize(8).Font.SetFontColor(Color("1E3A8A"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Alignment.SetWrapText();
    }

    private static void EstadosCxp(IXLRange rango)
    {
        // PARCIAL y PENDIENTE antes que PAGADO para que una no pinte a la otra.
        foreach (var (texto, fondo, letra) in new[] { ("PARCIAL", "FEF3C7", "92400E"), ("PENDIENTE", "FEE2E2", "991B1B"), ("PAGADO", "DCFCE7", "166534") })
            rango.AddConditionalFormat().WhenContains(texto).Fill.SetBackgroundColor(Color(fondo)).Font.SetFontColor(Color(letra));
    }
}
