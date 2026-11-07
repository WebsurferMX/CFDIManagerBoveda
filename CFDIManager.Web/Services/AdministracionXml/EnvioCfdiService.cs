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

    public bool Enviar { get; set; } = true;

    /// <summary>Resultado del envío: "" sin enviar, "Enviado" o el error.</summary>
    public string Resultado { get; set; } = "";

    public decimal Total => Cfdis.Sum(x => x.TipoCfdi == "P" ? x.MontoTotalPagos ?? 0 : x.Total);
}

/// <summary>
/// "Enviar CFDI" de Administración de XML: manda a cada contraparte el PDF (formato SINUBE) y el XML de sus comprobantes.
/// El correo propuesto es el del catálogo de clientes de SINUBE o, si no tiene, el último que se capturó para ese RFC.
/// </summary>
public class EnvioCfdiService(
    PostgreSqlRepository repo,
    XmlCfdiParser parser,
    RepresentacionImpresaService pdf,
    CorreoService correo,
    SinubeService sinube,
    SinubeDatosService datosSinube)
{
    private static string ClaveCorreo(long empresaId, string rfc) => $"correo_cfdi:{empresaId}:{rfc.Trim().ToUpperInvariant()}";

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

        foreach (GrupoEnvioCfdi g in grupos)
        {
            // Primero el correo del catálogo de clientes de SINUBE (público en general no está ahí).
            if (conSinube && g.Rfc is not ("XAXX010101000" or "XEXX010101000"))
            {
                try
                {
                    if (await datosSinube.ClienteAsync(config, g.Rfc, ct) is { } cliente)
                    {
                        g.NombreComercial = cliente.NombreComercial;
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

    /// <summary>Envía el PDF y el XML de cada CFDI del grupo y recuerda el correo (se usa si SINUBE no tiene uno).</summary>
    public async Task EnviarAsync(
        EmpresaDto empresa, GrupoEnvioCfdi grupo, IReadOnlyList<string> cc, string mensaje, string enviadoPor, CancellationToken ct = default)
    {
        List<string> adicionales = CorreoService.SepararCorreos(grupo.Adicionales);
        List<string> para = CorreoService.SepararCorreos(grupo.Para)
            .Concat(adicionales)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (para.Count == 0)
            throw new InvalidOperationException("Escribe al menos un correo.");

        var adjuntos = new List<AdjuntoCorreo>();
        var cliente = new DatosClientePdf(grupo.NombreComercial);

        foreach (AdminXmlFilaDto x in grupo.Cfdis)
        {
            string ruta = RutaXmlLocal.Resolver(x.RutaXml);
            if (!File.Exists(ruta))
                throw new FileNotFoundException($"No se encontró el XML de {Nombre(x)}.");

            string nombre = Nombre(x);
            byte[] xml = await File.ReadAllBytesAsync(ruta, ct);
            byte[] archivoPdf = await Task.Run(() => pdf.Generar(parser.Parse(ruta), cliente), ct);
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

    private static string Folio(AdminXmlFilaDto x) =>
        string.Join("-", new[] { x.Serie, x.Folio }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } f ? f : x.Uuid[..8];

    private static string Nombre(AdminXmlFilaDto x) =>
        string.Concat($"{x.RfcEmisor}_{Folio(x)}_{x.Uuid}".Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static string Tipo(string t) => t switch
    {
        "I" => "Factura", "E" => "Nota de crédito", "P" => "Pago", "T" => "Traslado", "N" => "Nómina", _ => "CFDI"
    };
}
