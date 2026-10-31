using System.Globalization;
using ClosedXML.Excel;
using CFDIParser.Models;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Análisis 80-20 (Pareto) de clientes (CFDI emitidos) o proveedores (CFDI recibidos): importe neto por
/// contraparte (facturas menos notas de crédito vigentes, en MXN), ordenado de mayor a menor y clasificado
/// A (hasta el 80 % acumulado), B (hasta el 95 %) y C (el resto). Hojas: Dashboard 80-20, Pareto y Detalle CFDI.
/// </summary>
public static class Reporte8020
{
    private const string Moneda = "\"$\"#,##0.00;\\(\"$\"#,##0.00\\);\\-";
    private const string PublicoGeneral = "XAXX010101000";
    private const string HojaPareto = "Pareto";
    private const int Primera = 5;                // primera fila de datos de la hoja Pareto
    private const int MaximoGrafico = 25;         // contrapartes en la curva de Pareto

    private static readonly CultureInfo Mx = new("es-MX");

    private sealed class Contraparte
    {
        public string Rfc = "";
        public string Nombre = "";
        public int Facturas;
        public decimal Facturado;
        public int Notas;
        public decimal NotasImporte;
        public decimal Neto => Facturado - NotasImporte;
    }

    /// <summary>
    /// <paramref name="filas"/>: comprobantes del filtro (se usan solo facturas y notas de crédito no canceladas).
    /// <paramref name="clientes"/>: true analiza receptores (emitidos), false emisores (recibidos).
    /// </summary>
    public static byte[] Crear(IReadOnlyList<AdminXmlFilaDto> filas, bool clientes, string rfcEmpresa, string periodo)
    {
        List<AdminXmlFilaDto> usados = filas
            .Where(x => x.TipoCfdi is "I" or "E")
            .Where(x => !x.EstadoSat.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            .ToList();

        string RfcDe(AdminXmlFilaDto x) => (clientes ? x.RfcReceptor : x.RfcEmisor).Trim().ToUpperInvariant();
        string NombreDe(AdminXmlFilaDto x) => clientes ? x.NombreReceptor : x.NombreEmisor;

        // Público en general concentra ventas de mostrador: se reporta aparte para no deformar el 80-20.
        List<AdminXmlFilaDto> mostrador = clientes ? usados.Where(x => RfcDe(x) == PublicoGeneral).ToList() : [];
        decimal importeMostrador = mostrador.Sum(x => Signo(x) * Mxn(x));

        List<Contraparte> lista = usados
            .Where(x => !clientes || RfcDe(x) != PublicoGeneral)
            .GroupBy(RfcDe)
            .Select(g => new Contraparte
            {
                Rfc = g.Key,
                Nombre = g.Select(NombreDe).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "",
                Facturas = g.Count(x => x.TipoCfdi == "I"),
                Facturado = g.Where(x => x.TipoCfdi == "I").Sum(Mxn),
                Notas = g.Count(x => x.TipoCfdi == "E"),
                NotasImporte = g.Where(x => x.TipoCfdi == "E").Sum(Mxn)
            })
            .OrderByDescending(c => c.Neto)
            .ThenBy(c => c.Nombre, StringComparer.Ordinal)
            .ToList();

        string quien = clientes ? "CLIENTES" : "PROVEEDORES";
        string uno = clientes ? "CLIENTE" : "PROVEEDOR";
        string operacion = clientes ? "VENTA" : "COMPRA";

        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Segoe UI";

        int ultima = EscribirPareto(wb, lista, uno, operacion);
        EscribirDetalle(wb, usados, clientes, RfcDe, NombreDe);
        int enGrafico = Math.Min(MaximoGrafico, lista.Count(c => c.Neto > 0));
        EscribirDashboard(wb, lista, ultima, quien, uno, operacion, clientes, rfcEmpresa, periodo,
            usados.Count - mostrador.Count, mostrador.Count, importeMostrador, enGrafico);

        wb.Worksheet("Dashboard 80-20").SetTabActive().Select();
        wb.FullCalculationOnLoad = true;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        if (enGrafico > 0)
            AgregarGrafico(stream, Primera + enGrafico - 1, uno);

        return stream.ToArray();
    }

    private static int Signo(AdminXmlFilaDto x) => x.TipoCfdi == "E" ? -1 : 1;

    /// <summary>Total en pesos: los CFDI en otra moneda se convierten con su tipo de cambio.</summary>
    private static decimal Mxn(AdminXmlFilaDto x) =>
        x.Moneda is "" or "MXN" or "XXX" ? x.Total : x.Total * (x.TipoCambio is > 0 ? x.TipoCambio.Value : 1);

    // ---------- Hoja Pareto ----------

    private static int EscribirPareto(XLWorkbook wb, List<Contraparte> lista, string uno, string operacion)
    {
        var ws = wb.Worksheets.Add(HojaPareto);
        ws.SetTabColor(XLColor.FromHtml("#0084BE"));
        ws.ShowGridLines = false;

        var titulo = ws.Range("A2:K2").Merge();
        ws.Cell("A2").Value = $"ANÁLISIS 80-20 POR {uno} · {operacion} NETA (FACTURAS − NOTAS DE CRÉDITO, MXN)";
        titulo.Style.Fill.SetBackgroundColor(Color("005A80")).Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.White)
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(2).Height = 24;

        string[] encabezados = ["#", "RFC", uno, "FACTURAS", "FACTURADO", "NOTAS NC", "NOTAS DE CRÉDITO", "NETO", "% PART.", "% ACUM.", "CLASE"];
        for (var i = 0; i < encabezados.Length; i++) ws.Cell(4, i + 1).Value = encabezados[i];

        decimal totalPositivo = lista.Where(c => c.Neto > 0).Sum(c => c.Neto);
        decimal acumulado = 0;
        int ultima = Primera + Math.Max(lista.Count, 1) - 1;

        for (var i = 0; i < lista.Count; i++)
        {
            Contraparte c = lista[i];
            int r = Primera + i;

            // El acumulado y la clase solo aplican a importes netos positivos.
            decimal participacion = c.Neto > 0 && totalPositivo > 0 ? c.Neto / totalPositivo : 0;
            decimal antes = acumulado;
            acumulado += participacion;
            string clase = c.Neto <= 0 ? "—" : antes < 0.80m ? "A" : antes < 0.95m ? "B" : "C";

            ws.Cell(r, 1).Value = i + 1;
            ws.Cell(r, 2).Value = c.Rfc;
            ws.Cell(r, 3).Value = c.Nombre;
            ws.Cell(r, 4).Value = c.Facturas;
            ws.Cell(r, 5).Value = c.Facturado;
            ws.Cell(r, 6).Value = c.Notas;
            ws.Cell(r, 7).Value = c.NotasImporte;
            ws.Cell(r, 8).Value = c.Neto;
            ws.Cell(r, 9).Value = participacion;
            ws.Cell(r, 10).Value = c.Neto > 0 ? Math.Min(acumulado, 1m) : 0;
            ws.Cell(r, 11).Value = clase;

            // Auxiliares de la gráfica: nombre corto y línea de la meta de 80 %.
            ws.Cell(r, 13).Value = Corto(c.Nombre, c.Rfc);
            ws.Cell(r, 14).Value = 0.8;
        }

        if (lista.Count == 0)
            ws.Cell(Primera, 3).Value = "Sin facturas ni notas de crédito vigentes en el filtro";

        var tabla = ws.Range(4, 1, ultima, 11).CreateTable("TblPareto");
        tabla.Theme = XLTableTheme.TableStyleMedium2;
        tabla.ShowRowStripes = true;
        tabla.ShowTotalsRow = true;
        tabla.Field("RFC").TotalsRowLabel = "TOTAL";
        tabla.Field(uno).TotalsRowFormulaA1 = $"SUBTOTAL(103,TblPareto[{uno}])&\" {uno.ToLowerInvariant()}{(uno == "CLIENTE" ? "s" : "es")}\"";
        foreach (string campo in new[] { "FACTURAS", "FACTURADO", "NOTAS NC", "NOTAS DE CRÉDITO", "NETO" })
            tabla.Field(campo).TotalsRowFunction = XLTotalsRowFunction.Sum;

        foreach (int col in new[] { 5, 7, 8 }) ws.Range(Primera, col, ultima + 1, col).Style.NumberFormat.Format = Moneda;
        foreach (int col in new[] { 9, 10 }) ws.Range(Primera, col, ultima, col).Style.NumberFormat.Format = "0.0%";
        foreach (int col in new[] { 1, 4, 6, 9, 10, 11 }) ws.Range(Primera, col, ultima + 1, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(ultima + 1, 1, ultima + 1, 11).Style.Font.Bold = true;

        string clases = $"K{Primera}:K{ultima}";
        foreach (var (texto, fondo, letra) in new[] { ("A", "C6EFCE", "006100"), ("B", "FFEB9C", "9C5700"), ("C", "FFC7CE", "9C0006") })
            ws.Range(clases).AddConditionalFormat().WhenEquals($"\"{texto}\"").Fill.SetBackgroundColor(Color(fondo)).Font.SetFontColor(Color(letra)).Font.SetBold();
        ws.Range($"H{Primera}:H{ultima}").AddConditionalFormat().DataBar(Color("0084BE")).LowestValue().HighestValue();
        ws.Range($"J{Primera}:J{ultima}").AddConditionalFormat().DataBar(Color("C00000")).LowestValue().HighestValue();

        double[] anchos = [6, 16, 46, 11, 18, 11, 18, 18, 10, 10, 8];
        for (var i = 0; i < anchos.Length; i++) ws.Column(i + 1).Width = anchos[i];

        ws.Cell("M4").Value = "ETIQUETA GRÁFICA";
        ws.Cell("N4").Value = "Meta 80%";
        ws.Range($"M4:N{ultima}").Style.Font.SetFontColor(Color("A6A6A6")).Font.SetFontSize(8);
        ws.Column(13).Width = 24;
        ws.Column(14).Width = 9;

        ws.SheetView.FreezeRows(4);
        return ultima;
    }

    private static string Corto(string nombre, string rfc)
    {
        string texto = string.IsNullOrWhiteSpace(nombre) ? rfc : nombre.Trim();
        return texto.Length <= 24 ? texto : texto[..23] + "…";
    }

    // ---------- Hoja Detalle CFDI ----------

    private static void EscribirDetalle(
        XLWorkbook wb, List<AdminXmlFilaDto> usados, bool clientes,
        Func<AdminXmlFilaDto, string> rfcDe, Func<AdminXmlFilaDto, string> nombreDe)
    {
        var ws = wb.Worksheets.Add("Detalle CFDI");
        ws.SetTabColor(XLColor.FromHtml("#7F7F7F"));

        string[] encabezados = ["Fecha", "Tipo", "Serie", "Folio", "UUID", clientes ? "RFC Receptor" : "RFC Emisor",
            clientes ? "Cliente" : "Proveedor", "Moneda", "Tipo de cambio", "Total", "Total MXN (con signo)", "Estado SAT"];
        for (var i = 0; i < encabezados.Length; i++) ws.Cell(1, i + 1).Value = encabezados[i];

        var ordenados = usados.OrderBy(x => x.FechaEmision).ToList();

        for (var i = 0; i < ordenados.Count; i++)
        {
            AdminXmlFilaDto x = ordenados[i];
            int r = i + 2;
            ws.Cell(r, 1).Value = x.FechaEmision;
            ws.Cell(r, 2).Value = x.TipoCfdi == "E" ? "Nota de crédito" : "Factura";
            ws.Cell(r, 3).Value = x.Serie;
            ws.Cell(r, 4).Value = x.Folio;
            ws.Cell(r, 5).Value = x.Uuid;
            ws.Cell(r, 6).Value = rfcDe(x);
            ws.Cell(r, 7).Value = nombreDe(x);
            ws.Cell(r, 8).Value = x.Moneda;
            ws.Cell(r, 9).Value = x.TipoCambio ?? 1;
            ws.Cell(r, 10).Value = x.Total;
            ws.Cell(r, 11).Value = Signo(x) * Mxn(x);
            ws.Cell(r, 12).Value = x.EstadoSat;
        }

        var tabla = ws.Range(1, 1, Math.Max(ordenados.Count, 1) + 1, encabezados.Length).CreateTable("TblDetalle8020");
        tabla.Theme = XLTableTheme.TableStyleLight9;
        ws.Column(1).Style.DateFormat.Format = "dd/MM/yyyy";
        ws.Columns(10, 11).Style.NumberFormat.Format = Moneda;
        ws.Columns().AdjustToContents(1, Math.Min(ordenados.Count + 1, 300));
        ws.SheetView.FreezeRows(1);
    }

    // ---------- Dashboard ----------

    private static readonly int[] Grupos = [2, 6, 10, 14];

    private static void EscribirDashboard(
        XLWorkbook wb, List<Contraparte> lista, int ultima, string quien, string uno, string operacion, bool clientes,
        string rfcEmpresa, string periodo, int comprobantes, int cfdiMostrador, decimal importeMostrador, int enGrafico)
    {
        var ws = wb.Worksheets.Add("Dashboard 80-20", 1);
        ws.SetTabColor(XLColor.FromHtml("#0B2E4F"));
        ws.ShowGridLines = false;
        ws.Style.Font.FontName = "Calibri";

        ws.Column(1).Width = 2;
        ws.Column(17).Width = 2;
        foreach (int c in new[] { 5, 9, 13 }) ws.Column(c).Width = 2.29;
        foreach (int inicio in Grupos) ws.Columns(inicio, inicio + 2).Width = 15.5;
        ws.Row(2).Height = 36;
        ws.Row(3).Height = 20;
        ws.Row(7).Height = 32;

        string P(string campo) => $"TblPareto[{campo}]";
        string tipoCfdi = clientes ? "EMITIDOS" : "RECIBIDOS";

        Banda(ws, "B2:P2", $"ANÁLISIS 80-20 DE {quien} · CFDI {tipoCfdi} {periodo}", "003A55", 18, true);
        Banda(ws, "B3:P3", $"{rfcEmpresa} · {comprobantes:N0} facturas y notas de crédito vigentes · importes netos en MXN" +
            (clientes ? " · sin público en general" : ""), "0084BE", 10, false);

        string total = $"SUMIFS({P("NETO")},{P("NETO")},\">0\")";
        string sumaA = $"SUMIFS({P("NETO")},{P("CLASE")},\"A\")";
        string cuentaA = $"COUNTIF({P("CLASE")},\"A\")";
        string positivos = $"COUNTIF({P("NETO")},\">0\")";

        var tarjetas = new (string Titulo, string Valor, string Pie, string Formato, string[] Tonos)[]
        {
            ($"{operacion} NETA", total, $"\"{(clientes ? "Ventas" : "Compras")} netas de \"&{positivos}&\" {quien.ToLowerInvariant()}\"", Moneda,
                ["003A55", "00587F", "0084BE", "00B0F0"]),
            ($"{quien} CLASE A", cuentaA, $"IFERROR(TEXT({cuentaA}/{positivos},\"0%\"),\"0%\")&\" de los {quien.ToLowerInvariant()} generan el \"&IFERROR(TEXT({sumaA}/{total},\"0%\"),\"0%\")", "0",
                ["375623", "4E7D33", "70AD47", "A9D08E"]),
            ($"CONCENTRACIÓN TOP 10", $"IFERROR(SUM(INDEX({P("NETO")},1):INDEX({P("NETO")},MIN(10,{positivos})))/{total},0)",
                $"\"de la {operacion.ToLowerInvariant()} neta en los 10 principales\"", "0.0%", ["7030A0", "8E4CC0", "A66FD1", "C9A5E4"]),
            ($"{uno} PRINCIPAL", $"IFERROR(INDEX({P(uno)},1),\"—\")",
                $"IFERROR(TEXT(INDEX({P("% PART.")},1),\"0.0%\")&\" del total · \"&TEXT(INDEX({P("NETO")},1),\"$#,##0\"),\"\")", "@",
                ["7F6000", "A07800", "BF9000", "FFD966"]),
        };

        for (var g = 0; g < tarjetas.Length; g++)
        {
            var t = tarjetas[g];
            int col = Grupos[g];

            for (var r = 5; r <= 8; r++)
            {
                ws.Range(r, col, r, col + 2).Merge().Style
                    .Fill.SetBackgroundColor(Color(t.Tonos[r - 5])).Font.SetFontColor(XLColor.White)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                    .Alignment.SetWrapText();
            }

            ws.Cell(5, col).Value = t.Titulo;
            ws.Cell(5, col).Style.Font.SetBold().Font.SetFontSize(9);
            ws.Cell(7, col).FormulaA1 = t.Valor;
            ws.Cell(7, col).Style.Font.SetBold().Font.SetFontSize(g == 3 ? 11 : 20).NumberFormat.Format = t.Formato;
            ws.Cell(8, col).FormulaA1 = t.Pie;
            ws.Cell(8, col).Style.Font.SetFontSize(8);
        }

        ws.Row(8).Height = 28;

        // Clasificación ABC.
        Banda(ws, "B10:H10", "CLASIFICACIÓN ABC", "003A55", 11, true);
        Encabezado(ws, "B11", "CLASE");
        Encabezado(ws, "C11:D11", "CRITERIO");
        Encabezado(ws, "E11", quien);
        Encabezado(ws, "F11", $"% {quien}");
        Encabezado(ws, "G11", "IMPORTE NETO");
        Encabezado(ws, "H11", "% IMPORTE");

        var clases = new (string Clase, string Criterio, string Fondo, string Letra)[]
        {
            ("A", "Hasta 80 % acumulado", "C6EFCE", "006100"),
            ("B", "Del 80 % al 95 %", "FFEB9C", "9C5700"),
            ("C", "Último 5 %", "FFC7CE", "9C0006"),
        };

        for (var i = 0; i < clases.Length; i++)
        {
            var c = clases[i];
            int r = 12 + i;
            ws.Cell(r, 2).Value = c.Clase;
            ws.Cell(r, 2).Style.Fill.SetBackgroundColor(Color(c.Fondo)).Font.SetFontColor(Color(c.Letra)).Font.SetBold().Font.SetFontSize(14)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Range(r, 3, r, 4).Merge();
            ws.Cell(r, 3).Value = c.Criterio;
            ws.Cell(r, 5).FormulaA1 = $"COUNTIF({P("CLASE")},\"{c.Clase}\")";
            ws.Cell(r, 6).FormulaA1 = $"IFERROR(E{r}/{positivos},0)";
            ws.Cell(r, 7).FormulaA1 = $"SUMIFS({P("NETO")},{P("CLASE")},\"{c.Clase}\")";
            ws.Cell(r, 8).FormulaA1 = $"IFERROR(G{r}/{total},0)";
            ws.Row(r).Height = 22;
        }

        ws.Cell(15, 2).Value = "TOTAL";
        ws.Range(15, 3, 15, 4).Merge();
        ws.Cell(15, 5).FormulaA1 = "SUM(E12:E14)";
        ws.Cell(15, 6).FormulaA1 = "SUM(F12:F14)";
        ws.Cell(15, 7).FormulaA1 = "SUM(G12:G14)";
        ws.Cell(15, 8).FormulaA1 = "SUM(H12:H14)";
        ws.Range("B15:H15").Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetTopBorderColor(Color("003A55"));
        ws.Range("B12:H15").Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Range("E12:F15").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Range("F12:F15").Style.NumberFormat.Format = "0.0%";
        ws.Range("H12:H15").Style.NumberFormat.Format = "0.0%";
        ws.Range("G12:G15").Style.NumberFormat.Format = Moneda;
        ws.Range("H12:H14").AddConditionalFormat().DataBar(Color("0084BE")).LowestValue().HighestValue();

        // Top 15.
        Banda(ws, "B17:H17", $"TOP 15 {quien} POR {operacion} NETA", "00587F", 11, true);
        Encabezado(ws, "B18", "#");
        Encabezado(ws, "C18:E18", uno);
        Encabezado(ws, "F18", "NETO");
        Encabezado(ws, "G18", "% PART.");
        Encabezado(ws, "H18", "% ACUM.");

        for (var i = 0; i < 15; i++)
        {
            int r = 19 + i;
            int n = i + 1;
            ws.Cell(r, 2).FormulaA1 = $"IF(ROWS({P("RFC")})>={n},{n},\"\")";
            ws.Range(r, 3, r, 5).Merge();
            ws.Cell(r, 3).FormulaA1 = $"IFERROR(INDEX({P(uno)},{n}),\"\")";
            ws.Cell(r, 6).FormulaA1 = $"IFERROR(INDEX({P("NETO")},{n}),\"\")";
            ws.Cell(r, 7).FormulaA1 = $"IFERROR(INDEX({P("% PART.")},{n}),\"\")";
            ws.Cell(r, 8).FormulaA1 = $"IFERROR(INDEX({P("% ACUM.")},{n}),\"\")";
        }

        ws.Range("B19:H33").Style.Font.SetFontSize(9);
        ws.Range("B19:B33").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Range("C19:C33").Style.Alignment.SetIndent(1);
        ws.Range("F19:F33").Style.NumberFormat.Format = Moneda;
        ws.Range("G19:H33").Style.NumberFormat.Format = "0.0%";
        ws.Range("B19:H33").AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(Color("F2F9FD"));
        ws.Range("F19:F33").AddConditionalFormat().DataBar(Color("00A0DC")).LowestValue().HighestValue();
        ws.Range("H19:H33").AddConditionalFormat().WhenLessThan(0.8).Font.SetFontColor(Color("006100"));

        // Curva de Pareto (la gráfica se agrega con OpenXML sobre J10:P33).
        Banda(ws, "J10:P10", enGrafico > 0 ? $"CURVA DE PARETO · {enGrafico} {quien} PRINCIPALES" : "CURVA DE PARETO", "003A55", 11, true);

        if (enGrafico == 0)
        {
            ws.Range("J12:P12").Merge().Value = "Sin importes netos positivos para graficar.";
        }

        int fila = 35;

        if (clientes && cfdiMostrador > 0)
        {
            var aviso = ws.Range($"B{fila}:P{fila}").Merge();
            aviso.Value = $"Público en general ({PublicoGeneral}) no entra al análisis: {cfdiMostrador:N0} CFDI por {importeMostrador.ToString("C2", Mx)} netos.";
            aviso.Style.Fill.SetBackgroundColor(Color("FFF7D6")).Font.SetFontColor(Color("7F6000")).Font.SetFontSize(9).Alignment.SetIndent(1);
            fila++;
        }

        var nota = ws.Range($"B{fila}:P{fila}").Merge();
        nota.Value = $"Generado por CFDI Manager el {DateTime.Now:dd/MM/yyyy HH:mm} · Neto = facturas − notas de crédito vigentes (sin cancelados), convertido a MXN con el tipo de cambio del CFDI. " +
                     "Clase A: contrapartes que suman hasta el 80 % del neto; B: hasta el 95 %; C: el resto.";
        nota.Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080")).Alignment.SetWrapText();
        ws.Row(fila).Height = 26;
    }

    private static void Banda(IXLWorksheet ws, string rango, string texto, string fondo, double tamano, bool bold)
    {
        var r = ws.Range(rango).Merge();
        r.FirstCell().Value = texto;
        r.Style.Fill.SetBackgroundColor(Color(fondo)).Font.SetFontColor(XLColor.White).Font.SetFontSize(tamano).Font.SetBold(bold)
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }

    private static void Encabezado(IXLWorksheet ws, string rango, string texto)
    {
        var r = ws.Range(rango);
        if (r.Cells().Count() > 1) r.Merge();
        r.FirstCell().Value = texto;
        r.Style.Fill.SetBackgroundColor(Color("D9EEF9")).Font.SetBold().Font.SetFontSize(9).Font.SetFontColor(Color("003A55"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }

    private static XLColor Color(string hex) => XLColor.FromHtml("#" + hex);

    // ---------- Gráfica de Pareto (ClosedXML no escribe gráficas) ----------

    private static void AgregarGrafico(MemoryStream stream, int ultimaFila, string uno)
    {
        using var doc = SpreadsheetDocument.Open(stream, true);
        var wbPart = doc.WorkbookPart!;
        string id = wbPart.Workbook.Sheets!.Elements<S.Sheet>().Single(x => x.Name == "Dashboard 80-20").Id!;
        var hoja = (WorksheetPart)wbPart.GetPartById(id);

        var dibujo = hoja.AddNewPart<DrawingsPart>();
        var grafico = dibujo.AddNewPart<ChartPart>();
        ReporteGeneral.Escribir(grafico, GraficoXml(ultimaFila, uno));
        ReporteGeneral.Escribir(dibujo, $"""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
            <xdr:twoCellAnchor editAs="oneCell"><xdr:from><xdr:col>9</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>10</xdr:row><xdr:rowOff>38100</xdr:rowOff></xdr:from><xdr:to><xdr:col>16</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>33</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
            <xdr:graphicFrame macro=""><xdr:nvGraphicFramePr><xdr:cNvPr id="2" name="g_pareto"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm>
            <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart"><c:chart xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="{dibujo.GetIdOfPart(grafico)}"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>
            </xdr:wsDr>
            """);
        ReporteGeneral.InsertarDibujo(hoja, hoja.GetIdOfPart(dibujo));
    }

    /// <summary>Barras de neto por contraparte (eje izquierdo) y líneas de % acumulado y meta 80 % (eje derecho).</summary>
    private static string GraficoXml(int ultima, string uno)
    {
        string Rango(string col) => $"'{HojaPareto}'!${col}${Primera}:${col}${ultima}";
        string Titulo(string col) => $"'{HojaPareto}'!${col}$4";

        const string texto8 = """<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="800"><a:solidFill><a:srgbClr val="595959"/></a:solidFill></a:defRPr></a:pPr><a:endParaRPr lang="es-MX"/></a:p></c:txPr>""";
        const string textoGirado = """<c:txPr><a:bodyPr rot="-2700000" vert="horz"/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="700"><a:solidFill><a:srgbClr val="595959"/></a:solidFill></a:defRPr></a:pPr><a:endParaRPr lang="es-MX"/></a:p></c:txPr>""";
        const string linea = """<c:spPr><a:ln w="9525"><a:solidFill><a:srgbClr val="D9D9D9"/></a:solidFill></a:ln></c:spPr>""";
        string categorias = $"<c:cat><c:strRef><c:f>{Rango("M")}</c:f></c:strRef></c:cat>";

        return $"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
            <c:roundedCorners val="0"/>
            <c:chart>
            <c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="1000" b="1"><a:solidFill><a:srgbClr val="003A55"/></a:solidFill></a:defRPr></a:pPr><a:r><a:rPr lang="es-MX" sz="1000" b="1"><a:solidFill><a:srgbClr val="003A55"/></a:solidFill></a:rPr><a:t>Neto por {uno.ToLowerInvariant()} y % acumulado</a:t></a:r></a:p></c:rich></c:tx><c:overlay val="0"/></c:title>
            <c:autoTitleDeleted val="0"/>
            <c:plotArea><c:layout/>
            <c:barChart><c:barDir val="col"/><c:grouping val="clustered"/><c:varyColors val="0"/>
            <c:ser><c:idx val="0"/><c:order val="0"/><c:tx><c:strRef><c:f>{Titulo("H")}</c:f></c:strRef></c:tx>
            <c:spPr><a:solidFill><a:srgbClr val="0084BE"/></a:solidFill></c:spPr><c:invertIfNegative val="0"/>
            {categorias}
            <c:val><c:numRef><c:f>{Rango("H")}</c:f></c:numRef></c:val>
            </c:ser>
            <c:gapWidth val="40"/><c:axId val="501"/><c:axId val="502"/></c:barChart>
            <c:lineChart><c:grouping val="standard"/><c:varyColors val="0"/>
            <c:ser><c:idx val="1"/><c:order val="1"/><c:tx><c:strRef><c:f>{Titulo("J")}</c:f></c:strRef></c:tx>
            <c:spPr><a:ln w="28575" cap="rnd"><a:solidFill><a:srgbClr val="C00000"/></a:solidFill><a:round/></a:ln></c:spPr>
            <c:marker><c:symbol val="circle"/><c:size val="4"/><c:spPr><a:solidFill><a:srgbClr val="C00000"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr></c:marker>
            {categorias}
            <c:val><c:numRef><c:f>{Rango("J")}</c:f></c:numRef></c:val><c:smooth val="0"/>
            </c:ser>
            <c:ser><c:idx val="2"/><c:order val="2"/><c:tx><c:strRef><c:f>{Titulo("N")}</c:f></c:strRef></c:tx>
            <c:spPr><a:ln w="15875"><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill><a:prstDash val="dash"/></a:ln></c:spPr>
            <c:marker><c:symbol val="none"/></c:marker>
            {categorias}
            <c:val><c:numRef><c:f>{Rango("N")}</c:f></c:numRef></c:val><c:smooth val="0"/>
            </c:ser>
            <c:marker val="1"/><c:axId val="503"/><c:axId val="504"/></c:lineChart>
            <c:catAx><c:axId val="501"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="b"/><c:numFmt formatCode="General" sourceLinked="1"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/>{linea}{textoGirado}<c:crossAx val="502"/><c:crosses val="autoZero"/><c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/><c:tickLblSkip val="1"/><c:noMultiLvlLbl val="0"/></c:catAx>
            <c:valAx><c:axId val="502"/><c:scaling><c:orientation val="minMax"/><c:min val="0"/></c:scaling><c:delete val="0"/><c:axPos val="l"/><c:majorGridlines>{linea}</c:majorGridlines><c:numFmt formatCode="&quot;$&quot;#,##0" sourceLinked="0"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/><c:spPr><a:ln><a:noFill/></a:ln></c:spPr>{texto8}<c:crossAx val="501"/><c:crosses val="autoZero"/><c:crossBetween val="between"/></c:valAx>
            <c:catAx><c:axId val="503"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="1"/><c:axPos val="b"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/><c:crossAx val="504"/><c:crosses val="autoZero"/><c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/><c:noMultiLvlLbl val="0"/></c:catAx>
            <c:valAx><c:axId val="504"/><c:scaling><c:orientation val="minMax"/><c:max val="1"/><c:min val="0"/></c:scaling><c:delete val="0"/><c:axPos val="r"/><c:numFmt formatCode="0%" sourceLinked="0"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/><c:spPr><a:ln><a:noFill/></a:ln></c:spPr>{texto8}<c:crossAx val="503"/><c:crosses val="max"/><c:crossBetween val="between"/><c:majorUnit val="0.2"/></c:valAx>
            <c:spPr><a:noFill/></c:spPr>
            </c:plotArea>
            <c:legend><c:legendPos val="b"/><c:overlay val="0"/>{texto8}</c:legend>
            <c:plotVisOnly val="1"/><c:dispBlanksAs val="gap"/>
            </c:chart>
            <c:spPr><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill><a:ln w="9525"><a:solidFill><a:srgbClr val="D9D9D9"/></a:solidFill></a:ln></c:spPr>
            </c:chartSpace>
            """;
    }
}
