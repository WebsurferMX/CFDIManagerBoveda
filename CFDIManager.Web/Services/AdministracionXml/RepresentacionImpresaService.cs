using System.Globalization;
using CFDIParser.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Representación impresa (PDF) de un CFDI a partir de su XML: conceptos, impuestos,
/// sellos, cadena original del timbre y QR de verificación del SAT. El logo se toma
/// del catálogo de logos por RFC emisor (Storage/Logos).
/// </summary>
public class RepresentacionImpresaService
{
    private const string Acento = "#1F4E8C";
    private const string Gris = "#6B7280";
    private const string Linea = "#D1D5DB";

    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    private static readonly Dictionary<string, string> Titulos = new()
    {
        ["I"] = "Factura",
        ["E"] = "Nota de crédito",
        ["T"] = "Traslado",
        ["P"] = "Recibo de pago",
        ["N"] = "Recibo de nómina"
    };

    private static readonly Dictionary<string, string> Impuestos = new()
    {
        ["001"] = "ISR",
        ["002"] = "IVA",
        ["003"] = "IEPS"
    };

    private readonly CatalogoLogosService _logos;

    public RepresentacionImpresaService(CatalogoLogosService logos)
    {
        _logos = logos;
    }

    public byte[] Generar(CfdiDto c) => Crear(c).GeneratePdf();

