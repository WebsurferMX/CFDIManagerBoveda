using CFDIManager.Web.Services.Correo;
using CFDIManager.Web.Services.Sinube;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>CFDI de una misma contraparte (receptor en emitidos, emisor en recibidos): van en un solo correo.</summary>
public sealed class GrupoEnvioCfdi
{
    public string Rfc { get; init; } = "";
    public string Nombre { get; init; } = "";
    public List<AdminXmlFilaDto> Cfdis { get; init; } = [];

    /// <summary>Correo(s) del catálogo de clientes de SINUBE, separados por coma (editables, no se guardan).</summary>
    public string Para { get; set; } = "";

    /// <summary>Correos adicionales; se recuerdan por RFC con lo que tenga al enviar (vacío los borra).</summary>
    public string Adicionales { get; set; } = "";

    /// <summary>"SINUBE" si el correo salió del catálogo de clientes; "" si no.</summary>
    public string Origen { get; set; } = "";

    /// <summary>Nombre comercial del catálogo de clientes de SINUBE (va en el PDF debajo del receptor).</summary>
    public string NombreComercial { get; set; } = "";

    /// <summary>Cliente en SINUBE (código auxiliar, referencia bancaria, correo y direcciones para el formato de impresión).</summary>
    public ClienteSinube? Sinube { get; set; }

    /// <summary>Dirección de la empresa en SINUBE (#emisor_calle_fiscal#…).</summary>
    public DireccionSinube? DireccionEmisor { get; set; }

    public bool Enviar { get; set; } = true;

    /// <summary>Resultado del envío: "" sin enviar, "Enviado" o el error.</summary>
    public string Resultado { get; set; } = "";

    public decimal Total => Cfdis.Sum(x => x.TipoCfdi == "P" ? x.MontoTotalPagos ?? 0 : x.Total);
}

