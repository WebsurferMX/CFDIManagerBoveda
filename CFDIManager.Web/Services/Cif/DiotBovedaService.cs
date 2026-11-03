using System.Globalization;
using System.Xml.Linq;
using ClosedXML.Excel;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Cif;

/// <summary>Comprobante con lo que aporta a la DIOT del mes, en pesos (notas de crédito en negativo).</summary>
public sealed record DiotPartida(
    string Rfc, string Nombre, string Tipo, string Uuid, string SerieFolio, DateTime Fecha, string Criterio, decimal Base, decimal Iva);

public sealed record DiotProveedor(string Rfc, string Nombre, decimal Base, decimal Iva, List<DiotPartida> Partidas);

/// <summary>DIOT de un mes reconstruida con los XML recibidos de la bóveda.</summary>
public sealed class DiotBoveda
{
    public int Ejercicio { get; init; }
    public int Mes { get; init; }

    /// <summary>La bóveda tiene CFDI recibidos emitidos en el mes; sin ellos no hay contra qué comparar.</summary>
    public bool HayXml { get; init; }

    public List<DiotProveedor> Proveedores { get; init; } = [];

    /// <summary>Complementos de pago sin impuestos por pago (Pagos 1.0): su IVA no se pudo tomar.</summary>
    public int PagosSinImpuestos { get; init; }

    public decimal Base => Proveedores.Sum(p => p.Base);
    public decimal Iva => Proveedores.Sum(p => p.Iva);

    /// <summary>Proveedores con operaciones en el mes (lo que la DIOT llama "operaciones que relaciona").</summary>
    public int Operaciones => Proveedores.Count(p => p.Base != 0 || p.Iva != 0);
}

