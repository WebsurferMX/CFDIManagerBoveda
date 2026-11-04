using ClosedXML.Excel;
using CFDIParser.Models;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Reporte "Agrupado por uso de CFDI": la hoja Por Uso CFDI como tabla agrupada por uso (TblPorUso), la hoja Resumen
/// Uso CFDI y un dashboard que lee solo de esas dos: KPIs, usos por importe neto, detalle del uso elegido en el
/// dropdown, gráfico top 10 y los comprobantes de ese uso. Solo facturas y notas de crédito (en negativo).
/// </summary>
public static partial class ReporteGeneral
{
    private const string TablaPorUso = "TblPorUso";
    private const string HojaResumenUso = "Resumen Uso CFDI";

    public static (byte[] Bytes, string Nombre) CrearPorUso(IReadOnlyList<AdminXmlFilaDto> filas, Filtro filtro, bool recibidos)
    {
        List<AdminXmlFilaDto> usados = filas.Where(x => x.TipoCfdi is "I" or "E").ToList();

        static string Codigo(AdminXmlFilaDto x) => x.UsoCfdi.Trim().ToUpperInvariant();
        static string Clave(AdminXmlFilaDto x) => Codigo(x) is "" ? "(sin uso)"
            : $"{Codigo(x)} · {Catalogos.Uso.GetValueOrDefault(Codigo(x), "Uso no identificado")}";

        // Usos ordenados por importe neto (facturas menos notas de crédito).
        var usos = usados.GroupBy(Clave)
            .Select(g => (Clave: g.Key, Codigo: Codigo(g.First()), Neto: g.Sum(x => x.TipoCfdi == "E" ? -x.Total : x.Total)))
            .OrderByDescending(u => u.Neto).ThenBy(u => u.Clave, StringComparer.Ordinal).ToList();

        var rfc = filtro.Rfc?.Trim().ToUpperInvariant() ?? "Todos";
        var (periodo, periodoCorto) = Periodo(filtro, usados.Select(x => new DateTimeOffset(x.FechaEmision)));

        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Segoe UI";
        EscribirTablaAgrupada(wb, "Por Uso CFDI", TablaPorUso, "Uso CFDI", usados, emisor: recibidos,
            usos.Select(u => (u.Clave, u.Clave)).ToList(), Clave);

        var top = 0;
        if (usos.Count > 0)
        {
            var ultima = EscribirResumenUso(wb, usos.Select(u => (u.Codigo, u.Clave)).ToList(), periodo, recibidos, out top);
            EscribirDashboardUso(wb, usos[0].Clave, ultima, rfc, periodo, recibidos);
        }

        wb.Worksheets.First().SetTabActive().Select();
        wb.FullCalculationOnLoad = true;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        if (usos.Count > 0)
        {
            using var doc = SpreadsheetDocument.Open(stream, true);
            var wbPart = doc.WorkbookPart!;
            var dash = (WorksheetPart)wbPart.GetPartById(wbPart.Workbook.Sheets!.Elements<S.Sheet>().Single(x => x.Name == "Dashboard").Id!);
            GraficoTop(wbPart, dash, 4 + top, HojaResumenUso, "Top 10 usos de CFDI por importe neto");
        }
        return (stream.ToArray(), $"{rfc} {(recibidos ? "Recibidas" : "Emitidas")} {periodoCorto} Por Uso CFDI.xlsx");
    }

    private static int EscribirResumenUso(XLWorkbook wb, List<(string Codigo, string Clave)> usos, string periodo, bool recibidos, out int top)
    {
        const string t = TablaPorUso;
        var lado = recibidos ? "Emisor" : "Receptor";
        var contrapartes = recibidos ? "PROVEEDORES" : "CLIENTES";
        var ws = wb.Worksheets.Add(HojaResumenUso, 1);
        ws.SetTabColor(XLColor.FromHtml("#0084BE"));
        ws.ShowGridLines = false;
        double[] anchos = [9, 52, 11, 18, 11, 18, 18, 16, 10, 14];
        for (var i = 0; i < anchos.Length; i++) ws.Column(i + 1).Width = anchos[i];

        var titulo = ws.Range("A2:J2").Merge();
        ws.Cell("A2").Value = $"RESUMEN POR USO DE CFDI · CFDI {(recibidos ? "RECIBIDOS" : "EMITIDOS")} {periodo}";
        titulo.Style.Fill.SetBackgroundColor(Color("005A80")).Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.White)
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(2).Height = 26.1;