/// <summary>
/// "Enviar CFDI" de Administración de XML: manda a cada contraparte el PDF y el XML de sus comprobantes.
/// El PDF sale con el formato de impresión de SINUBE elegido (su plantilla vigente) o, sin SINUBE, con el formato propio.
/// El correo propuesto es el del catálogo de clientes de SINUBE más los adicionales recordados para ese RFC.
/// </summary>
public class EnvioCfdiService(
    PostgreSqlRepository repo,
    XmlCfdiParser parser,
    RepresentacionImpresaService pdf,
    HtmlPdfService htmlPdf,
    CorreoService correo,
    SinubeService sinube,
    SinubeDatosService datosSinube,
    AdendasSinubeService adendas,
    RutaXmlService rutas,
    ILogger<EnvioCfdiService> log)
{
    private static string ClaveCorreo(long empresaId, string rfc) => $"correo_cfdi:{empresaId}:{rfc.Trim().ToUpperInvariant()}";

    /// <summary>Formatos de impresión de facturas y pagos de SINUBE de la empresa (vacío si no tiene SINUBE o no responde).</summary>
    public async Task<List<FormatoImpresionSinube>> FormatosAsync(EmpresaDto empresa, CancellationToken ct = default)
    {
        SinubeConfiguracionDto config = sinube.ObtenerConfiguracion(empresa);
        if (SinubeService.FaltanDatos(config, requierePassword: true) is not null || HtmlPdfService.Navegador is null)
            return [];

        try
        {
            return await datosSinube.FormatosImpresionAsync(config, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "No se pudieron leer los formatos de impresión de SINUBE de {Empresa}", empresa.Rfc);
            return [];
        }
    }

    /// <summary>
    /// PDF del CFDI: con el formato de SINUBE que corresponde a su tipo (pago o factura) o, si no hay, con el formato propio.
    /// </summary>
    public async Task<byte[]> GenerarPdfAsync(
        string ruta, DatosClientePdf cliente, FormatoImpresionSinube? formatoCfdi, FormatoImpresionSinube? formatoPago, CancellationToken ct = default)
    {
        CfdiDto c = parser.Parse(ruta);
        FormatoImpresionSinube? formato = c.TipoComprobante switch
        {
            "P" => formatoPago,
            "N" => null,
            _ => formatoCfdi
        };

        if (formato is null)
            return await Task.Run(() => pdf.Generar(c, cliente), ct);

        string html = PlantillaSinube.Llenar(formato.Html, c, System.Xml.Linq.XDocument.Load(ruta), cliente, RepresentacionImpresaService.Qr(c));
        return await htmlPdf.ConvertirAsync(html, ct);
    }

    public async Task<List<GrupoEnvioCfdi>> PrepararAsync(EmpresaDto empresa, IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        List<AdminXmlFilaDto> filas = repo.ObtenerSeleccionAdministracionXml(new AdminXmlFiltroDto { EmpresaId = empresa.EmpresaId, Ids = ids.ToList() });
        string rfcEmpresa = empresa.Rfc.Trim().ToUpperInvariant();

        // Emitidos: el receptor; recibidos: el emisor (en un traslado propio, la misma empresa).
        (string Rfc, string Nombre) Contraparte(AdminXmlFilaDto x) =>
            x.RfcEmisor.Trim().ToUpperInvariant() == rfcEmpresa
                ? (x.RfcReceptor.Trim().ToUpperInvariant(), x.NombreReceptor)
                : (x.RfcEmisor.Trim().ToUpperInvariant(), x.NombreEmisor);

        var grupos = filas
            .GroupBy(x => Contraparte(x).Rfc)
            .Select(g => new GrupoEnvioCfdi
            {
                Rfc = g.Key,
                Nombre = Contraparte(g.First()).Nombre,
                Cfdis = g.OrderBy(x => x.FechaEmision).ToList()
            })
            .OrderBy(g => g.Nombre, StringComparer.CurrentCulture)
            .ToList();

        SinubeConfiguracionDto config = sinube.ObtenerConfiguracion(empresa);
        bool conSinube = SinubeService.FaltanDatos(config, requierePassword: true) is null;

        DireccionSinube? direccionEmpresa = null;
        if (conSinube)
        {
            try
            {
                direccionEmpresa = await datosSinube.DireccionEmpresaAsync(config, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "No se pudo leer la dirección de {Empresa} en SINUBE", empresa.Rfc);
            }
        }

        foreach (GrupoEnvioCfdi g in grupos)
        {
            g.DireccionEmisor = direccionEmpresa;

            // Primero el correo del catálogo de clientes de SINUBE (público en general no está ahí).
            if (conSinube && g.Rfc is not ("XAXX010101000" or "XEXX010101000"))
            {
                try
                {
                    if (await datosSinube.ClienteAsync(config, g.Rfc, ct) is { } cliente)
                    {
                        g.NombreComercial = cliente.NombreComercial;
                        g.Sinube = cliente;
                        if (cliente.Email != "")
                        {
                            g.Para = cliente.Email;
                            g.Origen = "SINUBE";
                        }
                    }
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Sin SINUBE se usa el último correo capturado.
                }
            }

            // Además, los adicionales que se dejaron la última vez para ese RFC.
            g.Adicionales = repo.ObtenerConfiguracionApp(ClaveCorreo(empresa.EmpresaId, g.Rfc)) ?? "";
        }

        return grupos;
    }

    /// <summary>Envía el PDF y el XML de cada CFDI del grupo y recuerda los correos adicionales.</summary>
    public async Task EnviarAsync(
        EmpresaDto empresa, GrupoEnvioCfdi grupo, IReadOnlyList<string> cc, string mensaje, string enviadoPor,
        FormatoImpresionSinube? formatoCfdi = null, FormatoImpresionSinube? formatoPago = null, CancellationToken ct = default)
    {
        List<string> adicionales = CorreoService.SepararCorreos(grupo.Adicionales);
        List<string> para = CorreoService.SepararCorreos(grupo.Para)
            .Concat(adicionales)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (para.Count == 0)
            throw new InvalidOperationException("Escribe al menos un correo.");

        var adjuntos = new List<AdjuntoCorreo>();
        ClienteSinube? s = grupo.Sinube;
        var cliente = new DatosClientePdf(
            grupo.NombreComercial, s?.CodigoAux ?? "", RefBancaria: s?.RefBancaria ?? "", Correo: s?.Email ?? "",
            Fiscal: s?.Fiscal, Envio: s?.Envio, Emisor: grupo.DireccionEmisor);

        var mesesRevisados = new HashSet<DateTime>();

        foreach (AdminXmlFilaDto x in grupo.Cfdis)
        {
            string ruta = RutaXmlLocal.Resolver(x.RutaXml);
            if (!File.Exists(ruta))
                throw new FileNotFoundException($"No se encontró el XML de {Nombre(x)}.");

            string nombre = Nombre(x);
            // Con adenda de SINUBE, el cliente recibe ese XML (mismo timbre); el PDF sale de la copia del SAT.
            byte[] xml = await File.ReadAllBytesAsync(await XmlConAdendaAsync(empresa, x, mesesRevisados, ct) ?? ruta, ct);
            byte[] archivoPdf = await GenerarPdfAsync(ruta, cliente, formatoCfdi, formatoPago, ct);
            adjuntos.Add(new AdjuntoCorreo($"{nombre}.pdf", "application/pdf", archivoPdf));
            adjuntos.Add(new AdjuntoCorreo($"{nombre}.xml", "application/xml", xml));
        }

        string emisor = empresa.NombreParaMostrar;
        string asunto = grupo.Cfdis.Count == 1
            ? $"CFDI {Folio(grupo.Cfdis[0])} · {emisor}"
            : $"{grupo.Cfdis.Count} CFDI · {emisor}";

        var parrafos = mensaje.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var datos = new List<(string, string)>();
        foreach (AdminXmlFilaDto x in grupo.Cfdis.Take(20))
            datos.Add(($"{Tipo(x.TipoCfdi)} {Folio(x)}", $"{(x.TipoCfdi == "P" ? x.MontoTotalPagos ?? 0 : x.Total):C2} · {x.FechaEmision:dd/MM/yyyy}"));
        if (grupo.Cfdis.Count > 20)
            datos.Add(("…", $"{grupo.Cfdis.Count - 20} más en los adjuntos"));

        (string html, string texto) = PlantillaCorreo.Construir(new PlantillaCorreo.Contenido
        {
            Titulo = asunto,
            Parrafos = parrafos,
            Datos = datos
        });

        await correo.EnviarAsync(new MensajeCorreo
        {
            Tipo = "CFDI",
            Para = para,
            Cc = cc,
            Asunto = asunto,
            CuerpoHtml = html,
            CuerpoTexto = texto,
            Adjuntos = adjuntos,
            EnviadoPor = enviadoPor,
            EmpresaId = empresa.EmpresaId
        }, cancelacion: ct);

        // Los adicionales se recuerdan tal como quedaron (vacío los borra).
        repo.GuardarConfiguracionApp(ClaveCorreo(empresa.EmpresaId, grupo.Rfc), string.Join(", ", adicionales));
    }

    /// <summary>
    /// XML con adenda de SINUBE (Emitidos\Adendas) de una factura emitida por la empresa, o null si no tiene.
    /// Si aún no se ha bajado (la tarea corre cada pocas horas), se pide su mes a SINUBE una vez por envío.
    /// </summary>
    private async Task<string?> XmlConAdendaAsync(EmpresaDto empresa, AdminXmlFilaDto x, HashSet<DateTime> revisados, CancellationToken ct)
    {
        if (x.TipoCfdi is "P" or "N" || !string.Equals(x.RfcEmisor.Trim(), empresa.Rfc.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;

        DateTime mes = new(x.FechaEmision.Year, x.FechaEmision.Month, 1);
        string archivo = Path.Combine(rutas.CarpetaAdendasPeriodo(empresa, mes.Year, mes.Month), x.Uuid.Trim().ToUpperInvariant() + ".xml");

        if (!File.Exists(archivo) && revisados.Add(mes) && adendas.TieneSinube(empresa))
        {
            try
            {
                await adendas.DescargarMesAsync(empresa, mes.Year, mes.Month, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Sin SINUBE se manda la copia del SAT.
                log.LogWarning(ex, "No se pudieron revisar las adendas de SINUBE de {Rfc} {Mes:yyyy-MM}", empresa.Rfc, mes);
            }
        }

        return File.Exists(archivo) ? archivo : null;
    }

    private static string Folio(AdminXmlFilaDto x) =>
        string.Join("-", new[] { x.Serie, x.Folio }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } f ? f : x.Uuid[..8];

    private static string Nombre(AdminXmlFilaDto x) =>
        string.Concat($"{x.RfcEmisor}_{Folio(x)}_{x.Uuid}".Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static string Tipo(string t) => t switch
    {
        "I" => "Factura", "E" => "Nota de crédito", "P" => "Pago", "T" => "Traslado", "N" => "Nómina", _ => "CFDI"
    };
}
