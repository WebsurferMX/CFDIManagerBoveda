using ClosedXML.Excel;
using CFDIParser.Models;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Reportes agrupados por uso de CFDI, método de pago y tipo de comprobante: la hoja del grupo como tabla agrupada
/// (un grupo del esquema por valor con su renglón "Total …"), su hoja de resumen y un dashboard que lee solo de ellas:
/// KPIs, lista por importe, detalle del valor elegido en el dropdown, gráfico top 10 y sus comprobantes.
/// Uso y método llevan facturas y notas de crédito (en negativo); tipo lleva todos los comprobantes, con notas de
/// crédito y pagos en negativo en la tabla y en positivo en el resumen y el dashboard.
/// </summary>
public static partial class ReporteGeneral
{
    public enum Agrupacion { Uso, Metodo, Tipo }

    /// <summary>Textos, hojas y tablas de cada agrupación. <c>Columna</c> es el encabezado del grupo en la tabla.</summary>
    private sealed record Agrupado(
        string Columna, string Codigo, string Plural, string Singular, string Hoja, string Tabla, string HojaResumen, string TablaResumen,
        string Archivo, Func<AdminXmlFilaDto, string> Valor, Func<string, string> Descripcion, string SinValor, string[] Tipos);

    private static readonly Dictionary<string, string> NombresTipo = new()
    {
        ["I"] = "Factura", ["E"] = "Nota de crédito", ["P"] = "Pago", ["T"] = "Traslado", ["N"] = "Nómina",
    };

    private static Agrupado Datos(Agrupacion a) => a switch
    {
        Agrupacion.Uso => new("Uso CFDI", "USO", "usos de CFDI", "uso", "Por Uso CFDI", "TblPorUso", "Resumen Uso CFDI", "TblResumenUso",
            "Por Uso CFDI", x => x.UsoCfdi, c => Catalogos.Uso.GetValueOrDefault(c, "Uso no identificado"), "(sin uso)", ["I", "E"]),
        Agrupacion.Metodo => new("Método de pago", "MÉTODO", "métodos de pago", "método", "Por Metodo de Pago", "TblPorMetodo",
            "Resumen Metodo de Pago", "TblResumenMetodo", "Por Metodo de Pago", x => x.MetodoPago,
            c => Catalogos.Metodo.GetValueOrDefault(c, "Método no identificado"), "(sin método)", ["I", "E"]),
        _ => new("Tipo de comprobante", "TIPO", "tipos de comprobante", "tipo", "Por Tipo", "TblPorTipo", "Resumen Tipo", "TblResumenTipo",
            "Por Tipo", x => x.TipoCfdi, c => NombresTipo.GetValueOrDefault(c, c), "(sin tipo)", ["I", "E", "P", "T", "N"]),
    };

