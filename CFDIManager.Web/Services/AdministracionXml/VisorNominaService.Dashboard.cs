using System.Globalization;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Dashboard del Excel del visor de nóminas. Todo son fórmulas sobre el mismo libro: la tabla de recibos (TblRecibos),
/// la de trabajadores (TblTrabajadores) y el renglón de ISR enterado de la hoja Detalle mensual. KPIs del ejercicio,
/// tabla mes por mes con gráfico, top 10 de trabajadores y resumen por departamento.
/// </summary>
public partial class VisorNominaService
{
    private const string MonedaDash = "\"$\"#,##0.00;\\(\"$\"#,##0.00\\);\\-";
    private const int DashPrimerMes = 12;

    private static XLColor C(string hex) => XLColor.FromHtml("#" + hex);

    private static void BandaDash(IXLWorksheet ws, string rango, string? texto, string fondo, double tamano, bool negrita = true)
    {
        var r = ws.Range(rango).Merge();
        if (texto is not null) r.FirstCell().Value = texto;
        r.Style.Fill.SetBackgroundColor(C(fondo)).Font.SetFontColor(XLColor.White).Font.SetFontSize(tamano).Font.SetBold(negrita)
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }

    private static void EncabezadoDash(IXLWorksheet ws, string rango, string texto, bool centrado = true)
    {
        var r = ws.Range(rango);
        if (r.Cells().Count() > 1) r.Merge();
        r.FirstCell().Value = texto;
        r.Style.Fill.SetBackgroundColor(C("D9EEF9")).Font.SetBold().Font.SetFontSize(8.5).Font.SetFontColor(C("003A55"))
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText();
        r.Style.Alignment.Horizontal = centrado ? XLAlignmentHorizontalValues.Center : XLAlignmentHorizontalValues.Left;
    }

