using ClosedXML.Excel;
using CFDIParser.Models;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Reporte de conceptos: la hoja Conceptos como tabla (TblConceptos), la hoja Resumen Conceptos por clave SAT
/// (ClaveProdServ) y un dashboard que lee solo de esas dos: KPIs de importe, notas de crédito, importe neto y claves,
/// top 15 claves, gráfico top 10 y detalle de las partidas de la clave elegida en el dropdown.
/// </summary>
public static partial class ReporteGeneral
{
    private const string TablaConceptos = "TblConceptos";
    private const string HojaResumenConceptos = "Resumen Conceptos";

    public sealed record Partida(AdminXmlFilaDto Cfdi, ConceptoDto Concepto);

    private sealed record ClaveSat(string Clave, string Etiqueta, decimal Neto);

    public static (byte[] Bytes, string Nombre) CrearConceptos(IReadOnlyList<Partida> partidas, Filtro filtro)
    {
        var recibidos = partidas.Count > 0 && partidas.All(p => p.Cfdi.Clasificacion == "RECIBIDO");
        var emitidos = partidas.Count > 0 && partidas.All(p => p.Cfdi.Clasificacion != "RECIBIDO");
        var uno = recibidos ? "PROVEEDOR" : emitidos ? "CLIENTE" : "CONTRAPARTE";
        var rfc = filtro.Rfc?.Trim().ToUpperInvariant() ?? "Todos";
        var (periodo, periodoCorto) = Periodo(filtro, partidas.Select(p => new DateTimeOffset(p.Cfdi.FechaEmision)));

        static bool EsTraslado(ImpuestoDto i) => i.Tipo.StartsWith("TRAS", StringComparison.OrdinalIgnoreCase);
        static string Tipo(AdminXmlFilaDto x) => x.TipoCfdi switch
        {
            "I" => "Factura", "E" => "NotaCredito", "T" => "Traslado", "P" => "Pago", "N" => "Nomina", var t => t
        };

        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Segoe UI";

        // ---------- Conceptos (tabla) ----------
        var ws = wb.Worksheets.Add("Conceptos");
        ws.SetTabColor(XLColor.FromHtml("#00B0F0"));
        string[] columnas =
        [
            "Serie", "Folio", "UUID", "Estado SAT", "Tipo", "Fecha Emision", "RFC Emisor", "Nombre Emisor",
            "RFC Receptor", "Nombre Receptor", "Contraparte", "ClaveProdServ", "NoIdentificacion", "Cantidad", "ClaveUnidad", "Unidad",
            "Descripcion", "ValorUnitario", "Descuento", "Importe", "IVA Trasladado", "IVA Retenido", "ISR Retenido", "Moneda", "Archivo XML"
        ];
        for (var c = 0; c < columnas.Length; c++) ws.Cell(1, c + 1).Value = columnas[c];
        var r = 2;
        foreach (var (x, k) in partidas)
        {
            object?[] valores =
            [
                x.Serie, x.Folio, x.Uuid, x.EstadoSat, Tipo(x), x.FechaEmision, x.RfcEmisor, x.NombreEmisor, x.RfcReceptor, x.NombreReceptor,
                x.Clasificacion == "RECIBIDO" ? x.NombreEmisor : x.NombreReceptor,
                k.ClaveProdServ, k.NoIdentificacion, k.Cantidad, k.ClaveUnidad, k.Unidad, k.Descripcion, k.ValorUnitario, k.Descuento ?? 0, k.Importe,
                k.Impuestos.Where(i => EsTraslado(i) && i.Impuesto == "002").Sum(i => i.Importe),
                k.Impuestos.Where(i => !EsTraslado(i) && i.Impuesto == "002").Sum(i => i.Importe),
                k.Impuestos.Where(i => !EsTraslado(i) && i.Impuesto == "001").Sum(i => i.Importe),
                x.Moneda, x.RutaXml
            ];
            for (var c = 0; c < valores.Length; c++)
                ws.Cell(r, c + 1).Value = valores[c] switch { null => Blank.Value, decimal d => d, DateTime f => f, var o => XLCellValue.FromObject(o, Inv) };
            r++;
        }
        var tabla = ws.Range(1, 1, Math.Max(2, r - 1), columnas.Length).CreateTable(TablaConceptos);
        tabla.Theme = XLTableTheme.TableStyleMedium2;
        tabla.ShowTotalsRow = true;
        tabla.Field("Serie").TotalsRowLabel = "TOTAL";
        tabla.Field("UUID").TotalsRowFunction = XLTotalsRowFunction.Count;
        foreach (var campo in new[] { "Cantidad", "Descuento", "Importe", "IVA Trasladado", "IVA Retenido", "ISR Retenido" })
        {
            tabla.Field(campo).TotalsRowFunction = XLTotalsRowFunction.Sum;
            if (campo != "Cantidad") ws.Column(Array.IndexOf(columnas, campo) + 1).Style.NumberFormat.Format = Contable;
        }
        ws.Column(Array.IndexOf(columnas, "ValorUnitario") + 1).Style.NumberFormat.Format = Contable;
        ws.Column(Array.IndexOf(columnas, "Fecha Emision") + 1).Style.NumberFormat.Format = "dd/mm/yyyy hh:mm";
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, columnas.Length).AdjustToContents(1, Math.Min(r, 300), 8, 50);