        string[] encabezados = ["USO", "USO CFDI", "FACTURAS", "FACTURADO", "NOTAS NC", "NOTAS DE CRÉDITO", "IMPORTE NETO", "IVA NETO", "% PART.", contrapartes];
        for (var i = 0; i < encabezados.Length; i++) ws.Cell(4, i + 1).Value = encabezados[i];
        var ultima = 4 + usos.Count;
        // Busca por la columna "Uso CFDI" de la tabla (la del grupo): los renglones "Total …" no coinciden.
        string De(int r, string tipo) => $"{t}[Uso CFDI],$B{r},{t}[Tipo],\"{tipo}\"";
        for (var i = 0; i < usos.Count; i++)
        {
            var r = 5 + i;
            ws.Cell(r, 1).Value = usos[i].Codigo;
            ws.Cell(r, 2).Value = usos[i].Clave;
            ws.Cell(r, 3).FormulaA1 = $"COUNTIFS({De(r, "Factura")})";
            ws.Cell(r, 4).FormulaA1 = $"SUMIFS({t}[Total],{De(r, "Factura")})";
            ws.Cell(r, 5).FormulaA1 = $"COUNTIFS({De(r, "NotaCredito")})";
            ws.Cell(r, 6).FormulaA1 = $"-SUMIFS({t}[Total],{De(r, "NotaCredito")})";
            ws.Cell(r, 7).FormulaA1 = $"D{r}-F{r}";
            ws.Cell(r, 8).FormulaA1 = $"SUMIFS({t}[IVA],{t}[Uso CFDI],$B{r})";
            ws.Cell(r, 9).FormulaA1 = $"IFERROR(G{r}/SUM($G$5:$G${ultima}),0)";
            ws.Cell(r, 10).FormulaA1 = $"IFERROR(ROWS(_xlfn.UNIQUE(_xlfn._xlws.FILTER({t}[RFC {lado}],{t}[Uso CFDI]=$B{r}))),0)";
            ws.Cell(r, 27).FormulaA1 = $"$G{r}";
        }

        var tabla = ws.Range(4, 1, ultima, 10).CreateTable("TblResumenUso");
        tabla.Theme = XLTableTheme.TableStyleMedium2;
        tabla.ShowRowStripes = true;
        tabla.ShowTotalsRow = true;
        tabla.Field("USO").TotalsRowLabel = "TOTAL";
        tabla.Field("USO CFDI").TotalsRowFormulaA1 = "SUBTOTAL(103,TblResumenUso[USO CFDI])&\" usos\"";
        foreach (var campo in new[] { "FACTURAS", "FACTURADO", "NOTAS NC", "NOTAS DE CRÉDITO", "IMPORTE NETO", "IVA NETO", "% PART." })
            tabla.Field(campo).TotalsRowFunction = XLTotalsRowFunction.Sum;