    public static (byte[] Bytes, string Nombre) CrearPorAgrupacion(IReadOnlyList<AdminXmlFilaDto> filas, Filtro filtro, bool recibidos, Agrupacion a)
    {
        var d = Datos(a);
        List<AdminXmlFilaDto> usados = filas.Where(x => d.Tipos.Contains(x.TipoCfdi)).ToList();

        string Codigo(AdminXmlFilaDto x) => d.Valor(x).Trim().ToUpperInvariant();
        string Clave(AdminXmlFilaDto x) => Codigo(x) is "" ? d.SinValor : $"{Codigo(x)} · {d.Descripcion(Codigo(x))}";
        static decimal Importe(AdminXmlFilaDto x) => x.TipoCfdi == "P" ? x.MontoTotalPagos ?? 0 : x.Total;

        // Uso y método: por importe neto (facturas menos notas de crédito). Tipo: en el orden del catálogo.
        var grupos = usados.GroupBy(Clave)
            .Select(g => (Clave: g.Key, Codigo: Codigo(g.First()), Neto: g.Sum(x => x.TipoCfdi == "E" ? -x.Total : Importe(x))))
            .ToList();
        grupos = a == Agrupacion.Tipo
            ? grupos.OrderBy(g => Array.IndexOf(d.Tipos, g.Codigo)).ToList()
            : grupos.OrderByDescending(g => g.Neto).ThenBy(g => g.Clave, StringComparer.Ordinal).ToList();

        var rfc = filtro.Rfc?.Trim().ToUpperInvariant() ?? "Todos";
        var (periodo, periodoCorto) = Periodo(filtro, usados.Select(x => new DateTimeOffset(x.FechaEmision)));

        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Segoe UI";
        EscribirTablaAgrupada(wb, d.Hoja, d.Tabla, d.Columna, usados, emisor: recibidos,
            grupos.Select(g => (g.Clave, g.Clave)).ToList(), Clave);

        var top = 0;
        if (grupos.Count > 0)
        {
            var codigos = grupos.Select(g => (g.Codigo, g.Clave)).ToList();
            var ultima = a == Agrupacion.Tipo
                ? EscribirResumenTipo(wb, d, codigos, periodo, recibidos, out top)
                : EscribirResumenFacturasNotas(wb, d, codigos, periodo, recibidos, out top);
            EscribirDashboardAgrupado(wb, d, a == Agrupacion.Tipo, grupos[0].Clave, ultima, rfc, periodo, recibidos);
        }

        wb.Worksheets.First().SetTabActive().Select();
        wb.FullCalculationOnLoad = true;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        if (grupos.Count > 0)
        {
            using var doc = SpreadsheetDocument.Open(stream, true);
            var wbPart = doc.WorkbookPart!;
            var dash = (WorksheetPart)wbPart.GetPartById(wbPart.Workbook.Sheets!.Elements<S.Sheet>().Single(x => x.Name == "Dashboard").Id!);
            GraficoTop(wbPart, dash, 4 + top, d.HojaResumen, $"Top 10 {d.Plural} por {(a == Agrupacion.Tipo ? "total" : "importe neto")}");
        }
        return (stream.ToArray(), $"{rfc} {(recibidos ? "Recibidas" : "Emitidas")} {periodoCorto} {d.Archivo}.xlsx");
    }