    /// <param name="filaEnterado">Renglón de "ISR enterado (captura)" en la hoja Detalle mensual (columnas C a N = ene a dic).</param>
    private static IXLWorksheet EscribirDashboard(XLWorkbook libro, EjercicioNomina ej, int filaEnterado)
    {
        var mx = CultureInfo.GetCultureInfo("es-MX");
        const string r = "TblRecibos", t = "TblTrabajadores";
        var ws = libro.AddWorksheet("Dashboard", 1);
        ws.SetTabColor(C("0B2E4F"));
        ws.ShowGridLines = false;
        ws.Style.Font.SetFontName("Segoe UI").Font.SetFontSize(9);

        ws.Column(1).Width = 2;
        ws.Column(2).Width = 13;
        ws.Columns(3, 13).Width = 15;
        ws.Column(14).Width = 2;
        foreach (var (fila, alto) in new[] { (2, 30.0), (3, 18.0), (4, 6.0), (5, 15.0), (6, 24.0), (7, 14.0), (8, 6.0), (10, 18.0), (11, 30.0) })
            ws.Row(fila).Height = alto;

        // ---------- Título ----------
        BandaDash(ws, "B2:M2", $"DASHBOARD DE NÓMINA · {ej.RfcRetenedor} {ej.NombreRetenedor} · EJERCICIO {ej.Anio}", "0B2E4F", 14);
        BandaDash(ws, "B3:M3", null, "0084BE", 9, negrita: false);
        ws.Cell("B3").FormulaA1 = $"ROWS({r}[UUID])&\" recibos vigentes · \"&ROWS({t}[RFC])&\" trabajadores · acumulado por fecha de pago" +
                                  (ej.Cancelados > 0 ? $" · {ej.Cancelados} cancelados no considerados" : "") + " · fuente: hojas Recibos, Trabajadores y Detalle mensual\"";

        // ---------- KPIs ----------
        string Suma(string campo) => $"SUM({r}[{campo}])";
        var tarjetas = new (string Titulo, string Valor, string Subtitulo, string Fondo, string Oscuro, bool Dinero)[]
        {
            ("PERCEPCIONES", Suma("PERCEPCIONES"), $"\"Gravado \"&TEXT({Suma("SUELDOS GRAVADOS")},\"$#,##0\")", "0084BE", "005A80", true),
            ("NETO PAGADO", Suma("NETO"), $"IFERROR(TEXT(D6/B6,\"0.0%\"),\"\")&\" de las percepciones\"", "00A0DC", "00587F", true),
            ("ISR RETENIDO", Suma("ISR"), $"\"Tasa efectiva \"&IFERROR(TEXT(F6/B6,\"0.0%\"),\"0.0%\")", "1E3A8A", "172554", true),
            ("SUBSIDIO NETO", Suma("SUBSIDIO NETO"), $"IFERROR(TEXT(H6/F6,\"0.0%\"),\"0.0%\")&\" del ISR retenido\"", "CA8A04", "854D0E", true),
            ("TRABAJADORES", $"ROWS({t}[RFC])", $"\"Promedio por recibo \"&IFERROR(TEXT(B6/ROWS({r}[UUID]),\"$#,##0\"),\"\")", "16A34A", "14532D", false),
            ("ISR POR ENTERAR", $"F6-SUM('Detalle mensual'!C{filaEnterado}:N{filaEnterado})",
                $"\"Enterado \"&TEXT(SUM('Detalle mensual'!C{filaEnterado}:N{filaEnterado}),\"$#,##0\")", "7C3AED", "3B0F63", true),
        };
        for (var i = 0; i < tarjetas.Length; i++)
        {
            var k = tarjetas[i];
            var col = 2 + i * 2;
            ws.Range(4, col, 4, col + 1).Style.Fill.BackgroundColor = C(k.Oscuro);
            for (var fila = 5; fila <= 8; fila++)
            {
                ws.Range(fila, col, fila, col + 1).Merge().Style.Fill.SetBackgroundColor(C(fila == 8 ? k.Oscuro : k.Fondo)).Font.SetFontColor(XLColor.White)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            }
            ws.Cell(5, col).Value = k.Titulo;
            ws.Cell(5, col).Style.Font.SetBold().Font.SetFontSize(8);
            ws.Cell(6, col).FormulaA1 = k.Valor;
            ws.Cell(6, col).Style.Font.SetBold().Font.SetFontSize(14).NumberFormat.Format = k.Dinero ? MonedaDash : "#,##0";
            ws.Cell(7, col).FormulaA1 = k.Subtitulo;
            ws.Cell(7, col).Style.Font.SetFontSize(7.5);
        }

        // ---------- Mes por mes ----------
        BandaDash(ws, "B10:M10", "MES POR MES (FECHA DE PAGO)", "0B2E4F", 9);
        string[] encabezados = ["MES", "RECIBOS", "TRABAJADORES", "PERCEPCIONES", "GRAVADO", "EXENTO", "PTU", "ISR RETENIDO", "SUBSIDIO NETO", "NETO PAGADO", "ISR ENTERADO", "ISR POR ENTERAR"];
        for (var i = 0; i < encabezados.Length; i++) EncabezadoDash(ws, ws.Cell(11, 2 + i).Address.ToString()!, encabezados[i]);
        var ultimoMes = DashPrimerMes + 11;
        var totales = ultimoMes + 1;
        for (var m = 1; m <= 12; m++)
        {
            var fila = DashPrimerMes + m - 1;
            string Del(string campo) => $"SUMIFS({r}[{campo}],{r}[MES],{m})";
            var enterado = $"'Detalle mensual'!{XLHelper.GetColumnLetterFromNumber(m + 2)}{filaEnterado}";
            ws.Cell(fila, 2).Value = mx.DateTimeFormat.GetMonthName(m).ToUpperInvariant();
            ws.Cell(fila, 3).FormulaA1 = $"COUNTIFS({r}[MES],{m})";
            ws.Cell(fila, 4).FormulaA1 = $"IFERROR(ROWS(_xlfn.UNIQUE(_xlfn._xlws.FILTER({r}[RFC],{r}[MES]={m}))),0)";
            ws.Cell(fila, 5).FormulaA1 = Del("PERCEPCIONES");
            ws.Cell(fila, 6).FormulaA1 = Del("SUELDOS GRAVADOS");
            ws.Cell(fila, 7).FormulaA1 = Del("SUELDOS EXENTOS");
            ws.Cell(fila, 8).FormulaA1 = Del("PTU");
            ws.Cell(fila, 9).FormulaA1 = Del("ISR");
            ws.Cell(fila, 10).FormulaA1 = Del("SUBSIDIO NETO");
            ws.Cell(fila, 11).FormulaA1 = Del("NETO");
            ws.Cell(fila, 12).FormulaA1 = $"IF({enterado}=\"\",\"\",{enterado})";
            // Sin captura del enterado no hay diferencia que mostrar.
            ws.Cell(fila, 13).FormulaA1 = $"IF(L{fila}=\"\",\"\",I{fila}-L{fila})";
        }
        ws.Cell(totales, 2).Value = "TOTAL";
        foreach (var c in new[] { "C", "E", "F", "G", "H", "I", "J", "K", "L", "M" })
            ws.Cell($"{c}{totales}").FormulaA1 = $"SUM({c}{DashPrimerMes}:{c}{ultimoMes})";
        ws.Cell($"D{totales}").FormulaA1 = $"ROWS({t}[RFC])";
        ws.Range(totales, 2, totales, 13).Style.Font.SetBold().Fill.SetBackgroundColor(C("EDF6FB"))
            .Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetTopBorderColor(C("0B2E4F"));
        ws.Range(DashPrimerMes, 5, totales, 13).Style.NumberFormat.Format = MonedaDash;
        ws.Range(DashPrimerMes, 3, totales, 4).Style.NumberFormat.Format = "#,##0";
        ws.Range(DashPrimerMes, 3, totales, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range($"B{DashPrimerMes}:M{ultimoMes}").AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(C("F5F9FC"));
        // Meses sin recibos en gris.
        ws.Range($"B{DashPrimerMes}:M{ultimoMes}").AddConditionalFormat().WhenIsTrue($"$C{DashPrimerMes}=0").Font.SetFontColor(C("A6A6A6"));
        ws.Range($"E{DashPrimerMes}:E{ultimoMes}").AddConditionalFormat().DataBar(C("00A0DC")).LowestValue().HighestValue();
        ws.Range($"M{DashPrimerMes}:M{ultimoMes}").AddConditionalFormat().WhenGreaterThan(1).Font.SetFontColor(C("B45309")).Font.SetBold();

        // ---------- Gráfico (lo agrega AgregarGraficoDashboard) ----------
        var bandaGrafico = totales + 2;
        BandaDash(ws, $"B{bandaGrafico}:M{bandaGrafico}", "PERCEPCIONES, ISR RETENIDO Y NETO PAGADO POR MES", "0B2E4F", 9);

        // ---------- Top 10 trabajadores y departamentos ----------
        var bandaListas = bandaGrafico + 18;
        BandaDash(ws, $"B{bandaListas}:H{bandaListas}", "TOP 10 TRABAJADORES POR PERCEPCIONES", "003A55", 9);
        BandaDash(ws, $"J{bandaListas}:M{bandaListas}", "POR DEPARTAMENTO", "00587F", 9);
        var encListas = bandaListas + 1;
        ws.Row(encListas).Height = 24;
        EncabezadoDash(ws, $"B{encListas}:D{encListas}", "TRABAJADOR", centrado: false);
        EncabezadoDash(ws, $"E{encListas}", "RECIBOS");
        EncabezadoDash(ws, $"F{encListas}", "PERCEPCIONES");
        EncabezadoDash(ws, $"G{encListas}", "ISR RETENIDO");
        EncabezadoDash(ws, $"H{encListas}", "NETO PAGADO");
        EncabezadoDash(ws, $"J{encListas}", "DEPARTAMENTO", centrado: false);
        EncabezadoDash(ws, $"K{encListas}", "TRABAJADORES");
        EncabezadoDash(ws, $"L{encListas}", "PERCEPCIONES");
        EncabezadoDash(ws, $"M{encListas}", "% DEL TOTAL");

        string Orden(string campo, int k) => $"IFERROR(INDEX(_xlfn.SORTBY({t}[{campo}],{t}[PERCEPCIONES],-1),{k}),\"\")";
        var primeraLista = encListas + 1;
        for (var k = 1; k <= 10; k++)
        {
            var fila = primeraLista + k - 1;
            ws.Range(fila, 2, fila, 4).Merge();
            ws.Cell(fila, 2).FormulaA1 = Orden("NOMBRE", k);
            ws.Cell(fila, 5).FormulaA1 = Orden("RECIBOS", k);
            ws.Cell(fila, 6).FormulaA1 = Orden("PERCEPCIONES", k);
            ws.Cell(fila, 7).FormulaA1 = Orden("ISR RETENIDO", k);
            ws.Cell(fila, 8).FormulaA1 = Orden("NETO PAGADO", k);
        }
        var ultimaLista = primeraLista + 9;
        ws.Range(primeraLista, 6, ultimaLista, 8).Style.NumberFormat.Format = MonedaDash;
        ws.Range(primeraLista, 5, ultimaLista, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range($"B{primeraLista}:H{ultimaLista}").AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(C("F5F9FC"));
        ws.Range($"F{primeraLista}:F{ultimaLista}").AddConditionalFormat().DataBar(C("00A0DC")).LowestValue().HighestValue();

        // Departamentos del ejercicio (los 10 con más percepciones; el resto se suma en "Otros").
        var departamentos = ej.Recibos.GroupBy(x => x.Departamento.Trim())
            .OrderByDescending(g => g.Sum(x => x.TotalPercepciones)).Select(g => g.Key).ToList();
        var mostrados = departamentos.Count > 10 ? departamentos.Take(9).ToList() : departamentos;
        for (var i = 0; i < mostrados.Count; i++)
        {
            var fila = primeraLista + i;
            var depto = mostrados[i];
            ws.Cell(fila, 10).Value = depto == "" ? "(sin departamento)" : depto;
            var criterio = depto == "" ? "\"\"" : $"$J{fila}";
            ws.Cell(fila, 11).FormulaA1 = $"IFERROR(ROWS(_xlfn.UNIQUE(_xlfn._xlws.FILTER({r}[RFC],{r}[DEPARTAMENTO]={criterio}))),0)";
            ws.Cell(fila, 12).FormulaA1 = $"SUMIFS({r}[PERCEPCIONES],{r}[DEPARTAMENTO],{criterio})";
            ws.Cell(fila, 13).FormulaA1 = $"IFERROR(L{fila}/SUM({r}[PERCEPCIONES]),0)";
        }
        if (departamentos.Count > 10)
        {
            var fila = primeraLista + 9;
            var primeras = primeraLista + 8;
            ws.Cell(fila, 10).Value = $"Otros ({departamentos.Count - 9})";
            ws.Cell(fila, 11).FormulaA1 =
                $"IFERROR(ROWS(_xlfn.UNIQUE(_xlfn._xlws.FILTER({r}[RFC],ISNA(MATCH({r}[DEPARTAMENTO],J{primeraLista}:J{primeras},0))))),0)";
            ws.Cell(fila, 12).FormulaA1 = $"SUM({r}[PERCEPCIONES])-SUM(L{primeraLista}:L{primeras})";
            ws.Cell(fila, 13).FormulaA1 = $"IFERROR(L{fila}/SUM({r}[PERCEPCIONES]),0)";
        }
        ws.Range(primeraLista, 10, ultimaLista, 10).Style.Alignment.SetShrinkToFit();
        ws.Range(primeraLista, 11, ultimaLista, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(primeraLista, 12, ultimaLista, 12).Style.NumberFormat.Format = MonedaDash;
        ws.Range(primeraLista, 13, ultimaLista, 13).Style.NumberFormat.Format = "0.0%";
        ws.Range($"J{primeraLista}:M{ultimaLista}").AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(C("F5F9FC"));
        ws.Range($"M{primeraLista}:M{ultimaLista}").AddConditionalFormat().DataBar(C("66CFF7")).LowestValue().HighestValue();

        var nota = ws.Range($"B{ultimaLista + 2}:M{ultimaLista + 2}").Merge();
        ws.Cell($"B{ultimaLista + 2}").Value = $"Generado por CFDI Manager el {DateTime.Now:dd/MM/yyyy HH:mm} · Recibos vigentes por fecha de pago · " +
            "Subsidio neto = causado menos ajustes · ISR por enterar = retenido menos el ISR enterado capturado en el visor.";
        nota.Style.Font.SetFontSize(8).Font.SetFontColor(C("808080"));

        ws.SheetView.FreezeRows(3);
        return ws;
    }

    /// <summary>Gráfico de columnas (percepciones, ISR retenido y neto pagado por mes) debajo de la tabla mes por mes.</summary>
    private static void AgregarGraficoDashboard(MemoryStream stream)
    {
        using var doc = SpreadsheetDocument.Open(stream, true);
        var wbPart = doc.WorkbookPart!;
        var dash = (WorksheetPart)wbPart.GetPartById(wbPart.Workbook.Sheets!.Elements<S.Sheet>().Single(x => x.Name == "Dashboard").Id!);

        const int primera = DashPrimerMes, ultima = DashPrimerMes + 11;
        var bandaGrafico = ultima + 3;
        (string Columna, string Color)[] series = [("E", "0084BE"), ("I", "1E3A8A"), ("K", "00B0F0")];
        var xmlSeries = string.Concat(series.Select((s, i) => $"""
            <c:ser><c:idx val="{i}"/><c:order val="{i}"/><c:tx><c:strRef><c:f>'Dashboard'!${s.Columna}$11</c:f></c:strRef></c:tx>
            <c:spPr><a:solidFill><a:srgbClr val="{s.Color}"/></a:solidFill></c:spPr><c:invertIfNegative val="0"/>
            <c:cat><c:strRef><c:f>'Dashboard'!$B${primera}:$B${ultima}</c:f></c:strRef></c:cat>
            <c:val><c:numRef><c:f>'Dashboard'!${s.Columna}${primera}:${s.Columna}${ultima}</c:f></c:numRef></c:val></c:ser>
            """));
        const string texto = """<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="800"><a:solidFill><a:srgbClr val="595959"/></a:solidFill></a:defRPr></a:pPr><a:endParaRPr lang="es-MX"/></a:p></c:txPr>""";
        const string linea = """<c:spPr><a:ln w="9525"><a:solidFill><a:srgbClr val="D9D9D9"/></a:solidFill></a:ln></c:spPr>""";

        var dibujo = dash.AddNewPart<DrawingsPart>();
        var grafico = dibujo.AddNewPart<ChartPart>();
        ReporteGeneral.Escribir(grafico, $"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
            <c:roundedCorners val="0"/>
            <c:chart><c:autoTitleDeleted val="1"/>
            <c:plotArea><c:layout/>
            <c:barChart><c:barDir val="col"/><c:grouping val="clustered"/><c:varyColors val="0"/>{xmlSeries}
            <c:gapWidth val="70"/><c:axId val="61001"/><c:axId val="61002"/></c:barChart>
            <c:catAx><c:axId val="61001"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="b"/><c:numFmt formatCode="General" sourceLinked="1"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="low"/>{linea}{texto}<c:crossAx val="61002"/><c:crosses val="autoZero"/><c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/><c:tickLblSkip val="1"/><c:noMultiLvlLbl val="0"/></c:catAx>
            <c:valAx><c:axId val="61002"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="l"/><c:majorGridlines>{linea}</c:majorGridlines><c:numFmt formatCode="&quot;$&quot;#,##0" sourceLinked="0"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/><c:spPr><a:ln><a:noFill/></a:ln></c:spPr>{texto}<c:crossAx val="61001"/><c:crosses val="autoZero"/><c:crossBetween val="between"/></c:valAx>
            <c:spPr><a:noFill/></c:spPr>
            </c:plotArea>
            <c:legend><c:legendPos val="t"/><c:overlay val="0"/>{texto}</c:legend>
            <c:plotVisOnly val="1"/><c:dispBlanksAs val="gap"/>
            </c:chart>
            <c:spPr><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill><a:ln w="9525"><a:solidFill><a:srgbClr val="D9D9D9"/></a:solidFill></a:ln></c:spPr>
            </c:chartSpace>
            """);
        // Debajo de la banda del gráfico, de B a M (columnas 1 a 13, base 0) y 16 filas de alto.
        ReporteGeneral.Escribir(dibujo, $"""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
            <xdr:twoCellAnchor editAs="oneCell"><xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{bandaGrafico}</xdr:row><xdr:rowOff>38100</xdr:rowOff></xdr:from><xdr:to><xdr:col>13</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{bandaGrafico + 16}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
            <xdr:graphicFrame macro=""><xdr:nvGraphicFramePr><xdr:cNvPr id="2" name="g_nomina_mensual"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm>
            <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart"><c:chart xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="{dibujo.GetIdOfPart(grafico)}"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>
            </xdr:wsDr>
            """);
        ReporteGeneral.InsertarDibujo(dash, dash.GetIdOfPart(dibujo));
    }
}
