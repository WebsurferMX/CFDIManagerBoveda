using ClosedXML.Excel;
using CFDIParser.Models;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Reporte "Agrupado por mes" (varios meses): la hoja Por Mes como tabla agrupada por mes y un dashboard de tendencia
/// mensual que lee solo de esa tabla: KPIs del periodo, tabla mes por mes y gráfico de columnas. Emitidos: facturado,
/// notas de crédito y venta neta. Recibidos: facturado, notas de crédito, pagado y saldo.
/// </summary>
public static partial class ReporteGeneral
{
    private const string TablaPorMes = "TblPorMes";

    public static (byte[] Bytes, string Nombre) CrearPorMes(IReadOnlyList<AdminXmlFilaDto> filas, Filtro filtro, bool recibidos)
    {
        List<AdminXmlFilaDto> usados = filas.Where(x => x.TipoCfdi is "I" or "E" || (recibidos && x.TipoCfdi == "P")).ToList();
        // "2026-10 · OCTUBRE": ordena por fecha y, como criterio de SUMIFS, Excel no lo confunde con una fecha
        // (con "2026-10" buscaría el número de fecha y no encontraría el texto de la tabla).
        static string Clave(AdminXmlFilaDto x) => $"{x.FechaEmision.ToString("yyyy-MM", Inv)} · {Meses[x.FechaEmision.Month - 1]}";
        var meses = usados.Select(Clave).Distinct().Order(StringComparer.Ordinal).ToList();
        string Etiqueta(string clave) => $"{Meses[int.Parse(clave[5..7]) - 1]} {clave[..4]}";

        var rfc = filtro.Rfc?.Trim().ToUpperInvariant() ?? "Todos";
        var (periodo, periodoCorto) = Periodo(filtro with { Mes = null }, usados.Select(x => new DateTimeOffset(x.FechaEmision)));
        if (meses.Count > 1) (periodo, periodoCorto) = ($"{Etiqueta(meses[0])} – {Etiqueta(meses[^1])}", $"{meses[0][..7]} a {meses[^1][..7]}");

        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Segoe UI";
        EscribirTablaAgrupada(wb, "Por Mes", TablaPorMes, "Mes", usados, emisor: recibidos,
            meses.Select(m => (m, Etiqueta(m))).ToList(), Clave);

        var conGrafico = meses.Count > 0;
        if (conGrafico) EscribirTendencia(wb, meses, Etiqueta, rfc, periodo, recibidos);

        wb.Worksheets.First().SetTabActive().Select();
        wb.FullCalculationOnLoad = true;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        if (conGrafico) AgregarGraficoMensual(stream, meses.Count, recibidos);
        return (stream.ToArray(), $"{rfc} {(recibidos ? "Recibidas" : "Emitidas")} {periodoCorto} Por Mes.xlsx");
    }