    /// <summary>Encabezado, títulos y anchos comunes de las hojas de resumen.</summary>
    private static IXLWorksheet HojaResumenAgrupado(XLWorkbook wb, Agrupado d, string titulo, string[] encabezados, double[] anchos)
    {
        var ws = wb.Worksheets.Add(d.HojaResumen, 1);
        ws.SetTabColor(XLColor.FromHtml("#0084BE"));
        ws.ShowGridLines = false;
        for (var i = 0; i < anchos.Length; i++) ws.Column(i + 1).Width = anchos[i];

        var rango = ws.Range(2, 1, 2, encabezados.Length).Merge();
        ws.Cell("A2").Value = titulo;
        rango.Style.Fill.SetBackgroundColor(Color("005A80")).Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.White)
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(2).Height = 26.1;
        for (var i = 0; i < encabezados.Length; i++) ws.Cell(4, i + 1).Value = encabezados[i];
        return ws;
    }

    /// <summary>Datos auxiliares del gráfico del dashboard (top 10 de la columna <paramref name="valor"/>).</summary>
    private static int AuxiliarGrafico(IXLWorksheet ws, string valor, string titulo, int ultima, int cuantos)
    {
        for (var r = 5; r <= ultima; r++) ws.Cell(r, 27).FormulaA1 = $"${valor}{r}";
        var top = Math.Max(1, Math.Min(10, cuantos));
        ws.Cell("AA1").Value = "Datos auxiliares del gráfico del Dashboard (no borrar)";
        ws.Cell("AA1").Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));
        ws.Cell("AA4").Value = titulo;
        ws.Cell("AC4").Value = "GRUPO";
        ws.Cell("AD4").Value = titulo;
        for (var i = 0; i < top; i++)
        {
            var r = 5 + i;
            ws.Cell(r, 30).FormulaA1 = $"IFERROR(LARGE($AA$5:$AA${ultima},ROW()-4),0)";
            ws.Cell(r, 29).FormulaA1 = $"IFERROR(INDEX($B$5:$B${ultima},MATCH(AD{r},$AA$5:$AA${ultima},0)),\"\")";
        }
        ws.Range($"AD5:AD{4 + top}").Style.NumberFormat.Format = Moneda;
        ws.Range($"AA5:AA{ultima}").Style.NumberFormat.Format = Moneda;
        ws.Column(27).Width = 18;
        ws.Column(29).Width = 52;
        ws.Column(30).Width = 18;
        return top;
    }

    /// <summary>Uso y método: A código, B grupo, C facturas, D facturado, E notas, F NC, G neto, H IVA neto, I %, J contrapartes.</summary>
    private static int EscribirResumenFacturasNotas(XLWorkbook wb, Agrupado d, List<(string Codigo, string Clave)> grupos, string periodo, bool recibidos, out int top)
    {
        var t = d.Tabla;
        var lado = recibidos ? "Emisor" : "Receptor";
        var ws = HojaResumenAgrupado(wb, d, $"RESUMEN POR {d.Columna.ToUpperInvariant()} · CFDI {(recibidos ? "RECIBIDOS" : "EMITIDOS")} {periodo}",
            [d.Codigo, d.Columna.ToUpperInvariant(), "FACTURAS", "FACTURADO", "NOTAS NC", "NOTAS DE CRÉDITO", "IMPORTE NETO", "IVA NETO", "% PART.",
             recibidos ? "PROVEEDORES" : "CLIENTES"],
            [9, 52, 11, 18, 11, 18, 18, 16, 10, 14]);
        var ultima = 4 + grupos.Count;
        // Busca por la columna del grupo en la tabla: los renglones "Total …" no coinciden.
        string De(int r, string tipo) => $"{t}[{d.Columna}],$B{r},{t}[Tipo],\"{tipo}\"";
        for (var i = 0; i < grupos.Count; i++)
        {
            var r = 5 + i;
            ws.Cell(r, 1).Value = grupos[i].Codigo;
            ws.Cell(r, 2).Value = grupos[i].Clave;
            ws.Cell(r, 3).FormulaA1 = $"COUNTIFS({De(r, "Factura")})";
            ws.Cell(r, 4).FormulaA1 = $"SUMIFS({t}[Total],{De(r, "Factura")})";
            ws.Cell(r, 5).FormulaA1 = $"COUNTIFS({De(r, "NotaCredito")})";
            ws.Cell(r, 6).FormulaA1 = $"-SUMIFS({t}[Total],{De(r, "NotaCredito")})";
            ws.Cell(r, 7).FormulaA1 = $"D{r}-F{r}";
            ws.Cell(r, 8).FormulaA1 = $"SUMIFS({t}[IVA],{t}[{d.Columna}],$B{r})";
            ws.Cell(r, 9).FormulaA1 = $"IFERROR(G{r}/SUM($G$5:$G${ultima}),0)";
            ws.Cell(r, 10).FormulaA1 = $"IFERROR(ROWS(_xlfn.UNIQUE(_xlfn._xlws.FILTER({t}[RFC {lado}],{t}[{d.Columna}]=$B{r}))),0)";
        }

        var tabla = ws.Range(4, 1, ultima, 10).CreateTable(d.TablaResumen);
        tabla.Theme = XLTableTheme.TableStyleMedium2;
        tabla.ShowRowStripes = true;
        tabla.ShowTotalsRow = true;
        tabla.Field(0).TotalsRowLabel = "TOTAL";
        tabla.Field(1).TotalsRowFormulaA1 = $"SUBTOTAL(103,{d.TablaResumen}[{d.Columna.ToUpperInvariant()}])&\" {d.Plural}\"";
        foreach (var campo in new[] { 2, 3, 4, 5, 6, 7, 8 }) tabla.Field(campo).TotalsRowFunction = XLTotalsRowFunction.Sum;

        foreach (var col in new[] { 4, 6, 7, 8 }) ws.Range(5, col, ultima + 1, col).Style.NumberFormat.Format = Moneda;
        ws.Range(5, 9, ultima + 1, 9).Style.NumberFormat.Format = "0.0%";
        foreach (var col in new[] { 1, 3, 5, 9, 10 }) ws.Range(5, col, ultima + 1, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(ultima + 1, 1, ultima + 1, 10).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(4);

        ws.Range($"D5:D{ultima}").AddConditionalFormat().DataBar(Color("0084BE")).LowestValue().HighestValue();
        ws.Range($"F5:F{ultima}").AddConditionalFormat().DataBar(Color("C00000")).LowestValue().HighestValue();
        ws.Range($"G5:G{ultima}").AddConditionalFormat().ColorScale()
            .LowestValue(Color("F2F9FD")).Midpoint(XLCFContentType.Percentile, 50, Color("9BDCF8")).HighestValue(Color("0084BE"));

        top = AuxiliarGrafico(ws, "G", "IMPORTE NETO", ultima, grupos.Count);
        return ultima;
    }

    /// <summary>
    /// Tipo: A código, B tipo, C comprobantes, D subtotal, E IVA, F total, G % de comprobantes, H contrapartes.
    /// Notas de crédito y pagos van en negativo en la tabla; aquí en positivo.
    /// </summary>
    private static int EscribirResumenTipo(XLWorkbook wb, Agrupado d, List<(string Codigo, string Clave)> grupos, string periodo, bool recibidos, out int top)
    {
        var t = d.Tabla;
        var lado = recibidos ? "Emisor" : "Receptor";
        var ws = HojaResumenAgrupado(wb, d, $"RESUMEN POR TIPO DE COMPROBANTE · CFDI {(recibidos ? "RECIBIDOS" : "EMITIDOS")} {periodo}",
            [d.Codigo, d.Columna.ToUpperInvariant(), "COMPROBANTES", "SUBTOTAL", "IVA", "TOTAL", "% COMPROB.", recibidos ? "PROVEEDORES" : "CLIENTES"],
            [9, 30, 14, 18, 16, 18, 12, 14]);
        var ultima = 4 + grupos.Count;
        for (var i = 0; i < grupos.Count; i++)
        {
            var r = 5 + i;
            var signo = grupos[i].Codigo is "E" or "P" ? "-" : "";
            ws.Cell(r, 1).Value = grupos[i].Codigo;
            ws.Cell(r, 2).Value = grupos[i].Clave;
            ws.Cell(r, 3).FormulaA1 = $"COUNTIFS({t}[{d.Columna}],$B{r})";
            ws.Cell(r, 4).FormulaA1 = $"{signo}SUMIFS({t}[SubTotal],{t}[{d.Columna}],$B{r})";
            ws.Cell(r, 5).FormulaA1 = $"{signo}SUMIFS({t}[IVA],{t}[{d.Columna}],$B{r})";
            ws.Cell(r, 6).FormulaA1 = $"{signo}SUMIFS({t}[Total],{t}[{d.Columna}],$B{r})";
            ws.Cell(r, 7).FormulaA1 = $"IFERROR(C{r}/SUM($C$5:$C${ultima}),0)";
            ws.Cell(r, 8).FormulaA1 = $"IFERROR(ROWS(_xlfn.UNIQUE(_xlfn._xlws.FILTER({t}[RFC {lado}],{t}[{d.Columna}]=$B{r}))),0)";
        }

        var tabla = ws.Range(4, 1, ultima, 8).CreateTable(d.TablaResumen);
        tabla.Theme = XLTableTheme.TableStyleMedium2;
        tabla.ShowRowStripes = true;
        tabla.ShowTotalsRow = true;
        tabla.Field(0).TotalsRowLabel = "TOTAL";
        tabla.Field(1).TotalsRowFormulaA1 = $"SUBTOTAL(103,{d.TablaResumen}[{d.Columna.ToUpperInvariant()}])&\" tipos\"";
        foreach (var campo in new[] { 2, 6 }) tabla.Field(campo).TotalsRowFunction = XLTotalsRowFunction.Sum;

        foreach (var col in new[] { 4, 5, 6 }) ws.Range(5, col, ultima, col).Style.NumberFormat.Format = Moneda;
        ws.Range(5, 7, ultima + 1, 7).Style.NumberFormat.Format = "0.0%";
        foreach (var col in new[] { 1, 3, 7, 8 }) ws.Range(5, col, ultima + 1, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(ultima + 1, 1, ultima + 1, 8).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(4);

        ws.Range($"C5:C{ultima}").AddConditionalFormat().DataBar(Color("00A0DC")).LowestValue().HighestValue();
        ws.Range($"F5:F{ultima}").AddConditionalFormat().DataBar(Color("0084BE")).LowestValue().HighestValue();

        top = AuxiliarGrafico(ws, "F", "TOTAL", ultima, grupos.Count);
        return ultima;
    }

    private static void EscribirDashboardAgrupado(XLWorkbook wb, Agrupado d, bool porTipo, string primero, int ultima, string rfc, string periodo, bool recibidos)
    {
        var t = d.Tabla;
        var lado = recibidos ? "Emisor" : "Receptor";
        var ws = wb.Worksheets.Add("Dashboard", 1);
        ws.SetTabColor(XLColor.FromHtml("#0B2E4F"));
        ws.ShowGridLines = false;
        ws.Style.Font.FontName = "Segoe UI";
        string Rc(string col) => $"'{d.HojaResumen}'!${col}$5:${col}${ultima}";

        ws.Column(1).Width = Ancho(10);
        ws.Column(17).Width = Ancho(10);
        foreach (var c in new[] { 5, 9, 13 }) ws.Column(c).Width = Ancho(12);
        foreach (var inicio in Grupos) ws.Columns(inicio, inicio + 2).Width = Ancho(72);
        (int Fila, double Alto)[] altos = [(1, 8), (2, 32), (3, 18), (4, 5), (5, 10), (6, 14), (7, 22), (8, 12), (9, 4), (10, 8), (11, 18), (12, 15), (13, 18), (28, 8), (29, 18), (30, 15)];
        foreach (var (fila, alto) in altos) ws.Row(fila).Height = alto;
        for (var r = 14; r <= 27; r++) ws.Row(r).Height = r is >= 15 and <= 19 ? 16 : 14;
        for (var r = 31; r <= 45; r++) ws.Row(r).Height = 13;

        Banda(ws, "B2:P2", $"ANÁLISIS POR {d.Columna.ToUpperInvariant()} · CFDI {(recibidos ? "RECIBIDOS" : "EMITIDOS")} {periodo}", "0B2E4F", 15, bold: true);
        Banda(ws, "B3:P3", null, "0084BE", 9, bold: false);
        string Cuenta(string tipo) => $"COUNTIFS({t}[Tipo],\"{tipo}\")";
        string Suma(string tipo) => $"SUMIFS({t}[Total],{t}[Tipo],\"{tipo}\")";
        ws.Cell("B3").FormulaA1 = $"\"{(recibidos ? "Receptor" : "Emisor")} {rfc} · \"&" +
            (porTipo ? $"COUNTA({t}[Tipo])&\" comprobantes · \"" : $"{Cuenta("Factura")}&\" facturas · \"&{Cuenta("NotaCredito")}&\" notas de crédito · \"") +
            $"&COUNTA({d.TablaResumen}[{d.Columna.ToUpperInvariant()}])&\" {d.Plural} · fuente: hoja {d.Hoja}\"";
        string[] franja = porTipo ? ["003A55", "C00000", "5B1BA0", "00B0F0"] : ["003A55", "C00000", "0084BE", "00B0F0"];
        for (var g = 0; g < 4; g++) ws.Range(4, Grupos[g], 4, Grupos[g] + 2).Style.Fill.BackgroundColor = Color(franja[g]);

        var principal = porTipo ? "" : $"\"Principal: \"&INDEX({Rc("A")},MATCH(MAX({Rc("G")}),{Rc("G")},0))&\" · \"&TEXT(MAX({Rc("I")}),\"0.0%\")";
        var tarjetas = porTipo
            ? new (string Titulo, string Valor, string Subtitulo, string[] Tonos, bool Dinero)[]
            {
                ("FACTURAS", Suma("Factura"), $"{Cuenta("Factura")}&\" facturas\"", ["003A55", "00587F", "0084BE", "00B0F0"], true),
                ("NOTAS DE CRÉDITO", $"-{Suma("NotaCredito")}",
                    $"IFERROR(TEXT(F7/B7,\"0.0%\"),\"0.0%\")&\" de lo facturado · \"&{Cuenta("NotaCredito")}&\" NC\"", ["7F0000", "A80000", "C00000", "E06666"], true),
                ("PAGOS", $"-{Suma("Pago")}", $"{Cuenta("Pago")}&\" complementos de pago\"", ["3B0F63", "5B1BA0", "7C3AED", "A855F7"], true),
                ("COMPROBANTES", $"SUM({Rc("C")})", $"COUNTA({Rc("B")})&\" tipos · \"&{Cuenta("Traslado")}&\" traslados · \"&{Cuenta("Nomina")}&\" nóminas\"",
                    ["0084BE", "00A0DC", "00B0F0", "8FDCF9"], false),
            }
            : new (string Titulo, string Valor, string Subtitulo, string[] Tonos, bool Dinero)[]
            {
                ("TOTAL FACTURADO", Suma("Factura"), $"{Cuenta("Factura")}&\" facturas\"", ["003A55", "00587F", "0084BE", "00B0F0"], true),
                ("NOTAS DE CRÉDITO", $"-{Suma("NotaCredito")}",
                    $"IFERROR(TEXT(F7/B7,\"0.0%\"),\"0.0%\")&\" del facturado · \"&{Cuenta("NotaCredito")}&\" NC\"", ["7F0000", "A80000", "C00000", "E06666"], true),
                ("IMPORTE NETO", "B7-F7", $"\"IVA neto \"&TEXT(SUM({Rc("H")}),\"$#,##0\")", ["00587F", "0084BE", "00A0DC", "66CFF7"], true),
                (d.Plural.ToUpperInvariant(), $"COUNTA({Rc("B")})", principal, ["0084BE", "00A0DC", "00B0F0", "8FDCF9"], false),
            };
        for (var g = 0; g < 4; g++)
        {
            var k = tarjetas[g];
            for (var r = 6; r <= 9; r++)
                ws.Range(r, Grupos[g], r, Grupos[g] + 2).Merge().Style.Fill.SetBackgroundColor(Color(k.Tonos[r - 6])).Font.SetFontColor(XLColor.White)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            var col = Grupos[g];
            ws.Cell(6, col).Value = k.Titulo;
            ws.Cell(6, col).Style.Font.SetBold().Font.SetFontSize(8);
            ws.Cell(7, col).FormulaA1 = k.Valor;
            ws.Cell(7, col).Style.Font.SetBold().Font.SetFontSize(13);
            if (k.Dinero) ws.Cell(7, col).Style.NumberFormat.Format = Moneda;
            ws.Cell(8, col).FormulaA1 = k.Subtitulo;
            ws.Cell(8, col).Style.Font.SetFontSize(7.5);
        }

        // ---------- Lista por grupo ----------
        Banda(ws, "B11:H11", $"{d.Plural.ToUpperInvariant()} POR {(porTipo ? "TOTAL" : "IMPORTE NETO")}", "003A55", 9, bold: true);
        Banda(ws, "J11:P11", $"DETALLE POR {d.Singular.ToUpperInvariant()}", "00587F", 9, bold: true);
        Encabezado(ws, "B12:D12", d.Columna.ToUpperInvariant(), indent: true);
        (string Titulo, string Fuente, string Formato)[] lista = porTipo
            ? [("COMPROBANTES", "C", "#,##0"), ("SUBTOTAL", "D", Moneda), ("TOTAL", "F", Moneda)]
            : [("FACTURADO", "D", Moneda), ("NOTAS DE CRÉDITO", "F", Moneda), ("IMPORTE NETO", "G", Moneda)];
        for (var i = 0; i < 3; i++) Encabezado(ws, ws.Cell(12, 6 + i).Address.ToString()!, lista[i].Titulo);
        Encabezado(ws, "J12:L12", $"SELECCIONA {d.Singular.ToUpperInvariant()}", indent: true);
        Encabezado(ws, "N12:P12", recibidos ? "PROVEEDORES" : "CLIENTES", indent: true);
        for (var r = 13; r <= 27; r++)
        {
            ws.Range(r, 2, r, 4).Merge();
            ws.Cell(r, 2).FormulaA1 = $"IFERROR(INDEX({Rc("B")},ROW()-12),\"\")";
            ws.Cell(r, 2).Style.Font.SetFontSize(8.5).Alignment.SetIndent(1);
            for (var i = 0; i < 3; i++)
            {
                ws.Cell(r, 6 + i).FormulaA1 = $"IFERROR(INDEX({Rc(lista[i].Fuente)},ROW()-12),\"\")";
                ws.Cell(r, 6 + i).Style.Font.SetFontSize(8.5).NumberFormat.Format = lista[i].Formato;
            }
        }
        ws.Range("B13:D27").AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(Color("F2F9FD"));
        ws.Range("F13:H27").AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(Color("F2F9FD"));
        ws.Range("F13:F27").AddConditionalFormat().DataBar(Color("00A0DC")).LowestValue().HighestValue();
        ws.Range("G13:G27").AddConditionalFormat().DataBar(Color("EE8B3A")).LowestValue().HighestValue();
        ws.Range("H13:H27").AddConditionalFormat().ColorScale()
            .LowestValue(Color("F2F9FD")).Midpoint(XLCFContentType.Percentile, 50, Color("9BDCF8")).HighestValue(Color("0084BE"));

        // ---------- Detalle del grupo elegido ----------
        var selector = ws.Range("J13:L13").Merge();
        ws.Cell("J13").Value = primero;
        selector.Style.Fill.SetBackgroundColor(Color("FFF7D6")).Font.SetBold().Font.SetFontSize(8.5).Font.SetFontColor(Color("003A55"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetShrinkToFit();
        selector.CreateDataValidation().List(Rc("B"), true);
        var cuantos = ws.Range("N13:P13").Merge();
        ws.Cell("N13").FormulaA1 = $"IFERROR(INDEX({Rc(porTipo ? "H" : "J")},MATCH($J$13,{Rc("B")},0)),0)";
        cuantos.Style.Fill.SetBackgroundColor(Color("EDF6FB")).Font.SetBold().Font.SetFontSize(9).Font.SetFontColor(Color("003A55"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);

        string Del(string tipo) => $"{t}[{d.Columna}],$J$13,{t}[Tipo],\"{tipo}\"";
        string DelGrupo(string col) => $"INDEX({Rc(col)},MATCH($J$13,{Rc("B")},0))";
        var mini = porTipo
            ? new (string Titulo, string Tono, bool LetraOscura, string Formula, string Formato)[]
            {
                ("COMPROBANTES", "003A55", false, DelGrupo("C"), "#,##0"),
                ("SUBTOTAL", "00587F", false, DelGrupo("D"), Moneda),
                ("IVA", "0084BE", false, DelGrupo("E"), Moneda),
                ("TOTAL", "00A0DC", false, DelGrupo("F"), Moneda),
                ("% DE LOS COMPROBANTES", "66CFF7", true, DelGrupo("G"), "0.0%"),
            }
            : new (string Titulo, string Tono, bool LetraOscura, string Formula, string Formato)[]
            {
                ("FACTURADO", "003A55", false, $"SUMIFS({t}[Total],{Del("Factura")})", Moneda),
                ("NOTAS DE CRÉDITO", "A80000", false, $"-SUMIFS({t}[Total],{Del("NotaCredito")})", Moneda),
                ("IMPORTE NETO", "0084BE", false, "N15-N16", Moneda),
                ("IVA NETO", "00A0DC", false, $"SUMIFS({t}[IVA],{t}[{d.Columna}],$J$13)", Moneda),
                ("FACTURAS / NC", "66CFF7", true, $"COUNTIFS({Del("Factura")})&\" / \"&COUNTIFS({Del("NotaCredito")})", "@"),
            };
        for (var i = 0; i < mini.Length; i++)
        {
            var r = 15 + i;
            var etiqueta = ws.Range(r, 10, r, 12).Merge();
            ws.Cell(r, 10).Value = mini[i].Titulo;
            etiqueta.Style.Fill.SetBackgroundColor(Color(mini[i].Tono)).Font.SetBold().Font.SetFontSize(8)
                .Font.SetFontColor(mini[i].LetraOscura ? Color("003A55") : XLColor.White)
                .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            var valor = ws.Range(r, 14, r, 16).Merge();
            ws.Cell(r, 14).FormulaA1 = $"IFERROR({mini[i].Formula},0)";
            valor.Style.Fill.SetBackgroundColor(Color("F5F9FC")).Font.SetBold().Font.SetFontSize(9).Font.SetFontColor(Color("003A55"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            if (mini[i].Formato != "@") valor.Style.NumberFormat.Format = mini[i].Formato;
        }

        // Comprobantes del grupo: una fórmula FILTER por columna, en el orden de la tabla (los "Total …" no coinciden).
        Banda(ws, "B29:P29", $"COMPROBANTES DEL {d.Singular.ToUpperInvariant()} SELECCIONADO (primeros 15)", "0084BE", 9, bold: true);
        var condicion = $"{t}[{d.Columna}]=$J$13";
        string Celda(string campo, string k) => $"INDEX(_xlfn._xlws.FILTER({t}[{campo}],{condicion}),{k})";
        (int Col, string Titulo, Func<string, string> Valor, string? Formato)[] detalle =
        [
            (2, "FECHA", k => Celda("Fecha Emision", k), "dd/mm/yyyy"),
            (3, "SERIE-FOLIO", k => $"{Celda("Serie", k)}&\"-\"&{Celda("Folio", k)}", null),
            (4, recibidos ? "PROVEEDOR" : "CLIENTE", k => $"{Celda($"Nombre {lado}", k)}&\"\"", null),
            (6, "TIPO", k => $"{Celda("Tipo", k)}&\"\"", null),
            (7, "SUBTOTAL", k => Celda("SubTotal", k), Moneda),
            (8, "IVA", k => Celda("IVA", k), Moneda),
            (10, "TOTAL", k => Celda("Total", k), Moneda),
            (11, porTipo ? "USO" : "MÉTODO", k => $"{Celda(porTipo ? "UsoCFDI" : "Metodo de Pago", k)}&\"\"", null),
            (12, "RFC", k => $"{Celda($"RFC {lado}", k)}&\"\"", null),
            (14, "UUID", k => $"{Celda("UUID", k)}&\"\"", null),
        ];
        foreach (var (col, titulo, valor, formato) in detalle)
        {
            Encabezado(ws, ws.Cell(30, col).Address.ToString()!, titulo, centrado: true);
            for (var r = 31; r <= 45; r++)
            {
                var celda = ws.Cell(r, col);
                celda.FormulaA1 = $"IFERROR({valor("ROW()-30")},\"\")";
                celda.Style.Font.SetFontSize(8);
                if (formato is not null) celda.Style.NumberFormat.Format = formato;
                else celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            }
        }
        foreach (var rango in new[] { "B31:D45", "F31:H45", "J31:L45", "N31:P45" })
            ws.Range(rango).AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=1").Fill.SetBackgroundColor(Color("F7F7F7"));

        var nota = ws.Range("B47:P47").Merge();
        ws.Cell("B47").Value = $"Generado por CFDI Manager el {DateTime.Now:dd/MM/yyyy HH:mm} · Fuente: hojas {d.Hoja} y {d.HojaResumen} · " +
                               (porTipo
                                   ? "Notas de crédito y pagos van en negativo en la hoja de detalle; el resumen y el dashboard los muestran en positivo. Los pagos suman el monto pagado."
                                   : "Solo facturas y notas de crédito; las notas van en negativo en la hoja de detalle y el dashboard las muestra en positivo.");
        nota.Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));
    }
}
