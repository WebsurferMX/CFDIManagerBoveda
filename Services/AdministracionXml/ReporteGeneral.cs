using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Reporte general con el formato de "RFC Emitidas MES AÑO.xlsx": hojas Factura, NCredito, Traslado, Pago y Nomina
/// (columnas de MiAdminXML) y, para CFDI emitidos, el dashboard compacto de ventas por cliente con su hoja Resumen Clientes.
/// </summary>
public static class ReporteGeneral
{
    /// <summary>Datos del CFDI que el reporte no lee del XML. Direccion: E (emitido) o R (recibido).</summary>
    public sealed record Origen(string Rfc, string Direccion, string Ruta, DateTimeOffset? Fecha);

    /// <summary>Filtro usado para el nombre del archivo y el periodo del dashboard.</summary>
    public sealed record Filtro(string? Rfc, string? Direccion, int? Anio, int? Mes);

    public sealed record Entrada(Origen Fila, XDocument Xml, string? EstadoSat, bool Verificado);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Meses = ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];
    private const string Contable = "_-\"$\"* #,##0.00_-;\\-\"$\"* #,##0.00_-;_-\"$\"* \"-\"??_-;_-@_-";
    private const string Moneda = "\"$\"#,##0.00;\\(\"$\"#,##0.00\\);\\-";
    private const string PublicoGeneral = "XAXX010101000";

    public static (byte[] Bytes, string Nombre) Crear(IReadOnlyList<Entrada> entradas, Filtro filtro)
    {
        var leidos = entradas.Select(x => new Leido(x)).ToList();
        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Segoe UI";
        var conDatos = new HashSet<string>();
        foreach (var hoja in Hojas)
        {
            var filas = leidos.Where(x => x.Tipo == hoja.Tipo).SelectMany(x => hoja.Tipo == "P" ? FilasPago(x) : [new Fila(x)]).ToList();
            // Factura y NCredito siempre existen cuando hay dashboard: sus fórmulas usan Table13 y Table1.
            if (filas.Count == 0 && hoja.Tipo is not ("I" or "E")) continue;
            EscribirHoja(wb, hoja, filas);
            if (filas.Count > 0) conDatos.Add(hoja.Nombre);
        }

        var emitidas = leidos.Count > 0 && leidos.All(x => x.E.Fila.Direccion == "E");
        var direccion = filtro.Direccion ?? (leidos.Select(x => x.E.Fila.Direccion).Distinct().SingleOrDefault());
        var rfc = filtro.Rfc?.Trim().ToUpperInvariant() ?? leidos.Select(x => x.E.Fila.Rfc).Distinct().SingleOrDefault() ?? "Todos";
        var (periodo, periodoCorto) = Periodo(filtro, leidos);
        var clientes = emitidas ? Clientes(leidos) : [];
        Dashboard? dashboard = null;
        if (clientes.Count > 0)
        {
            var resumen = EscribirResumen(wb, clientes, periodo);
            dashboard = EscribirDashboard(wb, clientes, rfc, periodo, resumen);
        }
        foreach (var name in new[] { "Factura", "NCredito" })
            if (dashboard is null && !conDatos.Contains(name) && wb.Worksheets.TryGetWorksheet(name, out var vacia)) vacia.Delete();
        if (!wb.Worksheets.Any())
            wb.Worksheets.Add("Sin datos").Cell(1, 1).Value = "No hay CFDI para el filtro seleccionado";
        wb.Worksheets.First().SetTabActive().Select();
        wb.FullCalculationOnLoad = true;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        if (dashboard is not null) AgregarGraficoYSegmentaciones(stream, dashboard);
        var carpeta = direccion switch { "E" => "Emitidas", "R" => "Recibidas", _ => "CFDI" };
        return (stream.ToArray(), $"{rfc} {carpeta} {periodoCorto}.xlsx");
    }

    // ---------- Hojas de datos ----------

    private sealed record Columna(string Titulo, Func<Fila, object?> Valor, bool Dinero = false);
    private sealed record Hoja(string Nombre, string Tabla, string Tipo, string Tab, Columna[] Columnas);
    private sealed record Fila(Leido C, XElement? Pago = null, XElement? Docto = null);

    private static IEnumerable<Fila> FilasPago(Leido c)
    {
        foreach (var pago in c.Pagos)
        {
            var doctos = pago.Elements().Where(x => x.Name.LocalName == "DoctoRelacionado").ToList();
            if (doctos.Count == 0) yield return new Fila(c, pago);
            foreach (var docto in doctos) yield return new Fila(c, pago, docto);
        }
    }

    private static readonly string[] TasasIeps = ["0.03", "0.06", "0.07", "0.08", "0.09", "0.265", "0.3", "0.53", "1.6"];

    private static readonly Columna[] Comprobante =
    [
        new("Verificado ó Asoc.", f => f.C.E.Verificado ? "Sí" : ""),
        new("Estado SAT", f => f.C.E.EstadoSat ?? ""),
        new("Tipo", f => f.C.Tipo switch { "I" => "Factura", "E" => "NotaCredito", "T" => "Traslado", "P" => "Pago", "N" => "Nomina", var t => t }),
        new("Fecha Emision", f => f.C.FechaCorta),
        new("Fecha Timbrado", f => f.C.A(f.C.Timbre, "FechaTimbrado")),
        new("Serie", f => f.C.A(f.C.Root, "Serie")),
        new("Folio", f => f.C.A(f.C.Root, "Folio")),
        new("UUID", f => f.C.A(f.C.Timbre, "UUID")),
        new("UUID Relacion", f => string.Join(" ", f.C.Relacionados)),
        new("RFC Emisor", f => f.C.A(f.C.Emisor, "Rfc").ToUpperInvariant()),
        new("Nombre Emisor", f => f.C.A(f.C.Emisor, "Nombre")),
        new("LugarDeExpedicion", f => f.C.A(f.C.Root, "LugarExpedicion")),
        new("RFC Receptor", f => f.C.ReceptorRfc),
        new("Nombre Receptor", f => f.C.ReceptorNombre),
        new("ResidenciaFiscal", f => f.C.A(f.C.Receptor, "ResidenciaFiscal")),
        new("NumRegIdTrib", f => f.C.A(f.C.Receptor, "NumRegIdTrib")),
        new("UsoCFDI", f => Catalogo(Catalogos.Uso, f.C.A(f.C.Receptor, "UsoCFDI"))),
        new("SubTotal", f => f.C.N(f.C.Root, "SubTotal"), true),
        new("Descuento", f => f.C.N(f.C.Root, "Descuento"), true),
        new("Total IEPS", f => f.C.IepsTotal, true),
        new("IVA 16%", f => f.C.Iva.GetValueOrDefault("0.16"), true),
        new("Retenido IVA", f => f.C.IvaRetenido, true),
        new("Retenido ISR", f => f.C.IsrRetenido, true),
        new("ISH", f => f.C.Ish, true),
        new("Total", f => f.C.Total, true),
        new("TotalOriginal", _ => ""),
        new("Total Trasladados", f => f.C.TotalTrasladados, true),
        new("Total Retenidos", f => f.C.TotalRetenidos, true),
        new("Total LocalTrasladado", f => f.C.LocalTrasladado, true),
        new("Total LocalRetenido", f => f.C.LocalRetenido, true),
        new("Complemento", f => string.Join(", ", f.C.Complementos)),
        new("Moneda", f => f.C.A(f.C.Root, "Moneda")),
        new("Tipo De Cambio", f => f.C.A(f.C.Root, "TipoCambio") is { Length: > 0 } tc ? tc : "1"),
        new("FormaDePago", f => Catalogo(Catalogos.Forma, f.C.A(f.C.Root, "FormaPago") is { Length: > 0 } fp ? fp : f.C.V32 ? f.C.A(f.C.Root, "formaDePago") : "")),
        new("Metodo de Pago", f => Catalogo(Catalogos.Metodo, f.C.A(f.C.Root, "MetodoPago") is { Length: > 0 } mp ? mp : f.C.V32 ? f.C.A(f.C.Root, "metodoDePago") : "")),
        new("NumCtaPago", f => f.C.V32 ? f.C.A(f.C.Root, "NumCtaPago") : ""),
        new("Condicion de Pago", f => f.C.A(f.C.Root, "CondicionesDePago")),
        new("Conceptos", f => f.C.Conceptos),
        new("Combustible", f => f.C.Complementos.Any(x => x.Contains("Combustible", StringComparison.OrdinalIgnoreCase)) ? "Sí" : "No"),
        .. TasasIeps.Select(t => new Columna($"IEPS {decimal.Parse(t, Inv) * 100:0.##}%", f => f.C.Ieps.GetValueOrDefault(t), true)),
        new("Archivo XML", f => Path.GetFileName(f.C.E.Fila.Ruta)),
        new("Direccion Emisor", f => f.C.Domicilio(f.C.Emisor).Direccion),
        new("Localidad Emisor", f => f.C.Domicilio(f.C.Emisor).Localidad),
        new("Direccion Receptor", f => f.C.Domicilio(f.C.Receptor).Direccion),
        new("Localidad Receptor", f => f.C.Domicilio(f.C.Receptor).Localidad),
        new("IVA 8%", f => f.C.Iva.GetValueOrDefault("0.08"), true),
        new("IEPS 30.4%", f => f.C.Ieps.GetValueOrDefault("0.304"), true),
        new("IVA Ret 6%", f => f.C.IvaRet6, true),
        new("RegimenFiscalReceptor", f => Catalogo(Catalogos.Regimen, f.C.A(f.C.Receptor, "RegimenFiscalReceptor"))),
        new("DomicilioFiscalReceptor", f => f.C.A(f.C.Receptor, "DomicilioFiscalReceptor")),
    ];

    private static readonly Columna[] Pago =
    [
        new("Verificado ó Asoc.", f => f.C.E.Verificado ? "Sí" : ""),
        new("Estado SAT", f => f.C.E.EstadoSat ?? ""),
        new("Version", f => f.C.A(f.C.Root, "Version")),
        new("TipoComprobante", f => f.C.Tipo),
        new("Fecha Emision", f => f.C.FechaCorta),
        new("Serie", f => f.C.A(f.C.Root, "Serie")),
        new("Folio", f => f.C.A(f.C.Root, "Folio")),
        new("UUID", f => f.C.A(f.C.Timbre, "UUID")),
        new("RFC Emisor", f => f.C.A(f.C.Emisor, "Rfc").ToUpperInvariant()),
        new("Nombre Emisor", f => f.C.A(f.C.Emisor, "Nombre")),
        new("RFC Receptor", f => f.C.ReceptorRfc),
        new("Nombre Receptor", f => f.C.ReceptorNombre),
        new("UsoCFDI", f => Catalogo(Catalogos.Uso, f.C.A(f.C.Receptor, "UsoCFDI"))),
        new("FechaPago", f => f.C.A(f.Pago, "FechaPago")),
        new("FormaDePagoP", f => Catalogo(Catalogos.Forma, f.C.A(f.Pago, "FormaDePagoP"))),
        new("MonedaP", f => f.C.A(f.Pago, "MonedaP")),
        new("Monto", f => f.C.N(f.Pago, "Monto"), true),
        new("UUIDRel", f => f.C.A(f.Docto, "IdDocumento").ToUpperInvariant()),
        new("SerieRel", f => f.C.A(f.Docto, "Serie")),
        new("FolioRel", f => f.C.A(f.Docto, "Folio")),
        new("NumParcialidad", f => f.C.A(f.Docto, "NumParcialidad")),
        new("ImpSaldoAnt", f => f.C.N(f.Docto, "ImpSaldoAnt"), true),
        new("ImpPagado", f => f.C.N(f.Docto, "ImpPagado"), true),
        new("ImpSaldoInsoluto", f => f.C.N(f.Docto, "ImpSaldoInsoluto"), true),
        new("Num Operacion", f => f.C.A(f.Pago, "NumOperacion")),
        new("Cuenta Destino", f => f.C.A(f.Pago, "CtaBeneficiario")),
        new("Cuenta Origen", f => f.C.A(f.Pago, "CtaOrdenante")),
        new("RfcEmisorCtaDestino", f => f.C.A(f.Pago, "RfcEmisorCtaBen")),
        new("RfcEmisorCtaOrigen", f => f.C.A(f.Pago, "RfcEmisorCtaOrd")),
        new("NomBancoOrdExtranjero", f => f.C.A(f.Pago, "NomBancoOrdExt")),
        new("TipoCadPago", f => f.C.A(f.Pago, "TipoCadPago")),
        new("CadPago", f => f.C.A(f.Pago, "CadPago")),
        new("Conceptos", f => f.C.Conceptos),
        new("Archivo XML", f => Path.GetFileName(f.C.E.Fila.Ruta)),
        new("Total", f => f.C.Total, true),
    ];

    private static readonly Columna[] Nomina =
    [
        new("Verificado ó Asoc.", f => f.C.E.Verificado ? "Sí" : ""),
        new("EstadoSAT", f => f.C.E.EstadoSat ?? ""),
        new("FechaEmision", f => f.C.FechaCorta),
        new("FechaTimbrado", f => f.C.A(f.C.Timbre, "FechaTimbrado")),
        new("Serie", f => f.C.A(f.C.Root, "Serie")),
        new("Folio", f => f.C.A(f.C.Root, "Folio")),
        new("UUID", f => f.C.A(f.C.Timbre, "UUID")),
        new("RFC Receptor", f => f.C.ReceptorRfc),
        new("NombreReceptor", f => f.C.ReceptorNombre),
        new("RegistroPatronal", f => f.C.A(f.C.Hijo(f.C.Nomina, "Emisor"), "RegistroPatronal")),
        new("TipoNomina", f => Catalogo(Catalogos.TipoNomina, f.C.A(f.C.Nomina, "TipoNomina"))),
        new("FechaPago", f => f.C.A(f.C.Nomina, "FechaPago")),
        new("FechaInicialPago", f => f.C.A(f.C.Nomina, "FechaInicialPago")),
        new("FechaFinalPago", f => f.C.A(f.C.Nomina, "FechaFinalPago")),
        new("NumDiasPagados", f => f.C.A(f.C.Nomina, "NumDiasPagados")),
        new("TotalPercepciones", f => f.C.N(f.C.Nomina, "TotalPercepciones"), true),
        new("TotalDeducciones", f => f.C.N(f.C.Nomina, "TotalDeducciones"), true),
        new("TotalOtrosPagos", f => f.C.N(f.C.Nomina, "TotalOtrosPagos"), true),
        new("SubTotal", f => f.C.N(f.C.Root, "SubTotal"), true),
        new("Descuento", f => f.C.N(f.C.Root, "Descuento"), true),
        // Retención de ISR del nodo Impuestos del comprobante; el ISR de nómina va en TotalImpuestosRetenidosDed.
        new("ISR XML", f => f.C.IsrRetenido, true),
        new("Total", f => f.C.Total, true),
        new("MetodoPago", f => Catalogo(Catalogos.Metodo, f.C.A(f.C.Root, "MetodoPago"))),
        new("Regimen", f => f.C.A(f.C.Emisor, "RegimenFiscal")),
        new("ArchivoXML", f => Path.GetFileName(f.C.E.Fila.Ruta)),
        new("Conceptos", f => f.C.Conceptos),
        new("Version", f => f.C.A(f.C.Nomina, "Version")),
        new("Moneda", f => f.C.A(f.C.Root, "Moneda")),
        new("TipoCambio", f => f.C.A(f.C.Root, "TipoCambio")),
        new("ReceptorCurp", f => f.C.A(f.C.NominaReceptor, "Curp")),
        new("NumSeguridadSocial", f => f.C.A(f.C.NominaReceptor, "NumSeguridadSocial")),
        new("FechaInicioRelLaboral", f => f.C.A(f.C.NominaReceptor, "FechaInicioRelLaboral")),
        new("Antigüedad", f => f.C.A(f.C.NominaReceptor, "Antigüedad") is { Length: > 0 } a ? a : f.C.A(f.C.NominaReceptor, "Antiguedad")),
        new("TipoContrato", f => Catalogo(Catalogos.TipoContrato, f.C.A(f.C.NominaReceptor, "TipoContrato"))),
        new("Sindicalizado", f => f.C.A(f.C.NominaReceptor, "Sindicalizado")),
        new("TipoJornada", f => Catalogo(Catalogos.TipoJornada, f.C.A(f.C.NominaReceptor, "TipoJornada"))),
        new("TipoRegimen", f => Catalogo(Catalogos.TipoRegimen, f.C.A(f.C.NominaReceptor, "TipoRegimen"))),
        new("NumEmpleado", f => f.C.A(f.C.NominaReceptor, "NumEmpleado")),
        new("Departamento", f => f.C.A(f.C.NominaReceptor, "Departamento")),
        new("Puesto", f => f.C.A(f.C.NominaReceptor, "Puesto")),
        new("RiesgoPuesto", f => f.C.A(f.C.NominaReceptor, "RiesgoPuesto")),
        new("PeriodicidadPago", f => Catalogo(Catalogos.Periodicidad, f.C.A(f.C.NominaReceptor, "PeriodicidadPago"))),
        new("Banco", f => f.C.A(f.C.NominaReceptor, "Banco")),
        new("CuentaBancaria", f => f.C.A(f.C.NominaReceptor, "CuentaBancaria")),
        new("SalarioBaseCotApor", f => f.C.N(f.C.NominaReceptor, "SalarioBaseCotApor"), true),
        new("SalarioDiarioIntegrado", f => f.C.N(f.C.NominaReceptor, "SalarioDiarioIntegrado"), true),
        new("ClaveEntFed", f => f.C.A(f.C.NominaReceptor, "ClaveEntFed")),
        new("TotalSueldosPer", f => f.C.N(f.C.Hijo(f.C.Nomina, "Percepciones"), "TotalSueldos"), true),
        new("TotalSeparacionIndemnizacionPer", f => f.C.N(f.C.Hijo(f.C.Nomina, "Percepciones"), "TotalSeparacionIndemnizacion"), true),
        new("TotalJubilacionPensionRetiroPer", f => f.C.N(f.C.Hijo(f.C.Nomina, "Percepciones"), "TotalJubilacionPensionRetiro"), true),
        new("TotalGravadoPercepcion", f => f.C.N(f.C.Hijo(f.C.Nomina, "Percepciones"), "TotalGravado"), true),
        new("TotalExentoPercepcion", f => f.C.N(f.C.Hijo(f.C.Nomina, "Percepciones"), "TotalExento"), true),
        new("TotalOtrasDeducciones", f => f.C.N(f.C.Hijo(f.C.Nomina, "Deducciones"), "TotalOtrasDeducciones"), true),
        new("TotalImpuestosRetenidosDed", f => f.C.N(f.C.Hijo(f.C.Nomina, "Deducciones"), "TotalImpuestosRetenidos"), true),
    ];

    // Table13 / Table1 / Table2 son los nombres del reporte original; el dashboard los referencia.
    private static readonly Hoja[] Hojas =
    [
        // Orden de los comprobantes en toda la app: facturas, nómina, notas de crédito, traslados y pagos.
        new("Factura", "Table13", "I", "00B0F0", Comprobante),
        new("Nomina", "Table2", "N", "375623", Nomina),
        new("NCredito", "Table1", "E", "C00000", Comprobante),
        new("Traslado", "TableTraslado", "T", "7F7F7F", Comprobante),
        new("Pago", "TablePago", "P", "7030A0", Pago),
    ];

    private static void EscribirHoja(XLWorkbook wb, Hoja hoja, IReadOnlyList<Fila> filas)
    {
        var ws = wb.Worksheets.Add(hoja.Nombre);
        ws.SetTabColor(XLColor.FromHtml("#" + hoja.Tab));
        var columnas = hoja.Columnas;
        for (var c = 0; c < columnas.Length; c++) ws.Cell(1, c + 1).Value = columnas[c].Titulo;
        for (var r = 0; r < filas.Count; r++)
            for (var c = 0; c < columnas.Length; c++)
                ws.Cell(r + 2, c + 1).Value = columnas[c].Valor(filas[r]) switch
                {
                    null => Blank.Value,
                    decimal d => d,
                    string s => s,
                    var o => XLCellValue.FromObject(o, Inv)
                };
        // Una tabla de Excel necesita al menos una fila de datos; si no hay CFDI queda una fila vacía.
        var table = ws.Range(1, 1, Math.Max(2, filas.Count + 1), columnas.Length).CreateTable(hoja.Tabla);
        table.Theme = XLTableTheme.TableStyleMedium8;
        table.ShowTotalsRow = true;
        for (var c = 0; c < columnas.Length; c++)
        {
            if (!columnas[c].Dinero) continue;
            table.Field(c).TotalsRowFunction = XLTotalsRowFunction.Sum;
            ws.Column(c + 1).Style.NumberFormat.Format = Contable;
        }
        ws.Columns(1, columnas.Length).Width = 18.71;
        ws.SheetView.FreezeRows(1);
    }

    // ---------- Resumen Clientes ----------

    private sealed record Cliente(string Rfc, string Nombre, decimal Facturado, decimal Neto);
    private sealed record Resumen(int Ultima, int TopGrafico);
    private sealed record Dashboard(int TopGrafico);

    private static List<Cliente> Clientes(List<Leido> leidos)
    {
        var facturas = leidos.Where(x => x.Tipo == "I").ToList();
        var notas = leidos.Where(x => x.Tipo == "E").ToList();
        return facturas.Concat(notas).GroupBy(x => x.ReceptorRfc).Select(g =>
        {
            var facturado = g.Where(x => x.Tipo == "I").Sum(x => x.Total);
            var nc = g.Where(x => x.Tipo == "E").Sum(x => x.Total);
            var nombre = g.OrderBy(x => x.Tipo == "I" ? 0 : 1).Select(x => x.ReceptorNombre).FirstOrDefault(x => x.Length > 0) ?? g.Key;
            return new Cliente(g.Key, nombre, facturado, facturado - nc);
        }).OrderByDescending(x => x.Facturado).ThenByDescending(x => x.Neto).ThenBy(x => x.Nombre, StringComparer.Ordinal).ToList();
    }

    private static Resumen EscribirResumen(XLWorkbook wb, List<Cliente> clientes, string periodo)
    {
        var ws = wb.Worksheets.Add("Resumen Clientes", 1);
        ws.SetTabColor(XLColor.FromHtml("#0084BE"));
        ws.ShowGridLines = false;
        double[] anchos = [20.29, 48.86, 12.57, 20.29, 12.57, 20.29, 20.29, 13.57, 20.29, 16.43];
        for (var i = 0; i < anchos.Length; i++) ws.Column(i + 1).Width = anchos[i];

        var titulo = ws.Range("A2:J2").Merge();
        ws.Cell("A2").Value = $"RESUMEN DE VENTAS POR CLIENTE · CFDI EMITIDOS {periodo}";
        titulo.Style.Fill.SetBackgroundColor(Color("005A80")).Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.White)
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(2).Height = 26.1;

        string[] encabezados = ["RFC RECEPTOR", "CLIENTE", "FACTURAS", "FACTURADO", "NOTAS NC", "NC APLICADAS", "VENTA NETA", "% PART.", "TICKET PROM.", "ESTADO"];
        for (var i = 0; i < encabezados.Length; i++) ws.Cell(4, i + 1).Value = encabezados[i];
        var ultima = 4 + clientes.Count;
        for (var i = 0; i < clientes.Count; i++)
        {
            var r = 5 + i;
            ws.Cell(r, 1).Value = clientes[i].Rfc;
            ws.Cell(r, 2).Value = clientes[i].Nombre;
            ws.Cell(r, 3).FormulaA1 = $"COUNTIFS(Table13[RFC Receptor],$A{r})";
            ws.Cell(r, 4).FormulaA1 = $"SUMIFS(Table13[Total],Table13[RFC Receptor],$A{r})";
            // NCredito puede traer facturas mezcladas; solo cuentan los renglones Tipo = NotaCredito.
            ws.Cell(r, 5).FormulaA1 = $"COUNTIFS(Table1[RFC Receptor],$A{r},Table1[Tipo],\"NotaCredito\")";
            ws.Cell(r, 6).FormulaA1 = $"SUMIFS(Table1[Total],Table1[RFC Receptor],$A{r},Table1[Tipo],\"NotaCredito\")";
            ws.Cell(r, 7).FormulaA1 = $"D{r}-F{r}";
            ws.Cell(r, 8).FormulaA1 = $"IFERROR(D{r}/SUM(Table13[Total]),0)";
            ws.Cell(r, 9).FormulaA1 = $"IFERROR(D{r}/C{r},0)";
            ws.Cell(r, 10).FormulaA1 = $"IF(F{r}>0,\"CON NC\",IF(D{r}>0,\"SIN NC\",\"SOLO NC\"))";
            // Datos auxiliares del gráfico: venta neta sin público en general y su top 10.
            ws.Cell(r, 27).FormulaA1 = $"IF($A{r}=\"{PublicoGeneral}\",\"\",$G{r})";
        }

        var tabla = ws.Range(4, 1, ultima, 10).CreateTable("TblClientes");
        tabla.Theme = XLTableTheme.TableStyleMedium2;
        tabla.ShowRowStripes = true;
        tabla.ShowTotalsRow = true;
        tabla.Field("RFC RECEPTOR").TotalsRowLabel = "TOTAL";
        tabla.Field("CLIENTE").TotalsRowFormulaA1 = "SUBTOTAL(103,TblClientes[CLIENTE])&\" clientes\"";
        foreach (var campo in new[] { "FACTURAS", "FACTURADO", "NOTAS NC", "NC APLICADAS", "VENTA NETA", "% PART." })
            tabla.Field(campo).TotalsRowFunction = XLTotalsRowFunction.Sum;
        tabla.Field("TICKET PROM.").TotalsRowFormulaA1 = $"IFERROR(D{ultima + 1}/C{ultima + 1},0)";
        tabla.Field("ESTADO").TotalsRowFormulaA1 = "SUBTOTAL(103,TblClientes[ESTADO])&\" filas\"";

        foreach (var col in new[] { 4, 6, 7, 9 }) ws.Range(5, col, ultima + 1, col).Style.NumberFormat.Format = Moneda;
        ws.Range(5, 8, ultima + 1, 8).Style.NumberFormat.Format = "0.0%";
        foreach (var col in new[] { 3, 5, 8, 10 }) ws.Range(5, col, ultima + 1, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(ultima + 1, 1, ultima + 1, 10).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(4);

        ws.Range($"D5:D{ultima}").AddConditionalFormat().DataBar(Color("0084BE")).LowestValue().HighestValue();
        ws.Range($"F5:F{ultima}").AddConditionalFormat().DataBar(Color("C00000")).LowestValue().HighestValue();
        foreach (var col in new[] { "G", "H" })
            ws.Range($"{col}5:{col}{ultima}").AddConditionalFormat().ColorScale()
                .LowestValue(Color("F2F9FD")).Midpoint(XLCFContentType.Percentile, 50, Color("9BDCF8")).HighestValue(Color("0084BE"));
        foreach (var (texto, fondo, letra) in new[] { ("CON NC", "FDE2E2", "9C0006"), ("SIN NC", "E2F0D9", "375623"), ("SOLO NC", "FFF2CC", "7F6000") })
            ws.Range($"J5:J{ultima}").AddConditionalFormat().WhenContains(texto).Fill.SetBackgroundColor(Color(fondo)).Font.SetFontColor(Color(letra));

        var top = Math.Max(1, Math.Min(10, clientes.Count(x => x.Rfc != PublicoGeneral)));
        ws.Cell("AA1").Value = "Datos auxiliares del gráfico del Dashboard (no borrar)";
        ws.Cell("AA1").Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));
        ws.Cell("AA4").Value = "VENTA NETA SIN PÚBLICO GENERAL";
        ws.Cell("AC4").Value = "CLIENTE";
        ws.Cell("AD4").Value = "VENTA NETA";
        for (var i = 0; i < top; i++)
        {
            var r = 5 + i;
            ws.Cell(r, 30).FormulaA1 = $"IFERROR(LARGE($AA$5:$AA${ultima},ROW()-4),0)";
            ws.Cell(r, 29).FormulaA1 = $"IFERROR(INDEX($B$5:$B${ultima},MATCH(AD{r},$AA$5:$AA${ultima},0)),\"\")";
        }
        ws.Range($"AD5:AD{4 + top}").Style.NumberFormat.Format = Moneda;
        ws.Range($"AA5:AA{ultima}").Style.NumberFormat.Format = Moneda;
        ws.Column(27).Width = 32;
        ws.Column(29).Width = 48;
        ws.Column(30).Width = 18;
        return new Resumen(ultima, top);
    }

    // ---------- Dashboard ----------

    private static Dashboard EscribirDashboard(XLWorkbook wb, List<Cliente> clientes, string rfc, string periodo, Resumen resumen)
    {
        var ws = wb.Worksheets.Add("Dashboard", 1);
        ws.SetTabColor(XLColor.FromHtml("#0B2E4F"));
        ws.ShowGridLines = false;
        ws.Style.Font.FontName = "Calibri";
        var u = resumen.Ultima;
        string Rc(string col) => $"'Resumen Clientes'!${col}$5:${col}${u}";

        ws.Column(1).Width = 2;
        ws.Column(17).Width = 2;
        foreach (var c in new[] { 5, 9, 13 }) ws.Column(c).Width = 2.29;
        foreach (var inicio in Grupos) ws.Columns(inicio, inicio + 2).Width = 17.43;
        (int Fila, double Alto)[] altos = [(1, 16.5), (2, 40), (3, 20.1), (4, 5.1), (5, 10), (6, 18), (7, 30), (8, 16), (9, 7), (10, 12), (11, 22), (12, 18), (13, 24), (28, 12), (29, 22), (30, 18)];
        foreach (var (fila, alto) in altos) ws.Row(fila).Height = alto;
        for (var r = 14; r <= 27; r++) ws.Row(r).Height = r is >= 15 and <= 19 ? 19 : 16;
        for (var r = 31; r <= 45; r++) ws.Row(r).Height = 15;

        // Celda auxiliar con el RFC del cliente elegido en el dropdown.
        ws.Cell("R1").Value = "RFC auxiliar del cliente seleccionado (celda de apoyo del dropdown)";
        ws.Cell("R1").Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));
        ws.Cell("R2").FormulaA1 = $"IFERROR(INDEX({Rc("A")},MATCH($J$13,{Rc("B")},0)),\"\")";
        ws.Cell("R2").Style.Font.SetFontColor(Color("808080"));

        Banda(ws, "B2:P2", $"DASHBOARD DE VENTAS POR CLIENTE · CFDI EMITIDOS {periodo}", "003A55", 20, bold: true);
        Banda(ws, "B3:P3", null, "0084BE", 10, bold: false);
        ws.Cell("B3").FormulaA1 = $"\"Emisor {rfc} · \"&COUNTA(Table13[UUID])&\" facturas · \"&COUNTIFS(Table1[Tipo],\"NotaCredito\")&\" notas de crédito · \"&COUNTA(TblClientes[RFC RECEPTOR])&\" clientes · fuente: hojas Factura y NCredito\"";
        string[] franja = ["003A55", "C00000", "0084BE", "00B0F0"];
        for (var g = 0; g < 4; g++) ws.Range(4, Grupos[g], 4, Grupos[g] + 2).Style.Fill.BackgroundColor = Color(franja[g]);

        var cancelados = "IF(COUNTIF(Table13[Estado SAT],\"Cancelado\")>0,\" · \"&COUNTIF(Table13[Estado SAT],\"Cancelado\")&\" cancelados\",\"\")";
        var tarjetas = new (string Titulo, string Valor, string Subtitulo, string[] Tonos, bool Dinero)[]
        {
            ("TOTAL FACTURADO", "SUM(Table13[Total])", $"COUNTA(Table13[UUID])&\" CFDI de ingreso\"&{cancelados}", ["003A55", "00587F", "0084BE", "00B0F0"], true),
            ("NOTAS DE CRÉDITO APLICADAS", "SUMIFS(Table1[Total],Table1[Tipo],\"NotaCredito\")", "IFERROR(TEXT(F7/B7,\"0.0%\"),\"0.0%\")&\" del facturado · \"&COUNTIFS(Table1[Tipo],\"NotaCredito\")&\" NC\"", ["7F0000", "A80000", "C00000", "E06666"], true),
            ("VENTA NETA", "B7-F7", "\"Facturado menos notas de crédito\"", ["00587F", "0084BE", "00A0DC", "66CFF7"], true),
            ("CLIENTES CON VENTA", $"COUNTIF({Rc("D")},\">0\")", "\"Ticket promedio \"&TEXT(IFERROR(B7/COUNTA(Table13[UUID]),0),\"$#,##0.00\")", ["0084BE", "00A0DC", "00B0F0", "8FDCF9"], false),
        };
        for (var g = 0; g < 4; g++)
        {
            var t = tarjetas[g];
            for (var r = 6; r <= 9; r++)
            {
                var celdas = ws.Range(r, Grupos[g], r, Grupos[g] + 2).Merge();
                celdas.Style.Fill.SetBackgroundColor(Color(t.Tonos[r - 6])).Font.SetFontColor(XLColor.White)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            }
            var col = Grupos[g];
            ws.Cell(6, col).Value = t.Titulo;
            ws.Cell(6, col).Style.Font.SetBold().Font.SetFontSize(9);
            ws.Cell(7, col).FormulaA1 = t.Valor;
            ws.Cell(7, col).Style.Font.SetBold().Font.SetFontSize(20);
            if (t.Dinero) ws.Cell(7, col).Style.NumberFormat.Format = Moneda;
            ws.Cell(8, col).FormulaA1 = t.Subtitulo;
            ws.Cell(8, col).Style.Font.SetFontSize(9);
        }

        // Top 15 clientes por facturación.
        Banda(ws, "B11:H11", "TOP 15 CLIENTES POR FACTURACIÓN", "003A55", 11, bold: true);
        Banda(ws, "J11:P11", "ESTADO DE CUENTA POR CLIENTE", "00587F", 11, bold: true);
        Encabezado(ws, "B12:D12", "CLIENTE", indent: true);
        Encabezado(ws, "F12", "FACTURADO");
        Encabezado(ws, "G12", "NC APLICADAS");
        Encabezado(ws, "H12", "VENTA NETA");
        Encabezado(ws, "J12:L12", "SELECCIONA CLIENTE", indent: true);
        Encabezado(ws, "N12:P12", "RFC", indent: true);
        for (var r = 13; r <= 27; r++)
        {
            // autoFill no funciona sobre celdas combinadas: una fórmula por fila.
            ws.Range(r, 2, r, 4).Merge();
            ws.Cell(r, 2).FormulaA1 = $"IFERROR(INDEX({Rc("B")},ROW()-12),\"\")";
            ws.Cell(r, 2).Style.Font.SetFontSize(9).Alignment.SetIndent(1);
            (int Col, string Fuente)[] importes = [(6, "D"), (7, "F"), (8, "G")];
            foreach (var (c, fuente) in importes)
            {
                ws.Cell(r, c).FormulaA1 = $"IFERROR(INDEX({Rc(fuente)},ROW()-12),\"\")";
                ws.Cell(r, c).Style.Font.SetFontName("Segoe UI").Font.SetFontSize(9).NumberFormat.Format = Moneda;
            }
        }
        ws.Range("B13:D27").AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(Color("F2F9FD"));
        ws.Range("F13:H27").AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=0").Fill.SetBackgroundColor(Color("F2F9FD"));
        ws.Range("F13:F27").AddConditionalFormat().DataBar(Color("00A0DC")).LowestValue().HighestValue();
        ws.Range("G13:G27").AddConditionalFormat().DataBar(Color("EE8B3A")).LowestValue().HighestValue();
        ws.Range("H13:H27").AddConditionalFormat().ColorScale()
            .LowestValue(Color("F2F9FD")).Midpoint(XLCFContentType.Percentile, 50, Color("9BDCF8")).HighestValue(Color("0084BE"));

        // Estado de cuenta: dropdown con los nombres de Resumen Clientes.
        var selector = ws.Range("J13:L13").Merge();
        ws.Cell("J13").Value = clientes[0].Nombre;
        selector.Style.Fill.SetBackgroundColor(Color("FFF7D6")).Font.SetBold().Font.SetFontSize(11).Font.SetFontColor(Color("003A55"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        selector.CreateDataValidation().List(Rc("B"), true);
        var rfcCliente = ws.Range("N13:P13").Merge();
        ws.Cell("N13").FormulaA1 = "$R$2";
        rfcCliente.Style.Fill.SetBackgroundColor(Color("EDF6FB")).Font.SetFontSize(11).Font.SetFontColor(Color("003A55"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        var mini = new (string Titulo, string Tono, bool LetraOscura, string Formula, bool Dinero)[]
        {
            ("FACTURADO AL CLIENTE", "003A55", false, "IFERROR(SUMIFS(Table13[Total],Table13[RFC Receptor],$R$2),0)", true),
            ("NOTAS DE CRÉDITO", "A80000", false, "IFERROR(SUMIFS(Table1[Total],Table1[RFC Receptor],$R$2,Table1[Tipo],\"NotaCredito\"),0)", true),
            ("VENTA NETA", "0084BE", false, "N15-N16", true),
            ("FACTURAS / NC EMITIDAS", "00A0DC", false, "COUNTIFS(Table13[RFC Receptor],$R$2)&\" / \"&COUNTIFS(Table1[RFC Receptor],$R$2,Table1[Tipo],\"NotaCredito\")", false),
            ("TICKET PROMEDIO", "66CFF7", true, "IFERROR(N15/COUNTIFS(Table13[RFC Receptor],$R$2),0)", true),
        };
        for (var i = 0; i < mini.Length; i++)
        {
            var r = 15 + i;
            var etiqueta = ws.Range(r, 10, r, 12).Merge();
            ws.Cell(r, 10).Value = mini[i].Titulo;
            etiqueta.Style.Fill.SetBackgroundColor(Color(mini[i].Tono)).Font.SetBold().Font.SetFontSize(9)
                .Font.SetFontColor(mini[i].LetraOscura ? Color("003A55") : XLColor.White)
                .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            var valor = ws.Range(r, 14, r, 16).Merge();
            ws.Cell(r, 14).FormulaA1 = mini[i].Formula;
            valor.Style.Fill.SetBackgroundColor(Color("F5F9FC")).Font.SetBold().Font.SetFontSize(11).Font.SetFontColor(Color("003A55"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            if (mini[i].Dinero) valor.Style.NumberFormat.Format = Moneda;
        }

        // Detalle del cliente: una fórmula FILTER por columna; un FILTER multicolumna escribiría sobre las separadoras.
        Banda(ws, "B29:H29", "FACTURAS DEL CLIENTE SELECCIONADO (primeras 15)", "0084BE", 11, bold: true);
        Banda(ws, "J29:P29", "NOTAS DE CRÉDITO DEL CLIENTE SELECCIONADO (primeras 15)", "A80000", 11, bold: true);
        (int Desfase, string Titulo, string Campo, bool Dinero)[] detalle =
            [(0, "FECHA", "Fecha Emision", false), (1, "FOLIO", "Folio", false), (2, "UUID", "UUID", false), (4, "SUBTOTAL", "SubTotal", true), (5, "IVA 16%", "IVA 16%", true), (6, "TOTAL", "Total", true)];
        foreach (var (inicio, tabla, condicion) in new[]
                 {
                     (2, "Table13", "Table13[RFC Receptor]=$R$2"),
                     (10, "Table1", "(Table1[RFC Receptor]=$R$2)*(Table1[Tipo]=\"NotaCredito\")"),
                 })
        {
            foreach (var (desfase, titulo, campo, dinero) in detalle)
            {
                var col = inicio + desfase;
                Encabezado(ws, ws.Cell(30, col).Address.ToString()!, titulo, centrado: true);
                for (var r = 31; r <= 45; r++)
                {
                    var celda = ws.Cell(r, col);
                    celda.FormulaA1 = $"IFERROR(INDEX(_xlfn._xlws.FILTER({tabla}[{campo}],{condicion}),ROW()-30),\"\")";
                    celda.Style.Font.SetFontName("Segoe UI").Font.SetFontSize(8);
                    if (dinero) celda.Style.NumberFormat.Format = Moneda;
                    else celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                }
            }
        }
        foreach (var rango in new[] { "B31:D45", "F31:H45", "J31:L45", "N31:P45" })
            ws.Range(rango).AddConditionalFormat().WhenIsTrue("MOD(ROW(),2)=1").Fill.SetBackgroundColor(Color("F7F7F7"));

        var nota = ws.Range("B47:P47").Merge();
        ws.Cell("B47").Value = $"Generado por CFDI Manager el {DateTime.Now:dd/MM/yyyy HH:mm} · Las notas de crédito solo cuentan los renglones con Tipo = NotaCredito de la hoja NCredito.";
        nota.Style.Font.SetFontSize(8).Font.SetFontColor(Color("808080"));

        return new Dashboard(resumen.TopGrafico);
    }

    private static readonly int[] Grupos = [2, 6, 10, 14];

    private static void Banda(IXLWorksheet ws, string rango, string? texto, string fondo, double tamano, bool bold)
    {
        var r = ws.Range(rango).Merge();
        if (texto is not null) r.FirstCell().Value = texto;
        r.Style.Fill.SetBackgroundColor(Color(fondo)).Font.SetFontColor(XLColor.White).Font.SetFontSize(tamano).Font.SetBold(bold)
            .Alignment.SetIndent(1).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }

    private static void Encabezado(IXLWorksheet ws, string rango, string texto, bool indent = false, bool centrado = false)
    {
        var r = ws.Range(rango);
        if (r.Cells().Count() > 1) r.Merge();
        r.FirstCell().Value = texto;
        r.Style.Fill.SetBackgroundColor(Color("D9EEF9")).Font.SetBold().Font.SetFontSize(9).Font.SetFontColor(Color("003A55"))
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        if (indent) r.Style.Alignment.Indent = 1;
        if (centrado) r.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static XLColor Color(string hex) => XLColor.FromHtml("#" + hex);

    private static (string Largo, string Corto) Periodo(Filtro filtro, List<Leido> leidos)
    {
        if (filtro.Anio is int anio && filtro.Mes is int mes and >= 1 and <= 12)
            return ($"{Meses[mes - 1]} {anio}", $"{Meses[mes - 1][..3]} {anio}");
        if (filtro.Anio is int soloAnio) return ($"{soloAnio}", $"{soloAnio}");
        var fechas = leidos.Select(x => x.E.Fila.Fecha).OfType<DateTimeOffset>().ToList();
        if (fechas.Count == 0) return ("", DateTime.Now.ToString("yyyyMMdd", Inv));
        var (min, max) = (fechas.Min(), fechas.Max());
        string Mes(DateTimeOffset d) => $"{Meses[d.Month - 1][..3]} {d.Year}";
        return min.Year == max.Year && min.Month == max.Month
            ? ($"{Meses[min.Month - 1]} {min.Year}", Mes(min))
            : ($"{Mes(min)} – {Mes(max)}", $"{Mes(min)} - {Mes(max)}");
    }

    // ---------- Gráfico y segmentaciones (ClosedXML no los escribe) ----------

    private static void AgregarGraficoYSegmentaciones(MemoryStream stream, Dashboard dashboard)
    {
        using var doc = SpreadsheetDocument.Open(stream, true);
        var wbPart = doc.WorkbookPart!;
        WorksheetPart Hoja(string nombre) =>
            (WorksheetPart)wbPart.GetPartById(wbPart.Workbook.Sheets!.Elements<S.Sheet>().Single(x => x.Name == nombre).Id!);

        // Gráfico de barras de venta neta por cliente en el hueco del bloque derecho.
        var dash = Hoja("Dashboard");
        var dibujo = dash.AddNewPart<DrawingsPart>();
        var grafico = dibujo.AddNewPart<ChartPart>();
        Escribir(grafico, GraficoXml(4 + dashboard.TopGrafico));
        Escribir(dibujo, $"""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
            <xdr:twoCellAnchor editAs="oneCell"><xdr:from><xdr:col>9</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>20</xdr:row><xdr:rowOff>38100</xdr:rowOff></xdr:from><xdr:to><xdr:col>16</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>27</xdr:row><xdr:rowOff>139700</xdr:rowOff></xdr:to>
            <xdr:graphicFrame macro=""><xdr:nvGraphicFramePr><xdr:cNvPr id="2" name="g_top10"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm>
            <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart"><c:chart xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="{dibujo.GetIdOfPart(grafico)}"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>
            </xdr:wsDr>
            """);
        InsertarDibujo(dash, dash.GetIdOfPart(dibujo));

        // Segmentaciones de ESTADO y FACTURAS sobre TblClientes, a la derecha de la tabla (columnas L a S).
        var resumen = Hoja("Resumen Clientes");
        var tabla = resumen.TableDefinitionParts.Single(x => x.Table!.Name == "TblClientes").Table!;
        var cacheIds = new List<string>();
        foreach (var (nombre, columna) in new[] { ("ESTADO", 10), ("FACTURAS", 3) })
        {
            var cache = wbPart.AddNewPart<SlicerCachePart>();
            Escribir(cache, $"""
                <slicerCacheDefinition xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" mc:Ignorable="x" xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main" name="Slicer_{nombre}" sourceName="{nombre}"><extLst><x:ext uri="{"{2F2917AC-EB37-4324-AD4E-5DD8C200BD13}"}" xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"><x15:tableSlicerCache tableId="{tabla.Id!.Value}" column="{columna}"/></x:ext></extLst></slicerCacheDefinition>
                """);
            cacheIds.Add(wbPart.GetIdOfPart(cache));
        }
        var segmentaciones = resumen.AddNewPart<SlicersPart>();
        Escribir(segmentaciones, """
            <slicers xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" mc:Ignorable="x" xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><slicer name="slcEstado" cache="Slicer_ESTADO" caption="ESTADO" rowHeight="241300"/><slicer name="slcFacturas" cache="Slicer_FACTURAS" caption="FACTURAS" rowHeight="241300"/></slicers>
            """);
        var dibujoResumen = resumen.AddNewPart<DrawingsPart>();
        Escribir(dibujoResumen, $"""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
            {AnclaSegmentacion(2, "slcEstado", 11, 14, 3, 11)}
            {AnclaSegmentacion(3, "slcFacturas", 15, 18, 3, 19)}
            </xdr:wsDr>
            """);
        InsertarDibujo(resumen, resumen.GetIdOfPart(dibujoResumen));
        var hojaExt = resumen.Worksheet.GetFirstChild<S.WorksheetExtensionList>() ?? resumen.Worksheet.AppendChild(new S.WorksheetExtensionList());
        hojaExt.Append(new S.WorksheetExtension(
            $"""<x:ext uri="{"{3A4CF648-6AED-40f4-86FF-DC5316D8AED3}"}" xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"><x14:slicerList xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"><x14:slicer xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="{resumen.GetIdOfPart(segmentaciones)}"/></x14:slicerList></x:ext>"""));
        resumen.Worksheet.Save();

        var libro = wbPart.Workbook;
        var nombres = libro.DefinedNames ?? libro.InsertAfter(new S.DefinedNames(), libro.Sheets);
        nombres.Append(new S.DefinedName("#N/A") { Name = "Slicer_ESTADO" }, new S.DefinedName("#N/A") { Name = "Slicer_FACTURAS" });
        var libroExt = libro.GetFirstChild<S.WorkbookExtensionList>() ?? libro.AppendChild(new S.WorkbookExtensionList());
        var caches = string.Concat(cacheIds.Select(id => $"""<x14:slicerCache xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="{id}"/>"""));
        libroExt.Append(new S.WorkbookExtension(
            $"""<x:ext uri="{"{46BE6895-7355-4a93-B00E-2C351335B9C9}"}" xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"><x15:slicerCaches xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">{caches}</x15:slicerCaches></x:ext>"""));
        libro.Save();
    }

    private static string AnclaSegmentacion(int id, string nombre, int colDesde, int colHasta, int filaDesde, int filaHasta) => $"""
        <xdr:twoCellAnchor editAs="oneCell"><xdr:from><xdr:col>{colDesde}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{filaDesde}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>{colHasta}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{filaHasta}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
        <mc:AlternateContent xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"><mc:Choice xmlns:sle15="http://schemas.microsoft.com/office/drawing/2012/slicer" Requires="sle15"><xdr:graphicFrame macro=""><xdr:nvGraphicFramePr><xdr:cNvPr id="{id}" name="{nombre}"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm><a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/drawing/2010/slicer"><sle:slicer xmlns:sle="http://schemas.microsoft.com/office/drawing/2010/slicer" name="{nombre}"/></a:graphicData></a:graphic></xdr:graphicFrame></mc:Choice>
        <mc:Fallback><xdr:sp macro="" textlink=""><xdr:nvSpPr><xdr:cNvPr id="0" name=""/><xdr:cNvSpPr><a:spLocks noTextEdit="1"/></xdr:cNvSpPr></xdr:nvSpPr><xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1828800" cy="1828800"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr><xdr:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang="es-MX" sz="1100"/><a:t>Segmentación de datos: requiere Excel 2013 o posterior.</a:t></a:r></a:p></xdr:txBody></xdr:sp></mc:Fallback></mc:AlternateContent><xdr:clientData/></xdr:twoCellAnchor>
        """;

    private static string GraficoXml(int ultimaFila)
    {
        const string ejeTexto = """<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="800"><a:solidFill><a:srgbClr val="595959"/></a:solidFill></a:defRPr></a:pPr><a:endParaRPr lang="es-MX"/></a:p></c:txPr>""";
        const string linea = """<c:spPr><a:ln w="9525"><a:solidFill><a:srgbClr val="D9D9D9"/></a:solidFill></a:ln></c:spPr>""";
        return $"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
            <c:roundedCorners val="0"/>
            <c:chart>
            <c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="1000" b="1"><a:solidFill><a:srgbClr val="003A55"/></a:solidFill></a:defRPr></a:pPr><a:r><a:rPr lang="es-MX" sz="1000" b="1"><a:solidFill><a:srgbClr val="003A55"/></a:solidFill></a:rPr><a:t>Top 10 clientes por venta neta (sin público general)</a:t></a:r></a:p></c:rich></c:tx><c:overlay val="0"/></c:title>
            <c:autoTitleDeleted val="0"/>
            <c:plotArea><c:layout/>
            <c:barChart><c:barDir val="bar"/><c:grouping val="clustered"/><c:varyColors val="0"/>
            <c:ser><c:idx val="0"/><c:order val="0"/><c:tx><c:strRef><c:f>'Resumen Clientes'!$AD$4</c:f></c:strRef></c:tx>
            <c:spPr><a:solidFill><a:srgbClr val="00A0DC"/></a:solidFill></c:spPr><c:invertIfNegative val="0"/>
            <c:dLbls><c:numFmt formatCode="&quot;$&quot;#,##0" sourceLinked="0"/><c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr><c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="700"><a:solidFill><a:srgbClr val="003A55"/></a:solidFill></a:defRPr></a:pPr><a:endParaRPr lang="es-MX"/></a:p></c:txPr><c:dLblPos val="outEnd"/><c:showLegendKey val="0"/><c:showVal val="1"/><c:showCatName val="0"/><c:showSerName val="0"/><c:showPercent val="0"/><c:showBubbleSize val="0"/></c:dLbls>
            <c:cat><c:strRef><c:f>'Resumen Clientes'!$AC$5:$AC${ultimaFila}</c:f></c:strRef></c:cat>
            <c:val><c:numRef><c:f>'Resumen Clientes'!$AD$5:$AD${ultimaFila}</c:f></c:numRef></c:val>
            </c:ser>
            <c:gapWidth val="60"/><c:axId val="1461061312"/><c:axId val="929409583"/></c:barChart>
            <c:catAx><c:axId val="1461061312"/><c:scaling><c:orientation val="maxMin"/></c:scaling><c:delete val="0"/><c:axPos val="l"/><c:numFmt formatCode="General" sourceLinked="1"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/>{linea}{ejeTexto}<c:crossAx val="929409583"/><c:crosses val="autoZero"/><c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/><c:tickLblSkip val="1"/><c:noMultiLvlLbl val="0"/></c:catAx>
            <c:valAx><c:axId val="929409583"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="1"/><c:axPos val="t"/><c:majorGridlines>{linea}</c:majorGridlines><c:numFmt formatCode="&quot;$&quot;#,##0" sourceLinked="0"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/><c:crossAx val="1461061312"/><c:crosses val="autoZero"/><c:crossBetween val="between"/></c:valAx>
            <c:spPr><a:noFill/></c:spPr>
            </c:plotArea>
            <c:plotVisOnly val="1"/><c:dispBlanksAs val="gap"/>
            </c:chart>
            <c:spPr><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill><a:ln w="9525"><a:solidFill><a:srgbClr val="D9D9D9"/></a:solidFill></a:ln></c:spPr>
            </c:chartSpace>
            """;
    }

    internal static void Escribir(OpenXmlPart part, string xml)
    {
        using var s = new MemoryStream(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + xml.Trim()));
        part.FeedData(s);
    }

    // El elemento <drawing> va antes de legacyDrawing, tableParts y extLst según el esquema de la hoja.
    internal static void InsertarDibujo(WorksheetPart hoja, string relId)
    {
        var ws = hoja.Worksheet;
        var dibujo = new S.Drawing { Id = relId };
        var siguiente = ws.ChildElements.FirstOrDefault(x => x is S.LegacyDrawing or S.LegacyDrawingHeaderFooter or S.Picture or S.OleObjects or S.Controls or S.WebPublishItems or S.TableParts or S.WorksheetExtensionList);
        if (siguiente is null) ws.Append(dibujo);
        else ws.InsertBefore(dibujo, siguiente);
        ws.Save();
    }

    // ---------- Lectura del XML ----------

    private sealed class Leido
    {
        public Entrada E { get; }
        public XElement Root { get; }
        public bool V32 { get; }
        public XElement? Emisor { get; }
        public XElement? Receptor { get; }
        public XElement? Timbre { get; }
        public XElement? Nomina { get; }
        public XElement? NominaReceptor { get; }
        public List<XElement> Pagos { get; }
        public string Tipo { get; }
        public string ReceptorRfc { get; }
        public string ReceptorNombre { get; }
        public string FechaCorta { get; }
        public decimal Total { get; }
        public List<string> Relacionados { get; }
        public List<string> Complementos { get; }
        public string Conceptos { get; }
        public Dictionary<string, decimal> Iva { get; } = [];
        public Dictionary<string, decimal> Ieps { get; } = [];
        public decimal IepsTotal { get; }
        public decimal IvaRetenido { get; }
        public decimal IsrRetenido { get; }
        public decimal IvaRet6 { get; }
        public decimal TotalTrasladados { get; }
        public decimal TotalRetenidos { get; }
        public decimal Ish { get; }
        public decimal LocalTrasladado { get; }
        public decimal LocalRetenido { get; }

        public Leido(Entrada e)
        {
            E = e;
            Root = e.Xml.Root!;
            V32 = decimal.TryParse(A(Root, "Version"), NumberStyles.Number, Inv, out var version) && version < 3.3m;
            Emisor = Hijo(Root, "Emisor");
            Receptor = Hijo(Root, "Receptor");
            var complementos = Root.Elements().Where(x => x.Name.LocalName == "Complemento").SelectMany(x => x.Elements()).ToList();
            Timbre = complementos.FirstOrDefault(x => x.Name.LocalName == "TimbreFiscalDigital");
            Nomina = complementos.FirstOrDefault(x => x.Name.LocalName == "Nomina");
            NominaReceptor = Hijo(Nomina, "Receptor");
            Pagos = complementos.Where(x => x.Name.LocalName == "Pagos").SelectMany(x => x.Elements().Where(p => p.Name.LocalName == "Pago")).ToList();
            Complementos = complementos.Select(x => x.Name.LocalName).Where(x => x != "TimbreFiscalDigital").Distinct().ToList();
            var tipo = A(Root, "TipoDeComprobante");
            if (V32) tipo = tipo.ToLowerInvariant() switch { "ingreso" => "I", "egreso" => "E", "traslado" => "T", _ => tipo };
            Tipo = Nomina is not null ? "N" : Pagos.Count > 0 ? "P" : tipo.ToUpperInvariant();
            ReceptorRfc = A(Receptor, "Rfc").ToUpperInvariant();
            ReceptorNombre = A(Receptor, "Nombre");
            FechaCorta = DateTime.TryParse(A(Root, "Fecha"), Inv, DateTimeStyles.None, out var fecha) ? fecha.ToString("dd/MM/yyyy", Inv) : A(Root, "Fecha");
            Total = N(Root, "Total");
            Relacionados = Root.Elements().Where(x => x.Name.LocalName == "CfdiRelacionados")
                .SelectMany(x => x.Elements().Where(r => r.Name.LocalName == "CfdiRelacionado")).Select(x => A(x, "UUID").ToUpperInvariant()).ToList();
            var conceptos = Hijo(Root, "Conceptos")?.Elements().Where(x => x.Name.LocalName == "Concepto").ToList() ?? [];
            var texto = string.Join(" * ", conceptos.Select(x => A(x, "Descripcion")).Where(x => x.Length > 0));
            Conceptos = texto.Length > 2000 ? texto[..2000] : texto;

            var impuestos = Hijo(Root, "Impuestos");
            foreach (var t in Hijo(impuestos, "Traslados")?.Elements() ?? [])
            {
                var importe = N(t, "Importe");
                var tasa = Tasa(t);
                switch (Impuesto(t))
                {
                    case "002": Iva[tasa] = Iva.GetValueOrDefault(tasa) + importe; break;
                    case "003": Ieps[tasa] = Ieps.GetValueOrDefault(tasa) + importe; IepsTotal += importe; break;
                }
            }
            foreach (var r in Hijo(impuestos, "Retenciones")?.Elements() ?? [])
            {
                if (Impuesto(r) == "002") IvaRetenido += N(r, "Importe");
                else if (Impuesto(r) == "001") IsrRetenido += N(r, "Importe");
            }
            // La retención de IVA al 6% solo se distingue a nivel concepto.
            IvaRet6 = conceptos.SelectMany(c => Hijo(Hijo(c, "Impuestos"), "Retenciones")?.Elements() ?? [])
                .Where(r => Impuesto(r) == "002" && Tasa(r) == "0.06").Sum(r => N(r, "Importe"));
            TotalTrasladados = N(impuestos, "TotalImpuestosTrasladados") is var tt and not 0 ? tt : Iva.Values.Sum() + IepsTotal;
            TotalRetenidos = N(impuestos, "TotalImpuestosRetenidos") is var tr and not 0 ? tr : IvaRetenido + IsrRetenido;
            var locales = complementos.FirstOrDefault(x => x.Name.LocalName == "ImpuestosLocales");
            LocalTrasladado = N(locales, "TotaldeTraslados");
            LocalRetenido = N(locales, "TotaldeRetenciones");
            Ish = locales?.Elements().Where(x => x.Name.LocalName == "TrasladosLocales" && System.Text.RegularExpressions.Regex.IsMatch(A(x, "ImpLocTrasladado"), "ISH|HOSPEDAJE", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).Sum(x => N(x, "Importe")) ?? 0;
        }

        public string A(XElement? node, string name)
        {
            if (node is null) return "";
            var attr = node.Attributes().FirstOrDefault(x => x.Name.LocalName == name)
                       ?? node.Attributes().FirstOrDefault(x => x.Name.LocalName == char.ToLowerInvariant(name[0]) + name[1..]);
            return attr?.Value.Trim() ?? "";
        }

        public decimal N(XElement? node, string name) =>
            decimal.TryParse(A(node, name), NumberStyles.Number, Inv, out var n) ? n : 0m;

        public XElement? Hijo(XElement? node, string name) => node?.Elements().FirstOrDefault(x => x.Name.LocalName == name);

        public (string Direccion, string Localidad) Domicilio(XElement? node)
        {
            var d = Hijo(node, "DomicilioFiscal") ?? Hijo(node, "Domicilio") ?? Hijo(node, "ExpedidoEn");
            if (d is null) return ("", "");
            string Unir(string sep, params string[] campos) => string.Join(sep, campos.Select(x => A(d, x)).Where(x => x.Length > 0));
            return (Unir(" ", "calle", "noExterior", "noInterior", "colonia", "codigoPostal"), Unir(", ", "localidad", "municipio", "estado"));
        }

        private string Impuesto(XElement node) => A(node, "Impuesto").ToUpperInvariant() switch { "ISR" => "001", "IVA" => "002", "IEPS" => "003", var v => v };

        // Tasa como texto estable ("0.16", "0.265") para agrupar sin errores de redondeo.
        private string Tasa(XElement node)
        {
            var tasa = V32 ? N(node, "Tasa") / 100 : N(node, "TasaOCuota");
            return tasa.ToString("0.######", Inv);
        }
    }

    private static string Catalogo(IReadOnlyDictionary<string, string> catalogo, string clave) =>
        clave.Length == 0 ? "" : catalogo.TryGetValue(clave, out var descripcion) ? $"{clave} - {descripcion}" : clave;

    private static class Catalogos
    {
        public static readonly Dictionary<string, string> Forma = new()
        {
            ["01"] = "Efectivo", ["02"] = "Cheque nominativo", ["03"] = "Transferencia electrónica de fondos", ["04"] = "Tarjeta de crédito",
            ["05"] = "Monedero electrónico", ["06"] = "Dinero electrónico", ["08"] = "Vales de despensa", ["12"] = "Dación en pago",
            ["13"] = "Pago por subrogación", ["14"] = "Pago por consignación", ["15"] = "Condonación", ["17"] = "Compensación",
            ["23"] = "Novación", ["24"] = "Confusión", ["25"] = "Remisión de deuda", ["26"] = "Prescripción o caducidad",
            ["27"] = "A satisfacción del acreedor", ["28"] = "Tarjeta de débito", ["29"] = "Tarjeta de servicios",
            ["30"] = "Aplicación de anticipos", ["31"] = "Intermediario pagos", ["99"] = "Por definir",
        };

        public static readonly Dictionary<string, string> Metodo = new()
        {
            ["PUE"] = "Pago en una sola exhibición", ["PPD"] = "Pago en parcialidades o diferido",
        };

        public static readonly Dictionary<string, string> Uso = new()
        {
            ["G01"] = "Adquisición de mercancías", ["G02"] = "Devoluciones, descuentos o bonificaciones", ["G03"] = "Gastos en general",
            ["I01"] = "Construcciones", ["I02"] = "Mobiliario y equipo de oficina por inversiones", ["I03"] = "Equipo de transporte",
            ["I04"] = "Equipo de computo y accesorios", ["I05"] = "Dados, troqueles, moldes, matrices y herramental",
            ["I06"] = "Comunicaciones telefónicas", ["I07"] = "Comunicaciones satelitales", ["I08"] = "Otra maquinaria y equipo",
            ["D01"] = "Honorarios médicos, dentales y gastos hospitalarios", ["D02"] = "Gastos médicos por incapacidad o discapacidad",
            ["D03"] = "Gastos funerales", ["D04"] = "Donativos", ["D05"] = "Intereses reales efectivamente pagados por créditos hipotecarios (casa habitación)",
            ["D06"] = "Aportaciones voluntarias al SAR", ["D07"] = "Primas por seguros de gastos médicos",
            ["D08"] = "Gastos de transportación escolar obligatoria", ["D09"] = "Depósitos en cuentas para el ahorro, primas que tengan como base planes de pensiones",
            ["D10"] = "Pagos por servicios educativos (colegiaturas)", ["S01"] = "Sin efectos fiscales", ["CP01"] = "Pagos", ["CN01"] = "Nómina",
            ["P01"] = "Por definir",
        };

        public static readonly Dictionary<string, string> Regimen = new()
        {
            ["601"] = "General de Ley Personas Morales", ["603"] = "Personas Morales con Fines no Lucrativos",
            ["605"] = "Sueldos y Salarios e Ingresos Asimilados a Salarios", ["606"] = "Arrendamiento",
            ["607"] = "Régimen de Enajenación o Adquisición de Bienes", ["608"] = "Demás ingresos",
            ["610"] = "Residentes en el Extranjero sin Establecimiento Permanente en México", ["611"] = "Ingresos por Dividendos (socios y accionistas)",
            ["612"] = "Personas Físicas con Actividades Empresariales y Profesionales", ["614"] = "Ingresos por intereses",
            ["615"] = "Régimen de los ingresos por obtención de premios", ["616"] = "Sin obligaciones fiscales",
            ["620"] = "Sociedades Cooperativas de Producción que optan por diferir sus ingresos", ["621"] = "Incorporación Fiscal",
            ["622"] = "Actividades Agrícolas, Ganaderas, Silvícolas y Pesqueras", ["623"] = "Opcional para Grupos de Sociedades",
            ["624"] = "Coordinados", ["625"] = "Régimen de las Actividades Empresariales con ingresos a través de Plataformas Tecnológicas",
            ["626"] = "Régimen Simplificado de Confianza",
        };

        public static readonly Dictionary<string, string> TipoNomina = new() { ["O"] = "Ordinaria", ["E"] = "Extraordinaria" };

        public static readonly Dictionary<string, string> TipoContrato = new()
        {
            ["01"] = "Contrato de trabajo por tiempo indeterminado", ["02"] = "Contrato de trabajo para obra determinada",
            ["03"] = "Contrato de trabajo por tiempo determinado", ["04"] = "Contrato de trabajo por temporada",
            ["05"] = "Contrato de trabajo sujeto a prueba", ["06"] = "Contrato de trabajo con capacitación inicial",
            ["07"] = "Modalidad de contratación por pago de hora laborada", ["08"] = "Modalidad de trabajo por comisión laboral",
            ["09"] = "Modalidades de contratación donde no existe relación de trabajo", ["10"] = "Jubilación, pensión, retiro.",
            ["99"] = "Otro contrato",
        };

        public static readonly Dictionary<string, string> TipoJornada = new()
        {
            ["01"] = "Diurna", ["02"] = "Nocturna", ["03"] = "Mixta", ["04"] = "Por hora", ["05"] = "Reducida",
            ["06"] = "Continuada", ["07"] = "Partida", ["08"] = "Por turnos", ["99"] = "Otra Jornada",
        };

        public static readonly Dictionary<string, string> TipoRegimen = new()
        {
            ["02"] = "Sueldos", ["03"] = "Jubilados", ["04"] = "Pensionados", ["05"] = "Asimilados Miembros Sociedades Cooperativas Produccion",
            ["06"] = "Asimilados Integrantes Sociedades Asociaciones Civiles", ["07"] = "Asimilados Miembros consejos",
            ["08"] = "Asimilados comisionistas", ["09"] = "Asimilados Honorarios", ["10"] = "Asimilados acciones",
            ["11"] = "Asimilados otros", ["12"] = "Jubilados o Pensionados", ["13"] = "Indemnización o Separación", ["99"] = "Otro Regimen",
        };

        public static readonly Dictionary<string, string> Periodicidad = new()
        {
            ["01"] = "Diario", ["02"] = "Semanal", ["03"] = "Catorcenal", ["04"] = "Quincenal", ["05"] = "Mensual",
            ["06"] = "Bimestral", ["07"] = "Unidad obra", ["08"] = "Comisión", ["09"] = "Precio alzado", ["10"] = "Decenal",
            ["99"] = "Otra Periodicidad",
        };
    }
}