    private static void EscribirTendencia(XLWorkbook wb, List<string> meses, Func<string, string> etiqueta, string rfc, string periodo, bool recibidos)
    {
        var ws = wb.Worksheets.Add("Dashboard", 1);
        ws.SetTabColor(XLColor.FromHtml(recibidos ? "#1E3A8A" : "#0B2E4F"));
        ws.ShowGridLines = false;
        ws.Style.Font.SetFontName("Segoe UI").Font.SetFontSize(9);

        ws.Column(1).Width = Ancho(10);
        ws.Column(17).Width = Ancho(10);
        foreach (var inicio in Grupos) ws.Columns(inicio, inicio + 2).Width = Ancho(72);
        foreach (var c in new[] { 5, 9, 13 }) ws.Column(c).Width = Ancho(12);
        (int Fila, double Alto)[] altos = [(1, 8), (2, 32), (3, 18), (4, 5), (5, 10), (6, 14), (7, 22), (8, 12), (9, 4), (10, 8), (11, 18), (12, 28)];
        foreach (var (renglon, alto) in altos) ws.Row(renglon).Height = alto;

        const string t = TablaPorMes;
        const int primera = 13;
        var ultima = primera + meses.Count - 1;
        var totales = ultima + 1;
        string Col(string c) => $"{c}{primera}:{c}{ultima}";
        string DelMes(string tipo, int r) => $"SUMIFS({t}[Total],{t}[Mes],$R{r},{t}[Tipo],\"{tipo}\")";
        string CuentaMes(string tipo, int r) => $"COUNTIFS({t}[Mes],$R{r},{t}[Tipo],\"{tipo}\")";
        string Suma(string tipo) => $"SUMIFS({t}[Total],{t}[Tipo],\"{tipo}\")";
        string Cuenta(string tipo) => $"COUNTIFS({t}[Tipo],\"{tipo}\")";

        // ---------- Título y KPIs ----------
        var fondo = recibidos ? "1E3A8A" : "0B2E4F";
        Banda(ws, "B2:P2", $"TENDENCIA MENSUAL · CFDI {(recibidos ? "RECIBIDOS" : "EMITIDOS")} {periodo}", fondo, 15, bold: true);
        Banda(ws, "B3:P3", null, recibidos ? "2563EB" : "0084BE", 9, bold: false);
        ws.Cell("B3").FormulaA1 = $"\"{(recibidos ? "Receptor" : "Emisor")} {rfc} · {meses.Count} meses · \"&{Cuenta("Factura")}&\" facturas · \"&{Cuenta("NotaCredito")}&\" notas de crédito" +
                                  (recibidos ? $" · \"&{Cuenta("Pago")}&\" pagos" : "") + " · fuente: hoja Por Mes\"";

        var mejor = $"INDEX({Col("B")},MATCH(MAX({Col("H")}),{Col("H")},0))";
        var tarjetas = recibidos
            ? new (string Titulo, string Valor, string Subtitulo, string[] Tonos)[]
            {
                ("FACTURADO", Suma("Factura"), $"{Cuenta("Factura")}&\" facturas · promedio \"&TEXT(B7/{meses.Count},\"$#,##0\")&\" al mes\"", ["0F2A6B", "17399A", "2563EB", "3B82F6"]),
                ("NOTAS DE CRÉDITO", $"-{Suma("NotaCredito")}", $"IFERROR(TEXT(F7/B7,\"0.0%\"),\"0.0%\")&\" del facturado\"", ["5B1E0A", "8A2D0C", "C2410C", "F97316"]),
                ("PAGADO", $"-{Suma("Pago")}", $"IFERROR(TEXT(J7/(B7-F7),\"0.0%\"),\"0.0%\")&\" del neto · \"&{Cuenta("Pago")}&\" pagos\"", ["3B0F63", "5B1BA0", "7C3AED", "A855F7"]),
                ("SALDO DEL PERIODO", "B7-F7-J7", $"\"Mayor pago: \"&{mejor}", ["854D0E", "A16207", "CA8A04", "EAB308"]),
            }
            : new (string Titulo, string Valor, string Subtitulo, string[] Tonos)[]
            {
                ("TOTAL FACTURADO", Suma("Factura"), $"{Cuenta("Factura")}&\" facturas\"", ["003A55", "00587F", "0084BE", "00B0F0"]),
                ("NOTAS DE CRÉDITO", $"-{Suma("NotaCredito")}", $"IFERROR(TEXT(F7/B7,\"0.0%\"),\"0.0%\")&\" del facturado · \"&{Cuenta("NotaCredito")}&\" NC\"", ["7F0000", "A80000", "C00000", "E06666"]),
                ("VENTA NETA", "B7-F7", $"\"Mejor mes: \"&{mejor}", ["00587F", "0084BE", "00A0DC", "66CFF7"]),
                ("PROMEDIO MENSUAL", $"J7/{meses.Count}", $"\"Venta neta entre {meses.Count} meses\"", ["0084BE", "00A0DC", "00B0F0", "8FDCF9"]),
            };
        for (var g = 0; g < 4; g++)
        {
            var k = tarjetas[g];
            var col = Grupos[g];
            ws.Range(4, col, 4, col + 2).Style.Fill.BackgroundColor = Color(k.Tonos[0]);
            for (var r = 6; r <= 9; r++)
            {
                ws.Range(r, col, r, col + 2).Merge().Style.Fill.SetBackgroundColor(Color(k.Tonos[r - 6])).Font.SetFontColor(XLColor.White)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            }
            ws.Cell(6, col).Value = k.Titulo;
            ws.Cell(6, col).Style.Font.SetFontSize(8).Font.SetBold();
            ws.Cell(7, col).FormulaA1 = k.Valor;
            ws.Cell(7, col).Style.Font.SetFontSize(13).Font.SetBold().NumberFormat.Format = Moneda;
            ws.Cell(8, col).FormulaA1 = k.Subtitulo;
            ws.Cell(8, col).Style.Font.SetFontSize(7.5);
        }
        // El blanco no se lee sobre amarillo.
        if (recibidos) ws.Cell(8, Grupos[3]).Style.Font.FontColor = Color("422006");

        // ---------- Tabla mes por mes ----------
        Banda(ws, "B11:L11", "MES POR MES", fondo, 9, bold: true);
        string[] encabezados = recibidos
            ? ["MES", "FACTURAS", "FACTURADO", "NOTAS NC", "NOTAS DE CRÉDITO", "PAGADO", "SALDO DEL MES", "% PAGADO", "SALDO ACUMULADO"]
            : ["MES", "FACTURAS", "FACTURADO", "NOTAS NC", "NOTAS DE CRÉDITO", "VENTA NETA", "% DEL PERIODO", "VS. MES ANTERIOR", "ACUMULADO"];
        int[] columnas = [2, 3, 4, 6, 7, 8, 10, 11, 12];
        for (var i = 0; i < encabezados.Length; i++)
            EncabezadoCxp(ws, ws.Cell(12, columnas[i]).Address.ToString()!, encabezados[i]);

        // R: clave del mes (columna Mes de la tabla Por Mes) con la que buscan las fórmulas.
        ws.Cell("R12").Value = "Clave";
        ws.Range($"R12:R{ultima}").Style.Font.SetFontColor(Color("A6A6A6")).Font.SetFontSize(8);
        for (var i = 0; i < meses.Count; i++)
        {
            var r = primera + i;
            ws.Cell(r, 18).Value = meses[i];
            ws.Cell(r, 2).Value = etiqueta(meses[i]);
            ws.Cell(r, 3).FormulaA1 = CuentaMes("Factura", r);
            ws.Cell(r, 4).FormulaA1 = DelMes("Factura", r);
            ws.Cell(r, 6).FormulaA1 = CuentaMes("NotaCredito", r);
            ws.Cell(r, 7).FormulaA1 = $"-{DelMes("NotaCredito", r)}";
            if (recibidos)
            {
                ws.Cell(r, 8).FormulaA1 = $"-{DelMes("Pago", r)}";
                ws.Cell(r, 10).FormulaA1 = $"D{r}-G{r}-H{r}";
                ws.Cell(r, 11).FormulaA1 = $"IFERROR(H{r}/(D{r}-G{r}),0)";
                ws.Cell(r, 12).FormulaA1 = $"SUM($J${primera}:J{r})";
            }
            else
            {
                ws.Cell(r, 8).FormulaA1 = $"D{r}-G{r}";
                ws.Cell(r, 10).FormulaA1 = $"IFERROR(H{r}/$H${totales},0)";
                ws.Cell(r, 11).FormulaA1 = i == 0 ? "\"\"" : $"IFERROR(H{r}/H{r - 1}-1,\"\")";
                ws.Cell(r, 12).FormulaA1 = $"SUM($H${primera}:H{r})";
            }
        }

        ws.Cell(totales, 2).Value = "TOTAL";
        foreach (var c in new[] { "C", "D", "F", "G", "H" }) ws.Cell($"{c}{totales}").FormulaA1 = $"SUM({Col(c)})";
        if (recibidos)
        {
            ws.Cell($"J{totales}").FormulaA1 = $"SUM({Col("J")})";
            ws.Cell($"K{totales}").FormulaA1 = $"IFERROR(H{totales}/(D{totales}-G{totales}),0)";
        }
        else
        {
            ws.Cell($"J{totales}").FormulaA1 = $"SUM({Col("J")})";
        }
        ws.Range(totales, 2, totales, 12).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetTopBorderColor(Color(fondo));
        foreach (var c in new[] { 5, 9 }) ws.Cell(totales, c).Style.Border.TopBorder = XLBorderStyleValues.None;

        foreach (var c in new[] { "D", "G", "H" }) ws.Range($"{c}{primera}:{c}{totales}").Style.NumberFormat.Format = Moneda;
        ws.Range($"L{primera}:L{totales}").Style.NumberFormat.Format = Moneda;
        if (recibidos)
        {
            ws.Range($"J{primera}:J{totales}").Style.NumberFormat.Format = Moneda;
            ws.Range($"K{primera}:K{totales}").Style.NumberFormat.Format = "0.0%";
        }
        else
        {
            ws.Range($"J{primera}:K{totales}").Style.NumberFormat.Format = "0.0%";
        }
        foreach (var c in new[] { "C", "F", "J", "K" }) ws.Range($"{c}{primera}:{c}{totales}").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        if (recibidos) ws.Range($"J{primera}:J{totales}").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.SheetView.FreezeRows(12);

        foreach (var rango in new[] { $"B{primera}:D{ultima}", $"F{primera}:H{ultima}", $"J{primera}:L{ultima}" })
            ws.Range(rango).AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(Color("F1F5F9"));
        ws.Range(Col("D")).AddConditionalFormat().DataBar(Color(recibidos ? "60A5FA" : "00A0DC")).LowestValue().HighestValue();
        ws.Range(Col("H")).AddConditionalFormat().DataBar(Color(recibidos ? "A855F7" : "66CFF7")).LowestValue().HighestValue();
        if (recibidos)
            ws.Range(Col("J")).AddConditionalFormat().ColorScale()
                .LowestValue(Color("DCFCE7")).Midpoint(XLCFContentType.Percentile, 50, Color("FEF3C7")).HighestValue(Color("FECACA"));
        else
            ws.Range(Col("K")).AddConditionalFormat().ColorScale()
                .Minimum(XLCFContentType.Number, -0.2, Color("FECACA")).Midpoint(XLCFContentType.Number, 0, Color("FFFFFF")).Maximum(XLCFContentType.Number, 0.2, Color("BBF7D0"));

        // ---------- Gráfico (lo agrega AgregarGraficoMensual) ----------
        var bandaGrafico = totales + 2;
        Banda(ws, $"B{bandaGrafico}:P{bandaGrafico}",
            recibidos ? "FACTURADO, PAGADO Y SALDO POR MES" : "FACTURADO, NOTAS DE CRÉDITO Y VENTA NETA POR MES", fondo, 9, bold: true);

        var nota = ws.Range($"B{bandaGrafico + 18}:P{bandaGrafico + 18}").Merge();
        ws.Cell($"B{bandaGrafico + 18}").Value = $"Generado por CFDI Manager el {DateTime.Now:dd/MM/yyyy HH:mm} · Fuente: hoja Por Mes (notas de crédito" +
                                                 (recibidos ? " y pagos" : "") + " en negativo; el dashboard las muestra en positivo).";
        nota.Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));
    }

    private static void AgregarGraficoMensual(MemoryStream stream, int meses, bool recibidos)
    {
        using var doc = SpreadsheetDocument.Open(stream, true);
        var wbPart = doc.WorkbookPart!;
        var dash = (WorksheetPart)wbPart.GetPartById(wbPart.Workbook.Sheets!.Elements<S.Sheet>().Single(x => x.Name == "Dashboard").Id!);

        const int primera = 13;
        var ultima = primera + meses - 1;
        var bandaGrafico = ultima + 3;
        (string Columna, string Color)[] series = recibidos
            ? [("D", "2563EB"), ("H", "7C3AED"), ("J", "CA8A04")]
            : [("D", "0084BE"), ("G", "C00000"), ("H", "00B0F0")];

        var xmlSeries = string.Concat(series.Select((s, i) => $"""
            <c:ser><c:idx val="{i}"/><c:order val="{i}"/><c:tx><c:strRef><c:f>'Dashboard'!${s.Columna}$12</c:f></c:strRef></c:tx>
            <c:spPr><a:solidFill><a:srgbClr val="{s.Color}"/></a:solidFill></c:spPr><c:invertIfNegative val="0"/>
            <c:cat><c:strRef><c:f>'Dashboard'!$B${primera}:$B${ultima}</c:f></c:strRef></c:cat>
            <c:val><c:numRef><c:f>'Dashboard'!${s.Columna}${primera}:${s.Columna}${ultima}</c:f></c:numRef></c:val></c:ser>
            """));
        const string texto = """<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="800"><a:solidFill><a:srgbClr val="595959"/></a:solidFill></a:defRPr></a:pPr><a:endParaRPr lang="es-MX"/></a:p></c:txPr>""";
        const string linea = """<c:spPr><a:ln w="9525"><a:solidFill><a:srgbClr val="D9D9D9"/></a:solidFill></a:ln></c:spPr>""";

        var dibujo = dash.AddNewPart<DrawingsPart>();
        var grafico = dibujo.AddNewPart<ChartPart>();
        Escribir(grafico, $"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
            <c:roundedCorners val="0"/>
            <c:chart><c:autoTitleDeleted val="1"/>
            <c:plotArea><c:layout/>
            <c:barChart><c:barDir val="col"/><c:grouping val="clustered"/><c:varyColors val="0"/>{xmlSeries}
            <c:gapWidth val="80"/><c:axId val="51001"/><c:axId val="51002"/></c:barChart>
            <c:catAx><c:axId val="51001"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="b"/><c:numFmt formatCode="General" sourceLinked="1"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="low"/>{linea}{texto}<c:crossAx val="51002"/><c:crosses val="autoZero"/><c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/><c:tickLblSkip val="1"/><c:noMultiLvlLbl val="0"/></c:catAx>
            <c:valAx><c:axId val="51002"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="l"/><c:majorGridlines>{linea}</c:majorGridlines><c:numFmt formatCode="&quot;$&quot;#,##0" sourceLinked="0"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/><c:spPr><a:ln><a:noFill/></a:ln></c:spPr>{texto}<c:crossAx val="51001"/><c:crosses val="autoZero"/><c:crossBetween val="between"/></c:valAx>
            <c:spPr><a:noFill/></c:spPr>
            </c:plotArea>
            <c:legend><c:legendPos val="t"/><c:overlay val="0"/>{texto}</c:legend>
            <c:plotVisOnly val="1"/><c:dispBlanksAs val="gap"/>
            </c:chart>
            <c:spPr><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill><a:ln w="9525"><a:solidFill><a:srgbClr val="D9D9D9"/></a:solidFill></a:ln></c:spPr>
            </c:chartSpace>
            """);
        // Debajo de la banda del gráfico, de B a P (columnas 1 a 16, base 0) y 16 filas de alto.
        Escribir(dibujo, $"""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
            <xdr:twoCellAnchor editAs="oneCell"><xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{bandaGrafico}</xdr:row><xdr:rowOff>38100</xdr:rowOff></xdr:from><xdr:to><xdr:col>16</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{bandaGrafico + 16}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
            <xdr:graphicFrame macro=""><xdr:nvGraphicFramePr><xdr:cNvPr id="2" name="g_mensual"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm>
            <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart"><c:chart xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="{dibujo.GetIdOfPart(grafico)}"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>
            </xdr:wsDr>
            """);
        InsertarDibujo(dash, dash.GetIdOfPart(dibujo));
    }
}
