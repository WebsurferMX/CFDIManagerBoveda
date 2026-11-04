using ClosedXML.Excel;
using CFDIParser.Models;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Reportes agrupados con dashboard, leyendo solo de su propia hoja convertida en tabla de Excel:
/// "Agrupado por receptor" de emitidos (Resumen Receptores y dashboard compacto de ventas) y "Agrupado por emisor" de
/// recibidos (dashboard compacto de cuentas por pagar). Cada contraparte es un grupo del esquema de Excel con su
/// renglón "Total …"; notas de crédito y pagos van en negativo.
/// </summary>
public static partial class ReporteGeneral
{
    private const string TablaPorReceptor = "TblPorReceptor";
    private const string TablaPorEmisor = "TblPorEmisor";

    private static readonly Fuente PorReceptor = new(TablaPorReceptor, TablaPorReceptor, "Factura", NotasNegativas: true, FechaComoFecha: true,
        "Resumen Receptores", "TblReceptores", "RECEPTOR", "receptores", "IVA", "hoja Por Receptor");

    public static (byte[] Bytes, string Nombre) CrearPorReceptor(IReadOnlyList<AdminXmlFilaDto> filas, Filtro filtro)
    {
        List<AdminXmlFilaDto> usados = filas.Where(x => x.TipoCfdi is "I" or "E").ToList();
        string Rfc(AdminXmlFilaDto x) => x.RfcReceptor.Trim().ToUpperInvariant();

        var clientes = usados.GroupBy(Rfc).Select(g =>
        {
            var facturado = g.Where(x => x.TipoCfdi == "I").Sum(x => x.Total);
            var nc = g.Where(x => x.TipoCfdi == "E").Sum(x => x.Total);
            var nombre = g.OrderBy(x => x.TipoCfdi == "I" ? 0 : 1).Select(x => x.NombreReceptor).FirstOrDefault(x => x.Length > 0) ?? g.Key;
            return new Cliente(g.Key, nombre, facturado, facturado - nc);
        }).OrderByDescending(x => x.Facturado).ThenByDescending(x => x.Neto).ThenBy(x => x.Nombre, StringComparer.Ordinal).ToList();

        var rfc = filtro.Rfc?.Trim().ToUpperInvariant() ?? "Todos";
        var (periodo, periodoCorto) = Periodo(filtro, usados.Select(x => new DateTimeOffset(x.FechaEmision)));

        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Segoe UI";
        EscribirTablaAgrupada(wb, "Por Receptor", TablaPorReceptor, "Receptor", usados, emisor: false,
            clientes.Select(c => (c.Rfc, c.Nombre)).ToList());

        Dashboard? dashboard = null;
        if (clientes.Count > 0)
        {
            var resumen = EscribirResumen(wb, clientes, periodo, PorReceptor);
            dashboard = EscribirDashboard(wb, clientes, rfc, periodo, resumen, PorReceptor, compacto: true);
        }

        wb.Worksheets.First().SetTabActive().Select();
        wb.FullCalculationOnLoad = true;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        if (dashboard is not null) AgregarGraficoYSegmentaciones(stream, dashboard);
        return (stream.ToArray(), $"{rfc} Emitidas {periodoCorto} Por Receptor.xlsx");
    }