        // ---------- Claves SAT (facturas y notas de crédito) ----------
        var claves = partidas.Where(p => p.Cfdi.TipoCfdi is "I" or "E").GroupBy(p => p.Concepto.ClaveProdServ.Trim()).Select(g =>
        {
            var descripcion = g.Where(p => p.Concepto.Descripcion.Length > 0).GroupBy(p => p.Concepto.Descripcion.Trim())
                .OrderByDescending(d => d.Count()).Select(d => d.Key).FirstOrDefault() ?? "";
            if (descripcion.Length > 60) descripcion = descripcion[..60] + "…";
            var neto = g.Sum(p => p.Cfdi.TipoCfdi == "I" ? p.Concepto.Importe - (p.Concepto.Descuento ?? 0) : -p.Concepto.Importe);
            return new ClaveSat(g.Key, $"{g.Key} · {descripcion}", neto);
        }).OrderByDescending(c => c.Neto).ThenBy(c => c.Clave, StringComparer.Ordinal).ToList();

        var top = 0;
        if (claves.Count > 0)
        {
            var ultima = EscribirResumenConceptos(wb, claves, periodo, recibidos, out top);
            EscribirDashboardConceptos(wb, claves, ultima, rfc, periodo, recibidos, emitidos, uno);
        }

        wb.Worksheets.First().SetTabActive().Select();
        wb.FullCalculationOnLoad = true;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        if (claves.Count > 0)
        {
            using var doc = SpreadsheetDocument.Open(stream, true);
            var wbPart = doc.WorkbookPart!;
            var dash = (WorksheetPart)wbPart.GetPartById(wbPart.Workbook.Sheets!.Elements<S.Sheet>().Single(x => x.Name == "Dashboard").Id!);
            GraficoTop(wbPart, dash, 4 + top, HojaResumenConceptos, "Top 10 claves SAT por importe neto");
        }
        var carpeta = recibidos ? "Recibidas" : emitidos ? "Emitidas" : "CFDI";
        return (stream.ToArray(), $"{rfc} {carpeta} {periodoCorto} Conceptos.xlsx");
    }

    private static int EscribirResumenConceptos(XLWorkbook wb, List<ClaveSat> claves, string periodo, bool recibidos, out int top)
    {
        const string t = TablaConceptos;
        var ws = wb.Worksheets.Add(HojaResumenConceptos, 1);
        ws.SetTabColor(XLColor.FromHtml("#0084BE"));
        ws.ShowGridLines = false;
        double[] anchos = [14, 60, 11, 13, 18, 16, 18, 18, 10, 16];
        for (var i = 0; i < anchos.Length; i++) ws.Column(i + 1).Width = anchos[i];

        var titulo = ws.Range("A2:J2").Merge();
        ws.Cell("A2").Value = $"RESUMEN POR CLAVE SAT · CONCEPTOS {(recibidos ? "RECIBIDOS" : "EMITIDOS")} {periodo}";
        titulo.Style.Fill.SetBackgroundColor(Color("005A80")).Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.White)
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(2).Height = 26.1;

        string[] encabezados = ["CLAVE SAT", "CONCEPTO", "PARTIDAS", "CANTIDAD", "IMPORTE", "DESCUENTO", "NC APLICADAS", "IMPORTE NETO", "% PART.", "PRECIO PROM."];
        for (var i = 0; i < encabezados.Length; i++) ws.Cell(4, i + 1).Value = encabezados[i];
        var ultima = 4 + claves.Count;
        string De(int r, string tipo) => $"{t}[ClaveProdServ],$A{r},{t}[Tipo],\"{tipo}\"";
        for (var i = 0; i < claves.Count; i++)
        {
            var r = 5 + i;
            // Texto, igual que en la tabla de conceptos: así coinciden en SUMIFS y FILTER.
            ws.Cell(r, 1).Value = claves[i].Clave;
            ws.Cell(r, 1).Style.NumberFormat.Format = "@";
            ws.Cell(r, 2).Value = claves[i].Etiqueta;
            ws.Cell(r, 3).FormulaA1 = $"COUNTIFS({De(r, "Factura")})";
            ws.Cell(r, 4).FormulaA1 = $"SUMIFS({t}[Cantidad],{De(r, "Factura")})";
            ws.Cell(r, 5).FormulaA1 = $"SUMIFS({t}[Importe],{De(r, "Factura")})";
            ws.Cell(r, 6).FormulaA1 = $"SUMIFS({t}[Descuento],{De(r, "Factura")})";
            ws.Cell(r, 7).FormulaA1 = $"SUMIFS({t}[Importe],{De(r, "NotaCredito")})";
            ws.Cell(r, 8).FormulaA1 = $"E{r}-F{r}-G{r}";
            ws.Cell(r, 9).FormulaA1 = $"IFERROR(H{r}/SUM($H$5:$H${ultima}),0)";
            ws.Cell(r, 10).FormulaA1 = $"IFERROR((E{r}-F{r})/D{r},0)";
            ws.Cell(r, 27).FormulaA1 = $"$H{r}";
        }

        var tabla = ws.Range(4, 1, ultima, 10).CreateTable("TblResumenConceptos");
        tabla.Theme = XLTableTheme.TableStyleMedium2;
        tabla.ShowRowStripes = true;
        tabla.ShowTotalsRow = true;
        tabla.Field("CLAVE SAT").TotalsRowLabel = "TOTAL";
        tabla.Field("CONCEPTO").TotalsRowFormulaA1 = "SUBTOTAL(103,TblResumenConceptos[CONCEPTO])&\" claves\"";
        foreach (var campo in new[] { "PARTIDAS", "CANTIDAD", "IMPORTE", "DESCUENTO", "NC APLICADAS", "IMPORTE NETO", "% PART." })
            tabla.Field(campo).TotalsRowFunction = XLTotalsRowFunction.Sum;

        foreach (var col in new[] { 5, 6, 7, 8, 10 }) ws.Range(5, col, ultima + 1, col).Style.NumberFormat.Format = Moneda;
        ws.Range(5, 4, ultima + 1, 4).Style.NumberFormat.Format = "#,##0.##";
        ws.Range(5, 9, ultima + 1, 9).Style.NumberFormat.Format = "0.0%";
        foreach (var col in new[] { 3, 9 }) ws.Range(5, col, ultima + 1, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(ultima + 1, 1, ultima + 1, 10).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(4);

        ws.Range($"E5:E{ultima}").AddConditionalFormat().DataBar(Color("0084BE")).LowestValue().HighestValue();
        ws.Range($"G5:G{ultima}").AddConditionalFormat().DataBar(Color("C00000")).LowestValue().HighestValue();
        ws.Range($"H5:H{ultima}").AddConditionalFormat().ColorScale()
            .LowestValue(Color("F2F9FD")).Midpoint(XLCFContentType.Percentile, 50, Color("9BDCF8")).HighestValue(Color("0084BE"));

        // Datos auxiliares del gráfico del dashboard: top 10 por importe neto.
        top = Math.Max(1, Math.Min(10, claves.Count));
        ws.Cell("AA1").Value = "Datos auxiliares del gráfico del Dashboard (no borrar)";
        ws.Cell("AA1").Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));
        ws.Cell("AA4").Value = "IMPORTE NETO";
        ws.Cell("AC4").Value = "CONCEPTO";
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
        ws.Column(29).Width = 60;
        ws.Column(30).Width = 18;
        return ultima;
    }

    private static void EscribirDashboardConceptos(
        XLWorkbook wb, List<ClaveSat> claves, int ultima, string rfc, string periodo, bool recibidos, bool emitidos, string uno)
    {
        const string t = TablaConceptos;
        var ws = wb.Worksheets.Add("Dashboard", 1);
        ws.SetTabColor(XLColor.FromHtml("#0B2E4F"));
        ws.ShowGridLines = false;
        ws.Style.Font.FontName = "Segoe UI";
        string Rc(string col) => $"'{HojaResumenConceptos}'!${col}$5:${col}${ultima}";

        ws.Column(1).Width = Ancho(10);
        ws.Column(17).Width = Ancho(10);
        foreach (var c in new[] { 5, 9, 13 }) ws.Column(c).Width = Ancho(12);
        foreach (var inicio in Grupos) ws.Columns(inicio, inicio + 2).Width = Ancho(72);
        (int Fila, double Alto)[] altos = [(1, 8), (2, 32), (3, 18), (4, 5), (5, 10), (6, 14), (7, 22), (8, 12), (9, 4), (10, 8), (11, 18), (12, 15), (13, 18), (28, 8), (29, 18), (30, 15)];
        foreach (var (fila, alto) in altos) ws.Row(fila).Height = alto;
        for (var r = 14; r <= 27; r++) ws.Row(r).Height = r is >= 15 and <= 19 ? 16 : 14;
        for (var r = 31; r <= 45; r++) ws.Row(r).Height = 13;

        // Celda auxiliar con la clave SAT elegida en el dropdown.
        ws.Cell("R1").Value = "Clave SAT del concepto seleccionado (celda de apoyo del dropdown)";
        ws.Cell("R1").Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));
        ws.Cell("R2").FormulaA1 = $"IFERROR(INDEX({Rc("A")},MATCH($J$13,{Rc("B")},0)),\"\")";
        ws.Cell("R2").Style.Font.SetFontColor(Color("808080"));

        var lado = recibidos ? "RECIBIDOS" : emitidos ? "EMITIDOS" : "";
        Banda(ws, "B2:P2", $"ANÁLISIS DE CONCEPTOS · CFDI {lado} {periodo}".Replace("  ", " "), "0B2E4F", 15, bold: true);
        Banda(ws, "B3:P3", null, "0084BE", 9, bold: false);
        ws.Cell("B3").FormulaA1 = $"\"{(recibidos ? "Receptor" : "Emisor")} {rfc} · \"&COUNTIFS({t}[Tipo],\"Factura\")&\" partidas de factura · \"" +
                                  $"&COUNTIFS({t}[Tipo],\"NotaCredito\")&\" de notas de crédito · \"&COUNTA(TblResumenConceptos[CLAVE SAT])&\" claves SAT · fuente: hoja Conceptos\"";
        string[] franja = ["003A55", "C00000", "0084BE", "00B0F0"];
        for (var g = 0; g < 4; g++) ws.Range(4, Grupos[g], 4, Grupos[g] + 2).Style.Fill.BackgroundColor = Color(franja[g]);

        var descuentos = $"SUMIFS({t}[Descuento],{t}[Tipo],\"Factura\")";
        var tarjetas = new (string Titulo, string Valor, string Subtitulo, string[] Tonos, bool Dinero)[]
        {
            ("IMPORTE FACTURADO", $"SUMIFS({t}[Importe],{t}[Tipo],\"Factura\")",
                $"COUNTIFS({t}[Tipo],\"Factura\")&\" partidas · descuentos \"&TEXT({descuentos},\"$#,##0\")", ["003A55", "00587F", "0084BE", "00B0F0"], true),
            ("NOTAS DE CRÉDITO", $"SUMIFS({t}[Importe],{t}[Tipo],\"NotaCredito\")",
                $"IFERROR(TEXT(F7/B7,\"0.0%\"),\"0.0%\")&\" del importe · \"&COUNTIFS({t}[Tipo],\"NotaCredito\")&\" partidas\"", ["7F0000", "A80000", "C00000", "E06666"], true),
            ("IMPORTE NETO", $"B7-{descuentos}-F7", "\"Importe − descuentos − notas de crédito (sin IVA)\"", ["00587F", "0084BE", "00A0DC", "66CFF7"], true),
            ("CLAVES SAT", $"COUNTIF({Rc("H")},\">0\")",
                $"\"IVA trasladado \"&TEXT(SUMIFS({t}[IVA Trasladado],{t}[Tipo],\"Factura\"),\"$#,##0\")", ["0084BE", "00A0DC", "00B0F0", "8FDCF9"], false),
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

        // ---------- Top 15 claves ----------
        Banda(ws, "B11:H11", "TOP 15 CLAVES SAT POR IMPORTE NETO", "003A55", 9, bold: true);
        Banda(ws, "J11:P11", "DETALLE POR CONCEPTO", "00587F", 9, bold: true);
        Encabezado(ws, "B12:D12", "CONCEPTO (CLAVE SAT)", indent: true);
        Encabezado(ws, "F12", "IMPORTE");
        Encabezado(ws, "G12", "NC APLICADAS");
        Encabezado(ws, "H12", "IMPORTE NETO");
        Encabezado(ws, "J12:L12", "SELECCIONA CONCEPTO", indent: true);
        Encabezado(ws, "N12:P12", "CLAVE SAT", indent: true);
        for (var r = 13; r <= 27; r++)
        {
            ws.Range(r, 2, r, 4).Merge();
            ws.Cell(r, 2).FormulaA1 = $"IFERROR(INDEX({Rc("B")},ROW()-12),\"\")";
            ws.Cell(r, 2).Style.Font.SetFontSize(8.5).Alignment.SetIndent(1);
            foreach (var (c, fuente) in new[] { (6, "E"), (7, "G"), (8, "H") })
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

        // ---------- Detalle por concepto ----------
        var selector = ws.Range("J13:L13").Merge();
        ws.Cell("J13").Value = claves[0].Etiqueta;
        selector.Style.Fill.SetBackgroundColor(Color("FFF7D6")).Font.SetBold().Font.SetFontSize(8.5).Font.SetFontColor(Color("003A55"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetShrinkToFit();
        selector.CreateDataValidation().List(Rc("B"), true);
        var clave = ws.Range("N13:P13").Merge();
        ws.Cell("N13").FormulaA1 = "$R$2";
        clave.Style.Fill.SetBackgroundColor(Color("EDF6FB")).Font.SetFontSize(9).Font.SetFontColor(Color("003A55"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);

        string DeClave(string tipo) => $"{t}[ClaveProdServ],$R$2,{t}[Tipo],\"{tipo}\"";
        var mini = new (string Titulo, string Tono, bool LetraOscura, string Formula, string Formato)[]
        {
            ("IMPORTE FACTURADO", "003A55", false, $"SUMIFS({t}[Importe],{DeClave("Factura")})", Moneda),
            ("NOTAS DE CRÉDITO", "A80000", false, $"SUMIFS({t}[Importe],{DeClave("NotaCredito")})", Moneda),
            (recibidos ? "CANTIDAD COMPRADA" : "CANTIDAD VENDIDA", "0084BE", false, $"SUMIFS({t}[Cantidad],{DeClave("Factura")})", "#,##0.##"),
            ("PRECIO PROMEDIO", "00A0DC", false, $"IFERROR((N15-SUMIFS({t}[Descuento],{DeClave("Factura")}))/N17,0)", Moneda),
            ("PARTIDAS / NC", "66CFF7", true, $"COUNTIFS({DeClave("Factura")})&\" / \"&COUNTIFS({DeClave("NotaCredito")})", "@"),
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

        // Partidas de la clave: una fórmula FILTER por columna (facturas y notas de crédito).
        Banda(ws, "B29:P29", "PARTIDAS DEL CONCEPTO SELECCIONADO (primeras 15)", "0084BE", 9, bold: true);
        var condicion = $"({t}[ClaveProdServ]=$R$2)*(({t}[Tipo]=\"Factura\")+({t}[Tipo]=\"NotaCredito\"))";
        string Celda(string campo, string k) => $"INDEX(_xlfn._xlws.FILTER({t}[{campo}],{condicion}),{k})";
        (int Col, string Titulo, Func<string, string> Valor, string? Formato)[] detalle =
        [
            (2, "FECHA", k => Celda("Fecha Emision", k), "dd/mm/yyyy"),
            (3, "SERIE-FOLIO", k => $"{Celda("Serie", k)}&\"-\"&{Celda("Folio", k)}", null),
            (4, uno, k => $"{Celda("Contraparte", k)}&\"\"", null),
            (6, "DESCRIPCIÓN", k => $"{Celda("Descripcion", k)}&\"\"", null),
            (7, "CANTIDAD", k => Celda("Cantidad", k), "#,##0.##"),
            (8, "IMPORTE", k => Celda("Importe", k), Moneda),
            (10, "DESCUENTO", k => Celda("Descuento", k), Moneda),
            (11, "TIPO", k => $"{Celda("Tipo", k)}&\"\"", null),
            (12, "UNIDAD", k => $"{Celda("ClaveUnidad", k)}&\"\"", null),
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
        ws.Cell("B47").Value = $"Generado por CFDI Manager el {DateTime.Now:dd/MM/yyyy HH:mm} · Fuente: hojas Conceptos y Resumen Conceptos · " +
                               "Importes de los conceptos sin IVA; solo cuentan facturas y notas de crédito.";
        nota.Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));
    }
}
