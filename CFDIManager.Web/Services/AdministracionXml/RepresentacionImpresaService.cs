using System.Globalization;
using CFDIParser.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Cat = CFDIManager.Web.Services.AdministracionXml.ReporteGeneral.Catalogos;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>Datos del cliente que no vienen en el XML (catálogo de clientes de SINUBE): van debajo del receptor.</summary>
public sealed record DatosClientePdf(string NombreComercial = "", string CodigoAux = "", string Referencia = "");

/// <summary>
/// Representación impresa (PDF) de un CFDI con el formato de SINUBE (las mismas variables de su plantilla:
/// #emisor_rfc#, #comprobante_serie#, #cfdi_uuid#, impuestos por concepto, total con letra, sellos, QR…). El logo se toma
/// del catálogo de logos por RFC emisor (Storage/Logos).
/// </summary>
public class RepresentacionImpresaService
{
    private const string Negro = "#000000";
    private const string Cafe = "#1F0A06";
    private const string GrisFondo = "#EEEEEE";
    private const string GrisFolio = "#BBBBBB";
    private const string GrisTexto = "#808080";
    private const string GrisClaro = "#C0C0C0";
    private const string Texto = "#333333";

    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    private static readonly Dictionary<string, string> Tipos = new()
    {
        ["I"] = "Ingreso", ["E"] = "Egreso", ["T"] = "Traslado", ["P"] = "Pago", ["N"] = "Nómina"
    };

    private static readonly Dictionary<string, string> Exportaciones = new()
    {
        ["01"] = "No Aplica", ["02"] = "Definitiva con clave A1", ["03"] = "Temporal",
        ["04"] = "Definitiva con clave distinta a A1"
    };

    private static readonly Dictionary<string, string> NombresImpuesto = new() { ["001"] = "ISR", ["002"] = "IVA", ["003"] = "IEPS" };

    private readonly CatalogoLogosService _logos;

    public RepresentacionImpresaService(CatalogoLogosService logos)
    {
        _logos = logos;
    }

    public byte[] Generar(CfdiDto c, DatosClientePdf? cliente = null) => Crear(c, cliente).GeneratePdf();