/// <summary>
/// IVA pagado a proveedores del mes según la bóveda, con el criterio de flujo de efectivo de la DIOT: facturas PUE y
/// notas de crédito por su fecha de emisión, y facturas PPD por la fecha de pago de su complemento (ImpuestosP de
/// Pagos 2.0). Solo IVA al 16 %; sin cancelados.
/// </summary>
public class DiotBovedaService(PostgreSqlRepository repo)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // El complemento puede emitirse meses después del pago.
    private const int MesesDespuesParaPagos = 3;

    public DiotBoveda Calcular(long empresaId, int ejercicio, int mes)
    {
        var desde = new DateTime(ejercicio, mes, 1);
        var hasta = desde.AddMonths(1);
        var partidas = new List<DiotPartida>();
        int sinImpuestos = 0;

        foreach (DiotComprobanteDto c in repo.ObtenerComprobantesDiot(empresaId, desde, hasta, hasta.AddMonths(MesesDespuesParaPagos)))
        {
            string serieFolio = c.Serie == "" ? c.Folio : $"{c.Serie}-{c.Folio}";

            if (c.Tipo == "P")
            {
                var pagos = PagosDelMes(c.RutaXml, desde, hasta, out bool conImpuestos);
                if (!conImpuestos) sinImpuestos++;
                partidas.AddRange(pagos.Select(p => new DiotPartida(c.RfcEmisor, c.NombreEmisor, "Pago", c.Uuid, serieFolio, p.Fecha,
                    "PPD · fecha de pago", p.Base, p.Iva)));
                continue;
            }

            if (c.Base16 == 0 && c.Iva16 == 0)
                continue;

            decimal tc = c.Moneda is "" or "MXN" or "XXX" ? 1 : c.TipoCambio is > 0 ? c.TipoCambio.Value : 1;
            int signo = c.Tipo == "E" ? -1 : 1;
            partidas.Add(new DiotPartida(c.RfcEmisor, c.NombreEmisor, c.Tipo == "E" ? "NotaCredito" : "Factura", c.Uuid, serieFolio,
                c.FechaEmision, c.Tipo == "E" ? "Nota de crédito" : "PUE · fecha de emisión", signo * c.Base16 * tc, signo * c.Iva16 * tc));
        }

        var proveedores = partidas.GroupBy(p => p.Rfc)
            .Select(g => new DiotProveedor(g.Key, g.First().Nombre, Math.Round(g.Sum(p => p.Base), 2), Math.Round(g.Sum(p => p.Iva), 2),
                g.OrderBy(p => p.Fecha).ToList()))
            .OrderByDescending(p => p.Iva)
            .ToList();

        return new DiotBoveda
        {
            Ejercicio = ejercicio,
            Mes = mes,
            HayXml = repo.ContarRecibidos(empresaId, desde, hasta) > 0,
            Proveedores = proveedores,
            PagosSinImpuestos = sinImpuestos
        };
    }

    /// <summary>
    /// Pagos del complemento con FechaPago en [desde, hasta): base e IVA al 16 % de ImpuestosP, en pesos. Sin
    /// ImpuestosP (Pagos 1.0) no se puede saber el IVA: <paramref name="conImpuestos"/> queda en false.
    /// </summary>
    private static List<(DateTime Fecha, decimal Base, decimal Iva)> PagosDelMes(string ruta, DateTime desde, DateTime hasta, out bool conImpuestos)
    {
        conImpuestos = true;
        var resultado = new List<(DateTime, decimal, decimal)>();

        XElement raiz;
        try
        {
            raiz = XDocument.Load(ruta).Root!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
        {
            return resultado;
        }

        static string A(XElement e, string nombre) => e.Attributes().FirstOrDefault(a => a.Name.LocalName == nombre)?.Value ?? "";
        static decimal N(XElement e, string nombre) => decimal.TryParse(A(e, nombre), NumberStyles.Number, Inv, out decimal v) ? v : 0;

        foreach (XElement pago in raiz.Descendants().Where(e => e.Name.LocalName == "Pago"))
        {
            if (!DateTime.TryParse(A(pago, "FechaPago"), Inv, DateTimeStyles.None, out DateTime fecha) || fecha < desde || fecha >= hasta)
                continue;

            List<XElement> traslados = pago.Elements().Where(e => e.Name.LocalName == "ImpuestosP")
                .SelectMany(i => i.Descendants().Where(e => e.Name.LocalName == "TrasladoP")).ToList();

            if (!pago.Elements().Any(e => e.Name.LocalName == "ImpuestosP"))
            {
                conImpuestos = false;
                continue;
            }

            decimal tc = A(pago, "MonedaP") is "" or "MXN" ? 1 : N(pago, "TipoCambioP") is > 0 and var t ? t : 1;
            var iva16 = traslados.Where(t => A(t, "ImpuestoP") == "002" && N(t, "TasaOCuotaP") == 0.16m).ToList();
            if (iva16.Count > 0)
                resultado.Add((fecha, iva16.Sum(t => N(t, "BaseP")) * tc, iva16.Sum(t => N(t, "ImporteP")) * tc));
        }

        return resultado;
    }

    /// <summary>Excel de la conciliación: resumen por proveedor contra el acuse y detalle de comprobantes.</summary>
    public byte[] GenerarExcel(string rfc, DiotBoveda diot, DeclaracionSatDto? acuse)
    {
        const string moneda = "\"$\"#,##0.00;\\(\"$\"#,##0.00\\);\\-";
        string periodo = $"{CultureInfo.GetCultureInfo("es-MX").DateTimeFormat.GetMonthName(diot.Mes).ToUpperInvariant()} {diot.Ejercicio}";

        using var libro = new XLWorkbook();
        libro.Style.Font.FontName = "Segoe UI";

        var ws = libro.AddWorksheet("Por proveedor");
        ws.Cell("A1").Value = $"CONCILIACIÓN DIOT · {rfc} · {periodo}";
        ws.Range("A1:F1").Merge().Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#5B21B6"));
        ws.Cell("A2").Value = "Criterio: facturas PUE y notas de crédito por fecha de emisión; PPD por fecha de pago del complemento (IVA 16 %, sin cancelados).";
        ws.Cell("A2").Style.Font.SetFontSize(9).Font.SetFontColor(XLColor.Gray);

        (string Etiqueta, object? Acuse, object Boveda)[] resumen =
        [
            ("Operaciones (proveedores)", acuse?.Operaciones, diot.Operaciones),
            ("IVA pagado", acuse?.IvaPagado, diot.Iva),
        ];
        ws.Cell("A4").Value = "CONCEPTO";
        ws.Cell("B4").Value = "ACUSE DIOT";
        ws.Cell("C4").Value = "BÓVEDA";
        ws.Cell("D4").Value = "DIFERENCIA";
        for (int i = 0; i < resumen.Length; i++)
        {
            int r = 5 + i;
            ws.Cell(r, 1).Value = resumen[i].Etiqueta;
            ws.Cell(r, 2).Value = XLCellValue.FromObject(resumen[i].Acuse);
            ws.Cell(r, 3).Value = XLCellValue.FromObject(resumen[i].Boveda);
            ws.Cell(r, 4).FormulaA1 = $"IF(B{r}=\"\",\"\",B{r}-C{r})";
        }
        ws.Range("B6:D6").Style.NumberFormat.Format = moneda;
        ws.Range("A4:D4").Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#EDE9FE"));

        string[] encabezados = ["RFC", "PROVEEDOR", "COMPROBANTES", "VALOR ACTOS 16 %", "IVA PAGADO"];
        for (int i = 0; i < encabezados.Length; i++) ws.Cell(9, i + 1).Value = encabezados[i];
        int fila = 10;
        foreach (DiotProveedor p in diot.Proveedores)
        {
            ws.Cell(fila, 1).Value = p.Rfc;
            ws.Cell(fila, 2).Value = p.Nombre;
            ws.Cell(fila, 3).Value = p.Partidas.Count;
            ws.Cell(fila, 4).Value = p.Base;
            ws.Cell(fila, 5).Value = p.Iva;
            fila++;
        }
        var tabla = ws.Range(9, 1, Math.Max(10, fila - 1), encabezados.Length).CreateTable("TblDiotProveedores");
        tabla.Theme = XLTableTheme.TableStyleMedium12;
        tabla.ShowTotalsRow = true;
        tabla.Field("RFC").TotalsRowLabel = "TOTAL";
        foreach (string campo in new[] { "COMPROBANTES", "VALOR ACTOS 16 %", "IVA PAGADO" }) tabla.Field(campo).TotalsRowFunction = XLTotalsRowFunction.Sum;
        ws.Range(10, 4, fila, 5).Style.NumberFormat.Format = moneda;
        ws.Column(1).Width = 16;
        ws.Column(2).Width = 50;
        ws.Columns(3, 5).Width = 18;
        ws.SheetView.FreezeRows(9);

        var det = libro.AddWorksheet("Detalle");
        string[] columnas = ["RFC", "PROVEEDOR", "TIPO", "CRITERIO", "FECHA", "SERIE-FOLIO", "UUID", "VALOR ACTOS 16 %", "IVA"];
        for (int i = 0; i < columnas.Length; i++) det.Cell(1, i + 1).Value = columnas[i];
        int r2 = 2;
        foreach (DiotPartida x in diot.Proveedores.SelectMany(p => p.Partidas))
        {
            object[] valores = [x.Rfc, x.Nombre, x.Tipo, x.Criterio, x.Fecha, x.SerieFolio, x.Uuid, x.Base, x.Iva];
            for (int i = 0; i < valores.Length; i++)
                det.Cell(r2, i + 1).Value = valores[i] switch { decimal d => d, DateTime f => f, var o => XLCellValue.FromObject(o, Inv) };
            r2++;
        }
        var tablaDet = det.Range(1, 1, Math.Max(2, r2 - 1), columnas.Length).CreateTable("TblDiotDetalle");
        tablaDet.Theme = XLTableTheme.TableStyleMedium12;
        tablaDet.ShowTotalsRow = true;
        tablaDet.Field("RFC").TotalsRowLabel = "TOTAL";
        tablaDet.Field("VALOR ACTOS 16 %").TotalsRowFunction = XLTotalsRowFunction.Sum;
        tablaDet.Field("IVA").TotalsRowFunction = XLTotalsRowFunction.Sum;
        det.Column(5).Style.NumberFormat.Format = "dd/mm/yyyy";
        det.Columns(8, 9).Style.NumberFormat.Format = moneda;
        det.Columns().AdjustToContents(1, Math.Min(r2, 300), 8, 50);
        det.SheetView.FreezeRows(1);

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return memoria.ToArray();
    }
}