    public static (byte[] Bytes, string Nombre) CrearPorEmisor(IReadOnlyList<AdminXmlFilaDto> filas, Filtro filtro)
    {
        List<AdminXmlFilaDto> usados = filas.Where(x => x.TipoCfdi is "I" or "E" or "P").ToList();
        string Rfc(AdminXmlFilaDto x) => x.RfcEmisor.Trim().ToUpperInvariant();

        var proveedores = OrdenarProveedores(usados.GroupBy(Rfc).Select(g => new Proveedor(
            g.Key,
            g.OrderBy(x => x.TipoCfdi == "I" ? 0 : 1).Select(x => x.NombreEmisor).FirstOrDefault(x => x.Length > 0) ?? g.Key,
            g.Where(x => x.TipoCfdi == "I").Sum(x => x.Total),
            g.Count(x => x.TipoCfdi == "I"), g.Count(x => x.TipoCfdi == "E"), g.Count(x => x.TipoCfdi == "P"))));

        var rfc = filtro.Rfc?.Trim().ToUpperInvariant() ?? "Todos";
        var (periodo, periodoCorto) = Periodo(filtro, usados.Select(x => new DateTimeOffset(x.FechaEmision)));

        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Segoe UI";
        EscribirTablaAgrupada(wb, "Por Emisor", TablaPorEmisor, "Emisor", usados, emisor: true,
            proveedores.Select(p => (p.Rfc, p.Nombre)).ToList());

        if (proveedores.Count > 0)
            EscribirDashboardCxp(wb, proveedores, rfc, periodo, FuenteCxp.PorEmisor);

        wb.Worksheets.First().SetTabActive().Select();
        wb.FullCalculationOnLoad = true;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return (stream.ToArray(), $"{rfc} Recibidas {periodoCorto} Por Emisor.xlsx");
    }

    /// <summary>
    /// Hoja con los comprobantes como tabla de Excel, un grupo del esquema por contraparte (en el orden de
    /// <paramref name="contrapartes"/>) y su renglón "Total …" dentro de la tabla. Ese renglón no lleva Tipo ni RFC, así
    /// que las fórmulas del resumen y del dashboard (que filtran por Tipo) no lo suman, y el total de la tabla usa
    /// SUBTOTAL, que ignora los SUBTOTAL de cada grupo.
    /// </summary>
    private static void EscribirTablaAgrupada(
        XLWorkbook wb, string hoja, string nombreTabla, string uno, List<AdminXmlFilaDto> filas, bool emisor,
        List<(string Rfc, string Nombre)> contrapartes) =>
        EscribirTablaAgrupada(wb, hoja, nombreTabla, uno, filas, emisor,
            contrapartes.Select(c => (c.Rfc, $"{c.Rfc} {c.Nombre}")).ToList(), clave: null);