    /// <summary>
    /// Primera página como PNG para la vista previa: se ve aunque el navegador esté
    /// configurado para descargar los PDF en lugar de mostrarlos.
    /// </summary>
    public byte[] GenerarImagen(CfdiDto c) =>
        Crear(c, null).GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 160
        }).First();

    private IDocument Crear(CfdiDto c, DatosClientePdf? cliente)
    {
        byte[]? logo = _logos.Leer(c.RFCEmisor);
        byte[] qr = GenerarQr(UrlVerificacion(c));
        cliente ??= new DatosClientePdf();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginHorizontal(36);
                page.MarginVertical(28);
                page.DefaultTextStyle(t => t.FontSize(8).FontColor(Texto));

                page.Content().Column(col =>
                {
                    col.Item().Element(e => Encabezado(e, c, logo, cliente));
                    col.Item().PaddingTop(10).Element(e => Conceptos(e, c));
                    col.Item().PaddingTop(8).Element(e => Totales(e, c));
                    col.Item().PaddingTop(10).Element(e => CondicionesPago(e, c));
                    col.Item().PaddingTop(12).Element(e => Sellos(e, c, qr));
                    col.Item().PaddingTop(12).Background(GrisFondo).Padding(4).AlignCenter().Text(t =>
                    {
                        t.Span("Este documento es una representación impresa de un CFDI ").FontSize(8);
                        t.Span($" {c.Version} ").FontSize(7).Bold().FontColor(Colors.White).BackgroundColor(Texto);
                    });
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.CurrentPageNumber().FontSize(6.5f).FontColor(GrisTexto);
                    t.Span(" / ").FontSize(6.5f).FontColor(GrisTexto);
                    t.TotalPages().FontSize(6.5f).FontColor(GrisTexto);
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

    private static string Fecha(DateTime? f) => f is DateTime d ? d.ToString("dd/MM/yyyy hh:mm:ss tt", CultureInfo.InvariantCulture) : "";

    private static string Con(Dictionary<string, string> catalogo, string? clave) =>
        string.IsNullOrWhiteSpace(clave) ? "" : $"{clave} {catalogo.GetValueOrDefault(clave.Trim(), "")}".Trim();

    // ---------- Encabezado: emisor y datos del comprobante a la derecha; receptor debajo ----------

    private static void Encabezado(IContainer e, CfdiDto c, byte[]? logo, DatosClientePdf cliente)
    {
        e.Row(row =>
        {
            row.RelativeItem().Column(izq =>
            {
                // Emisor con logo, separado por una línea gris del receptor.
                izq.Item().BorderBottom(2).BorderColor(GrisFondo).PaddingBottom(8).Row(r =>
                {
                    if (logo is not null)
                    {
                        r.ConstantItem(90).Height(55).AlignMiddle().Image(logo).FitArea();
                        r.ConstantItem(10);
                    }

                    r.RelativeItem().AlignMiddle().Column(col =>
                    {
                        col.Item().Text(string.IsNullOrWhiteSpace(c.NombreEmisor) ? c.RFCEmisor : c.NombreEmisor).Bold().FontSize(10).FontColor(Negro);
                        col.Item().Text(t =>
                        {
                            t.Span("R.F.C. : ").Bold().FontSize(7).FontColor(GrisTexto);
                            t.Span(c.RFCEmisor).Bold().FontSize(7).FontColor(Negro);
                        });
                        col.Item().PaddingTop(3).Text(t =>
                        {
                            t.Span("REGIMEN FISCAL : ").Bold().FontSize(7).FontColor(GrisTexto);
                            t.Span(Con(Cat.Regimen, c.RegimenFiscalEmisor)).Bold().FontSize(8).FontColor(Negro);
                        });
                    });
                });

                izq.Item().PaddingTop(14).Row(r =>
                {
                    r.ConstantItem(90).AlignMiddle().AlignCenter().Text(c.TipoComprobante is "E" ? "Receptor" : "Receptor").Bold().FontSize(9).FontColor(Negro);
                    r.ConstantItem(10);
                    r.RelativeItem().Column(col =>
                    {
                        col.Item().Text(string.IsNullOrWhiteSpace(c.NombreReceptor) ? c.RFCReceptor : c.NombreReceptor).Bold().FontSize(10).FontColor(Negro);
                        if (cliente.CodigoAux != "")
                            col.Item().Text(cliente.CodigoAux).FontSize(7).FontColor(GrisTexto);

                        void Dato(string etiqueta, string valor)
                        {
                            if (string.IsNullOrWhiteSpace(valor))
                                return;
                            col.Item().Text(t =>
                            {
                                t.Span($"{etiqueta} ").Bold().FontSize(7).FontColor(GrisTexto);
                                t.Span(valor).Bold().FontSize(7).FontColor(Negro);
                            });
                        }

                        Dato("R.F.C. :", c.RFCReceptor);
                        Dato("DOMICILIO FISCAL:", c.DomicilioFiscalReceptor);
                        Dato("RÉGIMEN:", Con(Cat.Regimen, c.RegimenFiscalReceptor));
                        if (!string.IsNullOrWhiteSpace(c.UsoCfdi))
                            Dato("USO CFDI :", $"{c.UsoCfdi} - {Cat.Uso.GetValueOrDefault(c.UsoCfdi, "")}".TrimEnd(' ', '-'));

                        if (cliente.NombreComercial != "")
                            col.Item().PaddingTop(8).Text(cliente.NombreComercial).Bold().FontSize(7).FontColor(Negro);
                        if (cliente.Referencia != "")
                            col.Item().PaddingTop(4).Text(cliente.Referencia).FontSize(8);
                    });
                });
            });

            row.ConstantItem(10);

            // Datos del comprobante (columna derecha de SINUBE).
            row.ConstantItem(170).Column(der =>
            {
                der.Item().AlignRight().Text(t =>
                {
                    t.Span(" CFDI Ver. ").Bold().FontSize(7).FontColor(Colors.White).BackgroundColor(Negro);
                    t.Span($" {c.Version} ").Bold().FontSize(7).FontColor(Colors.White).BackgroundColor(Texto);
                });

                der.Item().BorderLeft(2).BorderColor(GrisFondo).Column(col =>
                {
                    col.Item().Background(Cafe).AlignCenter().Text("Serie - Folio").Bold().FontColor(Colors.White);
                    col.Item().Background(GrisFolio).AlignCenter().Text(t =>
                    {
                        t.Span($"{c.Serie} - ").Bold();
                        t.Span(c.Folio).Bold().FontColor(Negro);
                    });

                    void Etiqueta(string texto) => col.Item().Background(GrisFondo).Text(texto).Bold().FontColor(Negro);

                    Etiqueta("Número de serie del CSD");
                    col.Item().Text(c.NoCertificado);
                    Etiqueta("C.P., Fecha y Hora de emisión");
                    col.Item().Text($"{c.LugarExpedicion} {Fecha(c.FechaEmision)}").Bold().FontColor(GrisTexto);
                    col.Item().PaddingTop(10).Background(GrisFondo).AlignCenter().Text("Folio Fiscal").Bold().FontColor(Negro);
                    col.Item().Text(c.UUID.ToLowerInvariant()).Bold().FontSize(7).FontColor(GrisClaro);
                    col.Item().Text(t =>
                    {
                        t.Span("Tipo de comprobante : ").Bold().FontSize(7).FontColor(Negro);
                        t.Span($"{c.TipoComprobante} {Tipos.GetValueOrDefault(c.TipoComprobante, "")}").Bold().FontSize(7).FontColor(GrisClaro);
                    });
                    col.Item().Text("Fecha y hora de Certificación").Bold().FontColor(Negro);
                    col.Item().Text(Fecha(c.FechaTimbrado)).Bold().FontColor("#969696");
                    col.Item().PaddingTop(6).Background(GrisFondo).Text("Exportación").Bold().FontColor(Negro);
                    col.Item().Text(t =>
                    {
                        t.Span($"{c.Exportacion} ");
                        if (!string.IsNullOrWhiteSpace(c.Exportacion))
                            t.Span($": {Exportaciones.GetValueOrDefault(c.Exportacion, "")}").Bold();
                    });
                });
            });
        });
    }

    // ---------- Conceptos (encabezado negro) con sus impuestos debajo de cada uno ----------

    private static void Conceptos(IContainer e, CfdiDto c)
    {
        e.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.ConstantColumn(52);
                cd.ConstantColumn(50);
                cd.ConstantColumn(44);
                cd.RelativeColumn();
                cd.ConstantColumn(62);
                cd.ConstantColumn(62);
                cd.ConstantColumn(50);
            });

            t.Header(h =>
            {
                foreach (string titulo in new[] { "Clave SAT", "Unidad SAT", "Cantidad", "Descripción", "Precio unitario", "Importe", "Obj. de impuesto" })
                    h.Cell().Background(Negro).Padding(4).Text(titulo).Bold().FontColor(Colors.White);
            });

            foreach (ConceptoDto k in c.Conceptos)
            {
                IContainer Celda() => t.Cell().Padding(3);

                Celda().Text(k.ClaveProdServ);
                Celda().Text(string.IsNullOrWhiteSpace(k.ClaveUnidad) ? k.Unidad : k.ClaveUnidad);
                Celda().AlignCenter().Text(k.Cantidad.ToString("0.######", Mx));
                Celda().Text(k.Descripcion);
                Celda().AlignRight().Text(Dinero(k.ValorUnitario));
                Celda().AlignRight().Text(Dinero(k.Importe)).Bold();
                Celda().Text(k.ObjetoImp).Bold();

                if (k.Impuestos.Count > 0)
                    t.Cell().ColumnSpan(7).AlignRight().Element(x => ImpuestosConcepto(x, k));
            }
        });
    }

    private static void ImpuestosConcepto(IContainer e, ConceptoDto k)
    {
        e.Width(300).Background(GrisFondo).Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                for (int i = 0; i < 6; i++)
                    cd.RelativeColumn();
            });

            t.Header(h =>
            {
                foreach (string titulo in new[] { "Tipo imp", "Impuesto", "Tipo Factor", "Tasa o Cuota", "Base", "Importe" })
                    h.Cell().BorderBottom(1).BorderColor("#CCCCCC").Padding(1.5f).AlignCenter().Text(titulo).Bold().FontSize(6).FontColor("#888888");
            });

            foreach (ImpuestoDto i in k.Impuestos)
            {
                bool retencion = i.Tipo.StartsWith("RET", StringComparison.OrdinalIgnoreCase);
                bool exento = i.TipoFactor.Equals("Exento", StringComparison.OrdinalIgnoreCase);

                IContainer Celda() => t.Cell().Padding(1.5f);
                Celda().AlignCenter().Text(retencion ? "Retención" : "Traslado").FontSize(6.5f).FontColor("#444444");
                Celda().AlignCenter().Text(i.Impuesto).FontSize(6.5f).FontColor("#444444");
                Celda().AlignCenter().Text(i.TipoFactor).FontSize(6.5f).FontColor("#444444");
                Celda().AlignRight().Text(exento ? "" : Tasa(i)).FontSize(6.5f).FontColor("#444444");
                Celda().AlignRight().Text(Dinero(i.Base)).FontSize(6.5f).FontColor("#444444");
                Celda().AlignRight().Text(exento ? "" : Dinero(i.Importe)).FontSize(6.5f).FontColor("#444444");
            }
        });
    }

    // Tasa 0.08 → "8.0%"; cuota (IEPS por litro) se muestra tal cual.
    private static string Tasa(ImpuestoDto i) =>
        i.TipoFactor.Equals("Cuota", StringComparison.OrdinalIgnoreCase)
            ? i.Tasa.ToString("0.######", Mx)
            : (i.Tasa * 100).ToString("0.0#####", CultureInfo.InvariantCulture) + "%";

    // ---------- Totales: importe con letra a la izquierda; etiquetas en negro a la derecha ----------

    private static void Totales(IContainer e, CfdiDto c)
    {
        var lineas = new List<(string Etiqueta, decimal Valor)> { ("SUBTOTAL", c.SubTotal), ("DESCUENTO", c.Descuento ?? 0) };

        // Un renglón por impuesto y tasa, como #iepstrasltasa1# / #ivatrasladadoporcentaje# de SINUBE.
        foreach (var g in c.Impuestos
                     .GroupBy(i => (Retencion: i.Tipo.StartsWith("RET", StringComparison.OrdinalIgnoreCase), i.Impuesto, i.Tasa, i.TipoFactor))
                     .OrderBy(g => g.Key.Retencion).ThenByDescending(g => g.Key.Impuesto))
        {
            string nombre = NombresImpuesto.GetValueOrDefault(g.Key.Impuesto, g.Key.Impuesto);
            string tasa = g.Key.TipoFactor.Equals("Exento", StringComparison.OrdinalIgnoreCase)
                ? "EXENTO"
                : Tasa(new ImpuestoDto { Tasa = g.Key.Tasa, TipoFactor = g.Key.TipoFactor });
            lineas.Add(($"{(g.Key.Retencion ? "RET " : "")}{nombre} {tasa}", g.Sum(i => i.Importe)));
        }

        if (!c.Impuestos.Any(i => i.Impuesto == "002"))
            lineas.Add(("IVA 0.0%", 0));

        e.BorderTop(2).BorderColor(GrisFondo).Column(col =>
        {
            for (int n = 0; n < lineas.Count; n++)
            {
                var (etiqueta, valor) = lineas[n];
                int indice = n;
                col.Item().PaddingVertical(1).Row(r =>
                {
                    var letra = r.RelativeItem().Text(indice switch
                    {
                        0 => "Importe con letra:",
                        1 => ImporteConLetra.Convertir(c.Total, c.Moneda),
                        _ => ""
                    }).FontSize(8);
                    if (indice == 1)
                        letra.Bold();
                    r.ConstantItem(110).Text(t => t.Span($" {etiqueta} ").Bold().FontColor(Colors.White).BackgroundColor(Negro));
                    r.ConstantItem(90).AlignRight().Text(Dinero(valor));
                });
            }

            col.Item().PaddingTop(4).Background(Negro).Padding(4).Row(r =>
            {
                r.RelativeItem();
                r.ConstantItem(110).Text("TOTAL").Bold().FontColor(Colors.White);
                r.ConstantItem(90).AlignRight().Text(Dinero(c.Total)).Bold().FontColor(Colors.White);
            });
        });
    }

    private static void CondicionesPago(IContainer e, CfdiDto c)
    {
        e.Background(GrisFondo).Padding(5).Column(col =>
        {
            col.Item().Text("CONDICIONES, FORMA  y METODO DE PAGO").Bold().FontSize(7.5f).FontColor(Negro);
            col.Item().Text(string.Join("  |  ", new[]
            {
                c.CondicionesDePago,
                string.IsNullOrWhiteSpace(c.FormaPago) ? "" : $"{c.FormaPago} - {Cat.Forma.GetValueOrDefault(c.FormaPago, "")}",
                string.IsNullOrWhiteSpace(c.MetodoPago) ? "" : $"{c.MetodoPago} - {Cat.Metodo.GetValueOrDefault(c.MetodoPago, "")}"
            }.Where(x => !string.IsNullOrWhiteSpace(x)))).FontSize(7.5f).FontColor("#969696");

            if (c.Moneda is { Length: > 0 } moneda && moneda != "MXN")
                col.Item().Text($"Moneda: {moneda}{(c.TipoCambio is decimal tc ? $" · Tipo de cambio: {tc}" : "")}").FontSize(7.5f).FontColor("#969696");

            if (c.TipoComprobante == "P" && c.MontoTotalPagos is decimal pagos)
                col.Item().Text($"Monto total de pagos: {Dinero(pagos)}").FontSize(7.5f).Bold();

            foreach (CfdiRelacionadoDto r in c.Relacionados)
                col.Item().Text($"CFDI relacionado ({r.TipoRelacion}): {r.UUID}").FontSize(7.5f).FontColor("#969696");
        });
    }

    // ---------- Sellos, cadena original y QR ----------

    private static void Sellos(IContainer e, CfdiDto c, byte[] qr)
    {
        string selloCfd = string.IsNullOrEmpty(c.SelloCfd) ? c.Sello : c.SelloCfd;
        string cadena = $"||{c.VersionTimbre}|{c.UUID}|{c.FechaTimbrado:yyyy-MM-ddTHH:mm:ss}|{c.RfcProvCertif}|{selloCfd}|{c.NoCertificadoSat}||";

        e.Column(col =>
        {
            void Bloque(string titulo, string valor)
            {
                col.Item().PaddingTop(6).Text(titulo).Bold().FontSize(7.5f).FontColor(Negro);
                col.Item().Text(valor).FontSize(7.5f).FontColor(Negro);
            }

            Bloque("Sello digital del CFDI:", selloCfd);
            Bloque("Sello del SAT:", c.SelloSat);
            Bloque("Cadena original del complemento de certificación digital del SAT:", cadena);

            col.Item().ShowEntire().Row(r =>
            {
                r.RelativeItem().Column(datos =>
                {
                    datos.Item().PaddingTop(8).Text("No de Serie de Certificado del SAT:").Bold().FontSize(7.5f).FontColor(Negro);
                    datos.Item().Text(c.NoCertificadoSat).FontSize(7.5f);
                    datos.Item().PaddingTop(8).Text("Fecha y hora de certificación:").Bold().FontSize(7.5f).FontColor(Negro);
                    datos.Item().Text(Fecha(c.FechaTimbrado)).FontSize(7.5f);
                });
                r.ConstantItem(100).PaddingTop(6).Image(qr);
            });
        });
    }
}