        foreach (var col in new[] { 4, 6, 7, 8 }) ws.Range(5, col, ultima + 1, col).Style.NumberFormat.Format = Moneda;
        ws.Range(5, 9, ultima + 1, 9).Style.NumberFormat.Format = "0.0%";
        foreach (var col in new[] { 1, 3, 5, 9, 10 }) ws.Range(5, col, ultima + 1, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(ultima + 1, 1, ultima + 1, 10).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(4);

        ws.Range($"D5:D{ultima}").AddConditionalFormat().DataBar(Color("0084BE")).LowestValue().HighestValue();
        ws.Range($"F5:F{ultima}").AddConditionalFormat().DataBar(Color("C00000")).LowestValue().HighestValue();
        ws.Range($"G5:G{ultima}").AddConditionalFormat().ColorScale()
            .LowestValue(Color("F2F9FD")).Midpoint(XLCFContentType.Percentile, 50, Color("9BDCF8")).HighestValue(Color("0084BE"));

        // Datos auxiliares del gráfico del dashboard: top 10 por importe neto.
        top = Math.Max(1, Math.Min(10, usos.Count));
        ws.Cell("AA1").Value = "Datos auxiliares del gráfico del Dashboard (no borrar)";
        ws.Cell("AA1").Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));
        ws.Cell("AA4").Value = "IMPORTE NETO";
        ws.Cell("AC4").Value = "USO CFDI";
        ws.Cell("AD4").Value = "IMPORTE NETO";
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
        return ultima;
    }

    private static void EscribirDashboardUso(XLWorkbook wb, string primero, int ultima, string rfc, string periodo, bool recibidos)
    {
        const string t = TablaPorUso;
        var lado = recibidos ? "Emisor" : "Receptor";
        var ws = wb.Worksheets.Add("Dashboard", 1);
        ws.SetTabColor(XLColor.FromHtml("#0B2E4F"));
        ws.ShowGridLines = false;
        ws.Style.Font.FontName = "Segoe UI";
        string Rc(string col) => $"'{HojaResumenUso}'!${col}$5:${col}${ultima}";

        ws.Column(1).Width = Ancho(10);
        ws.Column(17).Width = Ancho(10);
        foreach (var c in new[] { 5, 9, 13 }) ws.Column(c).Width = Ancho(12);
        foreach (var inicio in Grupos) ws.Columns(inicio, inicio + 2).Width = Ancho(72);
        (int Fila, double Alto)[] altos = [(1, 8), (2, 32), (3, 18), (4, 5), (5, 10), (6, 14), (7, 22), (8, 12), (9, 4), (10, 8), (11, 18), (12, 15), (13, 18), (28, 8), (29, 18), (30, 15)];
        foreach (var (fila, alto) in altos) ws.Row(fila).Height = alto;
        for (var r = 14; r <= 27; r++) ws.Row(r).Height = r is >= 15 and <= 19 ? 16 : 14;
        for (var r = 31; r <= 45; r++) ws.Row(r).Height = 13;

        Banda(ws, "B2:P2", $"ANÁLISIS POR USO DE CFDI · CFDI {(recibidos ? "RECIBIDOS" : "EMITIDOS")} {periodo}", "0B2E4F", 15, bold: true);
        Banda(ws, "B3:P3", null, "0084BE", 9, bold: false);
        ws.Cell("B3").FormulaA1 = $"\"{(recibidos ? "Receptor" : "Emisor")} {rfc} · \"&COUNTIFS({t}[Tipo],\"Factura\")&\" facturas · \"" +
                                  $"&COUNTIFS({t}[Tipo],\"NotaCredito\")&\" notas de crédito · \"&COUNTA(TblResumenUso[USO CFDI])&\" usos de CFDI · fuente: hoja Por Uso CFDI\"";
        string[] franja = ["003A55", "C00000", "0084BE", "00B0F0"];
        for (var g = 0; g < 4; g++) ws.Range(4, Grupos[g], 4, Grupos[g] + 2).Style.Fill.BackgroundColor = Color(franja[g]);

        string Suma(string tipo) => $"SUMIFS({t}[Total],{t}[Tipo],\"{tipo}\")";
        string Cuenta(string tipo) => $"COUNTIFS({t}[Tipo],\"{tipo}\")";
        var tarjetas = new (string Titulo, string Valor, string Subtitulo, string[] Tonos, bool Dinero)[]
        {
            ("TOTAL FACTURADO", Suma("Factura"), $"{Cuenta("Factura")}&\" facturas\"", ["003A55", "00587F", "0084BE", "00B0F0"], true),
            ("NOTAS DE CRÉDITO", $"-{Suma("NotaCredito")}",
                $"IFERROR(TEXT(F7/B7,\"0.0%\"),\"0.0%\")&\" del facturado · \"&{Cuenta("NotaCredito")}&\" NC\"", ["7F0000", "A80000", "C00000", "E06666"], true),
            ("IMPORTE NETO", "B7-F7", $"\"IVA neto \"&TEXT(SUM({Rc("H")}),\"$#,##0\")", ["00587F", "0084BE", "00A0DC", "66CFF7"], true),
            ("USOS DE CFDI", $"COUNTA({Rc("B")})", $"\"Principal: \"&INDEX({Rc("A")},MATCH(MAX({Rc("G")}),{Rc("G")},0))&\" · \"&TEXT(MAX({Rc("I")}),\"0.0%\")",
                ["0084BE", "00A0DC", "00B0F0", "8FDCF9"], false),
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

        // ---------- Usos por importe neto ----------
        Banda(ws, "B11:H11", "USOS DE CFDI POR IMPORTE NETO", "003A55", 9, bold: true);
        Banda(ws, "J11:P11", "DETALLE POR USO DE CFDI", "00587F", 9, bold: true);
        Encabezado(ws, "B12:D12", "USO CFDI", indent: true);
        Encabezado(ws, "F12", "FACTURADO");
        Encabezado(ws, "G12", "NOTAS DE CRÉDITO");
        Encabezado(ws, "H12", "IMPORTE NETO");
        Encabezado(ws, "J12:L12", "SELECCIONA USO", indent: true);
        Encabezado(ws, "N12:P12", recibidos ? "PROVEEDORES" : "CLIENTES", indent: true);
        for (var r = 13; r <= 27; r++)
        {
            ws.Range(r, 2, r, 4).Merge();
            ws.Cell(r, 2).FormulaA1 = $"IFERROR(INDEX({Rc("B")},ROW()-12),\"\")";
            ws.Cell(r, 2).Style.Font.SetFontSize(8.5).Alignment.SetIndent(1);
            foreach (var (c, fuente) in new[] { (6, "D"), (7, "F"), (8, "G") })
            {
                ws.Cell(r, c).FormulaA1 = $"IFERROR(INDEX({Rc(fuente)},ROW()-12),\"\")";
                ws.Cell(r, c).Style.Font.SetFontSize(8.5).NumberFormat.Format = Moneda;
            }
        }
        ws.Range("B13:D27").AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(Color("F2F9FD"));
        ws.Range("F13:H27").AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(Color("F2F9FD"));
        ws.Range("F13:F27").AddConditionalFormat().DataBar(Color("00A0DC")).LowestValue().HighestValue();
        ws.Range("G13:G27").AddConditionalFormat().DataBar(Color("EE8B3A")).LowestValue().HighestValue();
        ws.Range("H13:H27").AddConditionalFormat().ColorScale()
            .LowestValue(Color("F2F9FD")).Midpoint(XLCFContentType.Percentile, 50, Color("9BDCF8")).HighestValue(Color("0084BE"));

        // ---------- Detalle del uso elegido ----------
        var selector = ws.Range("J13:L13").Merge();
        ws.Cell("J13").Value = primero;
        selector.Style.Fill.SetBackgroundColor(Color("FFF7D6")).Font.SetBold().Font.SetFontSize(8.5).Font.SetFontColor(Color("003A55"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetShrinkToFit();
        selector.CreateDataValidation().List(Rc("B"), true);
        var cuantos = ws.Range("N13:P13").Merge();
        ws.Cell("N13").FormulaA1 = $"IFERROR(INDEX({Rc("J")},MATCH($J$13,{Rc("B")},0)),0)";
        cuantos.Style.Fill.SetBackgroundColor(Color("EDF6FB")).Font.SetBold().Font.SetFontSize(9).Font.SetFontColor(Color("003A55"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);

        string DelUso(string tipo) => $"{t}[Uso CFDI],$J$13,{t}[Tipo],\"{tipo}\"";
        var mini = new (string Titulo, string Tono, bool LetraOscura, string Formula, string Formato)[]
        {
            ("FACTURADO", "003A55", false, $"SUMIFS({t}[Total],{DelUso("Factura")})", Moneda),
            ("NOTAS DE CRÉDITO", "A80000", false, $"-SUMIFS({t}[Total],{DelUso("NotaCredito")})", Moneda),
            ("IMPORTE NETO", "0084BE", false, "N15-N16", Moneda),
            ("IVA NETO", "00A0DC", false, $"SUMIFS({t}[IVA],{t}[Uso CFDI],$J$13)", Moneda),
            ("FACTURAS / NC", "66CFF7", true, $"COUNTIFS({DelUso("Factura")})&\" / \"&COUNTIFS({DelUso("NotaCredito")})", "@"),
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

        // Comprobantes del uso: una fórmula FILTER por columna (facturas y notas de crédito, en el orden de la tabla).
        Banda(ws, "B29:P29", "COMPROBANTES DEL USO SELECCIONADO (primeros 15)", "0084BE", 9, bold: true);
        var condicion = $"({t}[Uso CFDI]=$J$13)*(({t}[Tipo]=\"Factura\")+({t}[Tipo]=\"NotaCredito\"))";
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
            (11, "MÉTODO", k => $"{Celda("Metodo de Pago", k)}&\"\"", null),
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
        ws.Cell("B47").Value = $"Generado por CFDI Manager el {DateTime.Now:dd/MM/yyyy HH:mm} · Fuente: hojas Por Uso CFDI y Resumen Uso CFDI · " +
                               "Solo facturas y notas de crédito; las notas van en negativo en la hoja Por Uso CFDI y el dashboard las muestra en positivo.";
        nota.Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));
    }
}