    /// <summary>
    /// Primera página como PNG para la vista previa: se ve aunque el navegador esté
    /// configurado para descargar los PDF en lugar de mostrarlos.
    /// </summary>
    public byte[] GenerarImagen(CfdiDto c) =>
        Crear(c).GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 160
        }).First();

    private IDocument Crear(CfdiDto c)
    {
        byte[]? logo = _logos.Leer(c.RFCEmisor);
        byte[] qr = GenerarQr(UrlVerificacion(c));

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(8));

                page.Header().Element(e => Encabezado(e, c, logo));
                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Element(e => Receptor(e, c));
                    col.Item().Element(e => Conceptos(e, c));
                    col.Item().Element(e => Totales(e, c));
                    col.Item().Element(e => Timbre(e, c, qr));
                });
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Este documento es una representación impresa de un CFDI · Página ").FontColor(Gris);
                    t.CurrentPageNumber().FontColor(Gris);
                    t.Span(" de ").FontColor(Gris);
                    t.TotalPages().FontColor(Gris);
                });
            });
        });
    }

    /// <summary>URL del QR según el Anexo 20 (3.3 y 4.0); en 3.2 se usa la expresión impresa anterior.</summary>
    public static string UrlVerificacion(CfdiDto c)
    {
        string tt = c.Total.ToString("0.000000", CultureInfo.InvariantCulture).TrimEnd('0');
        tt = tt.EndsWith('.') ? tt + "0" : tt;

        if (decimal.TryParse(c.Version, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) && v < 3.3m)
        {
            string[] partes = c.Total.ToString("0.000000", CultureInfo.InvariantCulture).Split('.');
            return $"?re={c.RFCEmisor}&rr={c.RFCReceptor}&tt={partes[0].PadLeft(10, '0')}.{partes[1]}&id={c.UUID}";
        }

        string sello = string.IsNullOrEmpty(c.SelloCfd) ? c.Sello : c.SelloCfd;
        string fe = sello.Length >= 8 ? sello[^8..] : sello;

        return "https://verificacfdi.facturaelectronica.sat.gob.mx/default.aspx" +
               $"?id={c.UUID}&re={c.RFCEmisor}&rr={c.RFCReceptor}&tt={tt}&fe={Uri.EscapeDataString(fe)}";
    }

    private static byte[] GenerarQr(string texto)
    {
        using var generador = new QRCodeGenerator();
        using QRCodeData datos = generador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(datos).GetGraphic(8);
    }

    private static string Dinero(decimal? v) => (v ?? 0).ToString("C2", Mx);

    private static void Encabezado(IContainer e, CfdiDto c, byte[]? logo)
    {
        e.Row(row =>
        {
            if (logo is not null)
            {
                row.ConstantItem(110).Height(60).AlignMiddle().Image(logo).FitArea();
                row.ConstantItem(10);
            }

            row.RelativeItem().Column(col =>
            {
                col.Item().Text(string.IsNullOrWhiteSpace(c.NombreEmisor) ? c.RFCEmisor : c.NombreEmisor).Bold().FontSize(11);
                col.Item().Text($"RFC: {c.RFCEmisor}").FontColor(Gris);
                col.Item().Text($"Régimen fiscal: {c.RegimenFiscalEmisor}").FontColor(Gris);
                col.Item().Text($"Lugar de expedición: {c.LugarExpedicion}").FontColor(Gris);
            });

            row.ConstantItem(200).Border(1).BorderColor(Linea).Padding(6).Column(col =>
            {
                col.Item().Text(Titulos.GetValueOrDefault(c.TipoComprobante, "CFDI")).Bold().FontSize(12).FontColor(Acento);
                col.Item().Text($"Serie y folio: {string.Join("-", new[] { c.Serie, c.Folio }.Where(x => !string.IsNullOrWhiteSpace(x)))}");
                col.Item().Text($"Fecha: {c.FechaEmision:dd/MM/yyyy HH:mm:ss}");
                col.Item().Text("Folio fiscal:");
                col.Item().Text(c.UUID);
                col.Item().Text($"CFDI {c.Version} · Certificado: {c.NoCertificado}");
            });
        });
    }

    private static void Receptor(IContainer e, CfdiDto c)
    {
        e.Column(col =>
        {
            col.Item().Background(Acento).PaddingHorizontal(6).PaddingVertical(3).Text("RECEPTOR").Bold().FontColor(Colors.White);
            col.Item().PaddingTop(3).Text(string.IsNullOrWhiteSpace(c.NombreReceptor) ? c.RFCReceptor : c.NombreReceptor).Bold().FontSize(9);
            col.Item().Text(string.Join("   ·   ", new[]
            {
                $"RFC: {c.RFCReceptor}",
                string.IsNullOrWhiteSpace(c.UsoCfdi) ? "" : $"Uso CFDI: {c.UsoCfdi}",
                string.IsNullOrWhiteSpace(c.DomicilioFiscalReceptor) ? "" : $"C.P.: {c.DomicilioFiscalReceptor}",
                string.IsNullOrWhiteSpace(c.RegimenFiscalReceptor) ? "" : $"Régimen: {c.RegimenFiscalReceptor}"
            }.Where(x => x != ""))).FontColor(Gris);
        });
    }

    private static void Conceptos(IContainer e, CfdiDto c)
    {
        e.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.ConstantColumn(56);
                cd.ConstantColumn(42);
                cd.ConstantColumn(40);
                cd.RelativeColumn();
                cd.ConstantColumn(72);
                cd.ConstantColumn(72);
            });

            t.Header(h =>
            {
                foreach (string titulo in new[] { "Clave", "Cant.", "Unidad", "Descripción", "P. unitario", "Importe" })
                {
                    var celda = h.Cell().Background(Acento).Padding(3);
                    var texto = titulo is "Cant." or "P. unitario" or "Importe" ? celda.AlignRight() : celda;
                    texto.Text(titulo).Bold().FontColor(Colors.White);
                }
            });

            foreach (ConceptoDto k in c.Conceptos)
            {
                IContainer Celda() => t.Cell().BorderBottom(0.5f).BorderColor(Linea).Padding(3);

                Celda().Text(k.ClaveProdServ);
                Celda().AlignRight().Text(k.Cantidad.ToString("0.######", Mx));
                Celda().Text(string.IsNullOrWhiteSpace(k.ClaveUnidad) ? k.Unidad : k.ClaveUnidad);
                Celda().Text(k.Descripcion);
                Celda().AlignRight().Text(Dinero(k.ValorUnitario));
                Celda().AlignRight().Text(Dinero(k.Importe));
            }
        });
    }

    private static void Totales(IContainer e, CfdiDto c)
    {
        var lineas = new List<(string, decimal)> { ("Subtotal", c.SubTotal) };

        if (c.Descuento is > 0)
        {
            lineas.Add(("Descuento", -c.Descuento.Value));
        }

        foreach (var g in c.Impuestos.GroupBy(i => (i.Tipo, i.Impuesto)))
        {
            bool retencion = g.Key.Tipo.StartsWith("RET", StringComparison.OrdinalIgnoreCase);
            string nombre = Impuestos.GetValueOrDefault(g.Key.Impuesto, g.Key.Impuesto);
            decimal suma = g.Sum(i => i.Importe);
            lineas.Add(($"{nombre} {(retencion ? "retenido" : "trasladado")}", retencion ? -suma : suma));
        }

        e.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text($"Moneda: {c.Moneda}{(c.TipoCambio is decimal tc && tc != 1 ? $" · Tipo de cambio: {tc}" : "")}").FontColor(Gris);
                col.Item().Text($"Forma de pago: {Valor(c.FormaPago)} · Método de pago: {Valor(c.MetodoPago)}").FontColor(Gris);

                if (c.MontoTotalPagos is decimal pagos && c.TipoComprobante == "P")
                {
                    col.Item().Text($"Monto total de pagos: {Dinero(pagos)}").Bold();
                }

                foreach (CfdiRelacionadoDto r in c.Relacionados)
                {
                    col.Item().Text($"CFDI relacionado ({r.TipoRelacion}): {r.UUID}").FontColor(Gris);
                }
            });

            row.ConstantItem(220).Column(col =>
            {
                foreach (var (texto, valor) in lineas)
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text(texto).FontColor(Gris);
                        r.ConstantItem(90).AlignRight().Text(Dinero(valor));
                    });
                }

                col.Item().PaddingTop(3).Background(Acento).Padding(4).Row(r =>
                {
                    r.RelativeItem().Text("Total").Bold().FontColor(Colors.White);
                    r.ConstantItem(90).AlignRight().Text(Dinero(c.Total)).Bold().FontColor(Colors.White);
                });
            });
        });
    }

    private static void Timbre(IContainer e, CfdiDto c, byte[] qr)
    {
        string selloCfd = string.IsNullOrEmpty(c.SelloCfd) ? c.Sello : c.SelloCfd;
        string cadena = $"||{c.VersionTimbre}|{c.UUID}|{c.FechaTimbrado:yyyy-MM-ddTHH:mm:ss}|{c.RfcProvCertif}|{selloCfd}|{c.NoCertificadoSat}||";

        e.ShowEntire().Row(row =>
        {
            row.ConstantItem(100).Image(qr);
            row.ConstantItem(10);
            row.RelativeItem().Column(col =>
            {
                void Bloque(string titulo, string valor)
                {
                    col.Item().Text(titulo).Bold().FontSize(6.5f);
                    col.Item().Text(valor).FontSize(6.5f).FontColor(Gris);
                }

                Bloque("Sello digital del CFDI", selloCfd);
                Bloque("Sello del SAT", c.SelloSat);
                Bloque("Cadena original del complemento de certificación digital del SAT", cadena);
                col.Item().Text($"Certificado del SAT: {c.NoCertificadoSat}   ·   Fecha de certificación: {c.FechaTimbrado:dd/MM/yyyy HH:mm:ss}   ·   PAC: {c.RfcProvCertif}").FontSize(6.5f);
            });
        });
    }

    private static string Valor(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
}