    /// <summary>
    /// <paramref name="clave"/>: con qué se agrupa (y qué va en la primera columna); null agrupa por el RFC de la
    /// contraparte. <paramref name="grupos"/>: las claves en orden con su etiqueta para el renglón "Total …".
    /// </summary>
    private static void EscribirTablaAgrupada(
        XLWorkbook wb, string hoja, string nombreTabla, string uno, List<AdminXmlFilaDto> filas, bool emisor,
        List<(string Clave, string Etiqueta)> grupos, Func<AdminXmlFilaDto, string>? clave)
    {
        var ws = wb.Worksheets.Add(hoja);
        ws.SetTabColor(XLColor.FromHtml(emisor ? "#1E3A8A" : "#00B0F0"));

        // La contraparte (emisor en recibidos, receptor en emitidos) va primero, después de la columna del grupo.
        string lado = emisor ? "Emisor" : "Receptor";
        string otro = emisor ? "Receptor" : "Emisor";
        string[] columnas =
        [
            uno, $"RFC {lado}", $"Nombre {lado}", "Serie", "Folio", "UUID", "Estado SAT", "Tipo", "Fecha Emision",
            $"RFC {otro}", $"Nombre {otro}", "UsoCFDI", "Metodo de Pago", "Moneda", "SubTotal", "IVA", "Total"
        ];
        for (var c = 0; c < columnas.Length; c++) ws.Cell(1, c + 1).Value = columnas[c];

        static decimal Signo(AdminXmlFilaDto x) => x.TipoCfdi is "E" or "P" ? -1 : 1;
        static string Tipo(AdminXmlFilaDto x) => x.TipoCfdi switch { "I" => "Factura", "E" => "NotaCredito", "P" => "Pago", "T" => "Traslado", "N" => "Nomina", var t => t };
        string Rfc(AdminXmlFilaDto x) => (emisor ? x.RfcEmisor : x.RfcReceptor).Trim().ToUpperInvariant();
        string Nombre(AdminXmlFilaDto x) => emisor ? x.NombreEmisor : x.NombreReceptor;

        int[] importes = new[] { "SubTotal", "IVA", "Total" }.Select(c => Array.IndexOf(columnas, c) + 1).ToArray();
        int colUuid = Array.IndexOf(columnas, "UUID") + 1;
        Func<AdminXmlFilaDto, string> deGrupo = clave ?? Rfc;
        var porClave = filas.GroupBy(deGrupo).ToDictionary(g => g.Key);
        var r = 2;
        foreach (var (claveGrupo, etiqueta) in grupos)
        {
            if (!porClave.TryGetValue(claveGrupo, out var grupo)) continue;
            var inicio = r;
            foreach (var x in grupo.OrderBy(x => x.FechaEmision))
            {
                // El pago no trae importe en Total: lo pagado es MontoTotalPagos.
                var total = x.TipoCfdi == "P" ? x.MontoTotalPagos ?? 0 : x.Total;
                object[] valores =
                [
                    clave is null ? $"{Rfc(x)} {Nombre(x)}" : clave(x), Rfc(x), Nombre(x), x.Serie, x.Folio, x.Uuid, x.EstadoSat, Tipo(x), x.FechaEmision,
                    emisor ? x.RfcReceptor : x.RfcEmisor, emisor ? x.NombreReceptor : x.NombreEmisor, x.UsoCfdi, x.MetodoPago, x.Moneda,
                    Signo(x) * x.Subtotal, Signo(x) * x.Iva, Signo(x) * total
                ];
                for (var c = 0; c < valores.Length; c++)
                    ws.Cell(r, c + 1).Value = valores[c] switch { decimal d => d, DateTime f => f, var o => XLCellValue.FromObject(o, Inv) };
                r++;
            }

            ws.Rows(inicio, r - 1).Group();
            ws.Cell(r, 1).Value = $"Total {etiqueta}";
            ws.Cell(r, 4).FormulaA1 = $"SUBTOTAL(3,{ws.Cell(inicio, colUuid).Address}:{ws.Cell(r - 1, colUuid).Address})";
            foreach (var c in importes)
                ws.Cell(r, c).FormulaA1 = $"SUBTOTAL(9,{ws.Cell(inicio, c).Address}:{ws.Cell(r - 1, c).Address})";
            ws.Range(r, 1, r, columnas.Length).Style.Font.SetBold().Fill.SetBackgroundColor(Color(emisor ? "E0E7FF" : "DDEBF7"));
            r++;
        }
        if (r > 2) ws.Rows(2, r - 1).Group();

        // Una tabla de Excel necesita al menos una fila de datos.
        var tabla = ws.Range(1, 1, Math.Max(2, r - 1), columnas.Length).CreateTable(nombreTabla);
        tabla.Theme = XLTableTheme.TableStyleMedium2;
        tabla.ShowTotalsRow = true;
        tabla.Field(uno).TotalsRowLabel = "Total general";
        tabla.Field("UUID").TotalsRowFunction = XLTotalsRowFunction.Count;
        foreach (var campo in new[] { "SubTotal", "IVA", "Total" })
        {
            tabla.Field(campo).TotalsRowFunction = XLTotalsRowFunction.Sum;
            ws.Column(Array.IndexOf(columnas, campo) + 1).Style.NumberFormat.Format = Contable;
        }
        ws.Column(Array.IndexOf(columnas, "Fecha Emision") + 1).Style.NumberFormat.Format = "dd/mm/yyyy hh:mm";
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, columnas.Length).AdjustToContents(1, Math.Min(r, 300), 8, 50);
    }
}
