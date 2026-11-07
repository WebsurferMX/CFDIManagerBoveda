using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CFDIParser.Models;
using CFDIParser.Services;
using Microsoft.AspNetCore.DataProtection;

namespace CFDIManager.Web.Services.Sinube;

/// <summary>
/// Importación a SINUBE. Reproduce el protocolo de HerramientasSinube 13.9 (analizado con REA):
/// facturas de clientes (frmImportarFacturasDeXmlV2, tipo=5), de proveedores
/// (frmFacturasProveedorXMLV2, tipo=49) y la validación de usuario (validaUsuarioSinube).
/// Los XML se leen de la carpeta de la empresa (&lt;RFC&gt;\Emitidos|Recibidos\Año\Mes) sin moverlos;
/// lo enviado se registra en sinube_envios.
/// </summary>
public class SinubeService
{
    public const string ClienteHttp = "Sinube";

    private const string PrefijoCifrado = "dp:";

    // Los XML de proveedor más grandes se envían sin conceptos (TAMAÑO EXCEDIDO. XML REDUCIDO).
    private const int MaximoCaracteresProveedor = 900_000;

    private const string UrlSifec = "http://ws.si-nube.appspot.com/swactsifec";

    private const string UrlSifecPruebas = "http://facturanube.appspot.com/swactsifec";

    private const string NamespaceSifec = "http://svcSIFEC.server.sifec.neo.com/";

    // ClaveProdServ (combustibles, alcohol, tabaco...) que pueden llevar IEPS (cargaProdIeps).
    private static readonly HashSet<string> ClavesIeps =
    [
        "10191500", "10191506", "10191507", "10191508", "10191509", "10191510", "10191511",
        "15101500", "15101502", "15101503", "15101504", "15101505", "15101506", "15101507",
        "15101508", "15101509", "15101510", "15101511", "15101512", "15101513", "15101600",
        "15101601", "15101602", "15101603", "15101604", "15101605", "15101606", "15101607",
        "15101608", "15101609", "15101610", "15101611", "15101612", "15101613", "15101614",
        "15101700", "15101701", "15101702", "15101800", "15101801", "15111500", "15111501",
        "15111502", "15111503", "15111504", "15111505", "15111506", "15111507", "15111508",
        "15111509", "15111510", "15111511", "48121300", "48121302", "50161500", "50161509",
        "50161511", "50161512", "50161800", "50161813", "50161814", "50161815", "50161900",
        "50181900", "50181905", "50192100", "50192300", "50192301", "50192302", "50192303",
        "50192304", "50192400", "50192401", "50192402", "50192403", "50192404", "50193100",
        "50193101", "50193102", "50201708", "50201712", "50202200", "50202201", "50202207",
        "50202209", "50202300", "50202307", "50202308", "50202309", "50202311", "50211503",
        "50211504", "50221100", "50221101", "50221200", "50221201", "60141113", "80141628",
        "80141700", "80141701", "80141702", "80141703", "80141704", "80141706", "81161700",
        "83111500", "83111511", "83111600", "83111601", "83111602", "83112200", "83112300",
        "83112400", "83112403", "83112404", "83112405", "90151900", "93131607"
    ];

    private readonly PostgreSqlRepository _repo;
    private readonly RutaXmlService _rutas;
    private readonly IHttpClientFactory _http;
    private readonly IDataProtector _protector;
    private readonly ILogger<SinubeService> _logger;

    public SinubeService(
        PostgreSqlRepository repo,
        RutaXmlService rutas,
        IHttpClientFactory http,
        IDataProtectionProvider proveedor,
        ILogger<SinubeService> logger)
    {
        _repo = repo;
        _rutas = rutas;
        _http = http;
        _protector = proveedor.CreateProtector("CFDIManager.Sinube");
        _logger = logger;
    }

    // ───────────── Configuración ─────────────

    /// <summary>La configuración guardada, o una nueva con Empresa = RFC.</summary>
    public SinubeConfiguracionDto ObtenerConfiguracion(EmpresaDto empresa) =>
        _repo.ObtenerConfiguracionSinube(empresa.EmpresaId)
        ?? new SinubeConfiguracionDto
        {
            EmpresaId = empresa.EmpresaId,
            Empresa = empresa.Rfc
        };

    /// <summary>Guarda; si <paramref name="nuevaPassword"/> viene vacía se conserva la anterior.</summary>
    public void GuardarConfiguracion(SinubeConfiguracionDto configuracion, string? nuevaPassword)
    {
        if (!string.IsNullOrEmpty(nuevaPassword))
            configuracion.Password = PrefijoCifrado + _protector.Protect(nuevaPassword);

        _repo.GuardarConfiguracionSinube(configuracion);
    }

    /// <summary>Contraseña en claro; también la usa SinubeDatosService (consultas getpost).</summary>
    internal string Password(SinubeConfiguracionDto configuracion)
    {
        string? guardada = configuracion.Password;

        if (string.IsNullOrEmpty(guardada) || !guardada.StartsWith(PrefijoCifrado))
            return guardada ?? "";

        try
        {
            return _protector.Unprotect(guardada[PrefijoCifrado.Length..]);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new InvalidOperationException(
                "No se pudo leer la contraseña de SINUBE (cambiaron las llaves de cifrado). Captúrala de nuevo en Herramientas.");
        }
    }

    /// <summary>Mismos mensajes que HerramientasSinube cuando faltan datos en Opciones.</summary>
    public static string? FaltanDatos(SinubeConfiguracionDto configuracion, bool requierePassword)
    {
        if (string.IsNullOrWhiteSpace(configuracion.Empresa))
            return "Debe capturar la empresa en Herramientas → Acceso a SINUBE.";

        if (string.IsNullOrWhiteSpace(configuracion.Sucursal))
            return "Debe capturar la sucursal en Herramientas → Acceso a SINUBE.";

        if (requierePassword && string.IsNullOrEmpty(configuracion.Password))
            return "Debe capturar la contraseña para importaciones SINUBE en Herramientas → Acceso a SINUBE.";

        return null;
    }

    private static string UrlBlob(string ambiente) => ambiente switch
    {
        SinubeAmbientes.PruebasFacturanube => "http://ep.facturanube.appspot.com/blob?par=",
        SinubeAmbientes.PruebasSinubeFb => "http://ep.si-nube-fb.appspot.com/blob?par=",
        _ => "http://ep.si-nube.appspot.com/blob?par="
    };

    // ───────────── Acceso ─────────────

    /// <summary>
    /// validaUsuarioSinube(empresa, contraseña) del servicio ActualizacionSIFEC. SINUBE responde
    /// vacío cuando el acceso es correcto o el motivo del rechazo.
    /// </summary>
    public async Task<(bool Correcto, string Mensaje)> ProbarAccesoAsync(
        SinubeConfiguracionDto configuracion,
        CancellationToken cancelacion = default)
    {
        string? falta = FaltanDatos(configuracion, requierePassword: true);

        if (falta is not null)
            return (false, falta);

        bool correcto;
        string mensaje;

        try
        {
            string retorno = await LlamarSifecAsync(
                UrlSifec, "validaUsuarioSinube", cancelacion, configuracion.Empresa, Password(configuracion));

            correcto = string.IsNullOrWhiteSpace(retorno);
            mensaje = correcto ? "Acceso correcto a SINUBE." : retorno.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException or InvalidOperationException)
        {
            correcto = false;
            mensaje = "No se pudo comunicar con SINUBE: " + ex.Message;
        }

        if (configuracion.EmpresaId > 0)
            _repo.RegistrarPruebaSinube(configuracion.EmpresaId, (correcto ? "OK: " : "ERROR: ") + mensaje);

        return (correcto, mensaje);
    }

    /// <summary>
    /// Foliadores (series de facturación) de la sucursal: dameCertificadosFolios(empresa, contraseña,
    /// empresa, sucursal) responde &lt;Respuesta&gt;&lt;Certificados&gt; con un elemento por foliador cuyo
    /// atributo "folio" es el nombre y el otro atributo la llave que se envía como llaveSerFol.
    /// </summary>
    public async Task<List<SinubeFoliadorDto>> ObtenerFoliadoresAsync(
        SinubeConfiguracionDto configuracion,
        CancellationToken cancelacion = default)
    {
        string? falta = FaltanDatos(configuracion, requierePassword: true);

        if (falta is not null)
            throw new InvalidOperationException(falta);

        string url = configuracion.Ambiente == SinubeAmbientes.PruebasFacturanube ? UrlSifecPruebas : UrlSifec;

        string respuesta = await LlamarSifecAsync(
            url,
            "dameCertificadosFolios",
            cancelacion,
            configuracion.Empresa,
            Password(configuracion),
            configuracion.Empresa,
            configuracion.Sucursal);

        if (string.IsNullOrWhiteSpace(respuesta))
            throw new InvalidOperationException("SINUBE no devolvió foliadores (sin datos).");

        if (respuesta.StartsWith("Error"))
            throw new InvalidOperationException(respuesta);

        XElement? certificados = XDocument.Parse(respuesta).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Certificados");

        return (certificados?.Elements() ?? [])
            .Select(e => new SinubeFoliadorDto
            {
                Nombre = e.Attribute("folio")?.Value ?? "",
                Llave = e.Attributes().FirstOrDefault(a => a.Name.LocalName != "folio")?.Value ?? ""
            })
            .Where(f => f.Llave != "")
            .ToList();
    }

    /// <summary>Operación del servicio SOAP ActualizacionSIFEC (JAX-WS, argumentos arg0..argN).</summary>
    private async Task<string> LlamarSifecAsync(
        string url,
        string operacion,
        CancellationToken cancelacion,
        params string[] argumentos)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace tns = NamespaceSifec;

        var sobre = new XDocument(
            new XElement(soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", soap),
                new XAttribute(XNamespace.Xmlns + "tns", tns),
                new XElement(soap + "Body",
                    new XElement(tns + operacion,
                        argumentos.Select((valor, i) => new XElement("arg" + i, valor))))));

        using var contenido = new StringContent(sobre.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        contenido.Headers.Add("SOAPAction", $"\"{NamespaceSifec}ActualizacionSIFEC/{operacion}Request\"");

        using HttpResponseMessage respuesta =
            await _http.CreateClient(ClienteHttp).PostAsync(url, contenido, cancelacion);

        XDocument cuerpo = XDocument.Parse(await respuesta.Content.ReadAsStringAsync(cancelacion));

        if (cuerpo.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring") is XElement falla)
            throw new InvalidOperationException(falla.Value);

        return cuerpo.Descendants().FirstOrDefault(e => e.Name.LocalName == "return")?.Value ?? "";
    }

    // ───────────── Periodos y archivos ─────────────

    /// <summary>Carpetas Año\Mes con XML de Emitidos o Recibidos, del más reciente al más antiguo.</summary>
    public List<DateTime> Periodos(EmpresaDto empresa, bool clientes)
    {
        string raiz = Path.Combine(_rutas.CarpetaEmpresa(empresa), clientes ? "Emitidos" : "Recibidos");

        if (!Directory.Exists(raiz))
            return [];

        var periodos = new List<DateTime>();

        foreach (string carpetaAnio in Directory.GetDirectories(raiz))
        {
            if (!int.TryParse(Path.GetFileName(carpetaAnio), out int anio))
                continue;

            foreach (string carpetaMes in Directory.GetDirectories(carpetaAnio))
            {
                if (int.TryParse(Path.GetFileName(carpetaMes), out int mes) && mes is >= 1 and <= 12)
                    periodos.Add(new DateTime(anio, mes, 1));
            }
        }

        return periodos.OrderDescending().ToList();
    }

    public string CarpetaPeriodo(EmpresaDto empresa, bool clientes, DateTime periodo) =>
        Path.Combine(
            _rutas.CarpetaEmpresa(empresa),
            clientes ? "Emitidos" : "Recibidos",
            periodo.Year.ToString(),
            periodo.Month.ToString("00"));

    public List<string> Archivos(EmpresaDto empresa, bool clientes, DateTime periodo)
    {
        string carpeta = CarpetaPeriodo(empresa, clientes, periodo);

        return Directory.Exists(carpeta)
            ? Directory.GetFiles(carpeta, "*.xml").OrderBy(a => a).ToList()
            : [];
    }

    /// <summary>
    /// Lo que se sube a SINUBE de ese lado: la carpeta del periodo más los traslados propios (emisor = receptor = la
    /// empresa) guardados del otro lado. Esos traslados se registran en SINUBE como cliente y como proveedor.
    /// </summary>
    public List<string> ArchivosSinube(EmpresaDto empresa, bool clientes, DateTime periodo)
    {
        List<string> archivos = Archivos(empresa, clientes, periodo);
        var nombres = archivos.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string ruta in _repo.ObtenerRutasTrasladosPropios(empresa.EmpresaId, guardadosComoEmitidos: !clientes, periodo, periodo.AddMonths(1)))
        {
            if (File.Exists(ruta) && nombres.Add(Path.GetFileName(ruta)))
                archivos.Add(ruta);
        }

        return archivos;
    }

    // ───────────── Envío ─────────────

    /// <summary>
    /// Envía los XML del periodo. Cada resultado se reporta en <paramref name="progreso"/> y se guarda
    /// en sinube_envios. Los UUID ya aceptados se omiten salvo que <paramref name="reenviar"/>.
    /// Con <paramref name="sinFolio"/> las facturas de clientes se importan con el foliador elegido
    /// (comparten historial con las de clientes para no duplicarlas en SINUBE).
    /// </summary>
    /// <summary>UUID ya enviados desde la app (aceptados o que SINUBE dijo que ya existían).</summary>
    public HashSet<string> UuidsEnviados(EmpresaDto empresa, bool clientes) =>
        _repo.ObtenerUuidsEnviadosSinube(empresa.EmpresaId, clientes ? "C" : "P");

    public Task EnviarAsync(
        EmpresaDto empresa,
        SinubeConfiguracionDto configuracion,
        bool clientes,
        DateTime periodo,
        ISet<string>? tiposComprobante,
        bool reenviar,
        bool enviarConAdvertenciaIeps,
        Func<SinubeEnvioDto, int, int, Task> progreso,
        CancellationToken cancelacion,
        SinubeSinFolioDto? sinFolio = null) =>
        EnviarArchivosAsync(empresa, configuracion, clientes, ArchivosSinube(empresa, clientes, periodo), tiposComprobante,
            reenviar, enviarConAdvertenciaIeps, progreso, cancelacion, sinFolio);

    /// <summary>
    /// Como <see cref="EnviarAsync"/> pero con una lista de XML (p. ej. los modificados de XML Azulejeras),
    /// con las mismas validaciones, omisiones y registro en sinube_envios.
    /// </summary>
    public async Task EnviarArchivosAsync(
        EmpresaDto empresa,
        SinubeConfiguracionDto configuracion,
        bool clientes,
        List<string> archivos,
        ISet<string>? tiposComprobante,
        bool reenviar,
        bool enviarConAdvertenciaIeps,
        Func<SinubeEnvioDto, int, int, Task> progreso,
        CancellationToken cancelacion,
        SinubeSinFolioDto? sinFolio = null,
        IReadOnlySet<string>? yaEnSinube = null,
        IReadOnlyDictionary<string, string>? pagosEnSinube = null)
    {
        string? falta = FaltanDatos(configuracion, requierePassword: clientes);
        HashSet<string> uuidsPagosEnSinube = pagosEnSinube is null ? [] : pagosEnSinube.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (falta is not null)
            throw new InvalidOperationException(falta);

        if (sinFolio is not null && string.IsNullOrWhiteSpace(sinFolio.LlaveFoliador))
            throw new InvalidOperationException("Seleccione un foliador.");

        string tipo = clientes ? "C" : "P";
        HashSet<string> enviados = reenviar ? [] : _repo.ObtenerUuidsEnviadosSinube(empresa.EmpresaId, tipo);
        string password = clientes ? Password(configuracion) : "";
        var cacheIeps = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < archivos.Count; i++)
        {
            cancelacion.ThrowIfCancellationRequested();

            string archivo = archivos[i];
            SinubeEnvioDto envio;

            try
            {
                var documento = new XmlDocument();
                documento.Load(archivo);

                envio = clientes
                    ? ValidarCliente(documento, configuracion)
                    : ValidarProveedor(documento);

                envio.EmpresaId = empresa.EmpresaId;
                envio.Tipo = tipo;
                envio.Archivo = Path.GetFileName(archivo);

                if (string.IsNullOrEmpty(envio.Uuid))
                    envio.Uuid = Path.GetFileNameWithoutExtension(archivo).ToUpperInvariant();

                if (clientes && envio.TipoComprobante == "N")
                {
                    envio.Estatus = "NOMINA";
                    envio.Mensaje = "Recibo de nómina: SINUBE no lo importa.";
                    await progreso(envio, i + 1, archivos.Count);
                    continue;
                }

                if (tiposComprobante is not null && !tiposComprobante.Contains(envio.TipoComprobante))
                {
                    envio.Estatus = "OMITIDO";
                    envio.Mensaje = $"Tipo de comprobante {envio.TipoComprobante} no seleccionado.";
                    await progreso(envio, i + 1, archivos.Count);
                    continue;
                }

                if (enviados.Contains(envio.Uuid))
                {
                    envio.Estatus = "OMITIDO";
                    envio.Mensaje = "Ya se había enviado a SINUBE.";
                    await progreso(envio, i + 1, archivos.Count);
                    continue;
                }

                // Consultado antes en SINUBE (p. ej. XML Azulejeras): se registra como existente sin mandarlo.
                if (yaEnSinube?.Contains(envio.Uuid) == true)
                {
                    envio.Estatus = "EXISTE";
                    envio.Mensaje = "Ya está en SINUBE (consultado antes de subir).";
                    _repo.RegistrarEnvioSinube(envio);
                    await progreso(envio, i + 1, archivos.Count);
                    continue;
                }

                // Clientes (facturas, notas y pagos): SINUBE valida el serie-folio, no el UUID. Si otro comprobante ya
                // ocupa ese folio se intenta con las series alternas de la empresa (Herramientas), en orden; el XML de la
                // bóveda no se modifica. Los folios de pagos ocupados conocidos (DbRecepcionPagosXML) se saltan; los demás se
                // descubren por la respuesta de SINUBE.
                List<string>? seriesIntento = null;

                if (clientes && sinFolio is null && configuracion.SeriesAlternasPagos.Count > 0)
                {
                    if (envio.TipoComprobante == "P" && uuidsPagosEnSinube.Contains(envio.Uuid))
                    {
                        envio.Estatus = "EXISTE";
                        envio.Mensaje = "El pago ya está en SINUBE.";
                        _repo.RegistrarEnvioSinube(envio);
                        await progreso(envio, i + 1, archivos.Count);
                        continue;
                    }

                    bool Ocupado(string serie) =>
                        envio.TipoComprobante == "P" && pagosEnSinube?.ContainsKey(PagoSinube.SerieFolio(serie, envio.Folio)) == true;

                    seriesIntento = new[] { envio.Serie }.Concat(configuracion.SeriesAlternasPara(envio.Serie))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Where(s => !Ocupado(s))
                        .ToList();

                    if (seriesIntento.Count == 0)
                    {
                        envio.Estatus = "FOLIO";
                        envio.Mensaje = FolioOcupado(envio, configuracion);
                        _repo.RegistrarEnvioSinube(envio);
                        await progreso(envio, i + 1, archivos.Count);
                        continue;
                    }
                }

                // Sin folio: una factura que ya trae folio solo se importa si se aceptó sustituirlo.
                if (sinFolio is not null && envio.Folio != "" && !sinFolio.SustituirFolio)
                {
                    envio.Estatus = "OMITIDO";
                    envio.Mensaje = "La factura ya tiene SERIE/FOLIO; no se sustituyó por el foliador.";
                    await progreso(envio, i + 1, archivos.Count);
                    continue;
                }

                if (clientes)
                {
                    if (configuracion.ValidarIeps && !enviarConAdvertenciaIeps)
                    {
                        string? ieps = await RevisarIepsAsync(documento, configuracion, password, cacheIeps, cancelacion);

                        if (ieps is not null)
                        {
                            envio.Estatus = "IEPS";
                            envio.Mensaje = "Productos que pueden tener IEPS y existen en SINUBE sin tasa/cuota IEPS: " + ieps;
                            _repo.RegistrarEnvioSinube(envio);
                            await progreso(envio, i + 1, archivos.Count);
                            continue;
                        }
                    }

                    if (seriesIntento is null)
                    {
                        await EnviarClienteAsync(documento, envio, configuracion, password, empresa.Rfc, sinFolio, cancelacion);

                        if (UuidYaExiste(envio))
                            envio.Estatus = "EXISTE";
                    }
                    else
                    {
                        await EnviarPagoConSeriesAsync(documento, envio, seriesIntento, configuracion, password, empresa.Rfc, cancelacion);
                    }
                }
                else
                {
                    await EnviarProveedorAsync(documento, envio, configuracion, cancelacion);

                    // "El comprobante ya existe" en proveedores trae proveedor-serie-folio, no el UUID. Solo con la
                    // revisión previa (yaEnSinube: lo que SINUBE tiene con ese UUID ya se omitió arriba) se sabe que es
                    // otro comprobante del proveedor con el mismo folio, y se reintenta con las series alternas.
                    if (envio.Estatus == "EXISTE" && yaEnSinube is not null &&
                        configuracion.SeriesAlternasProveedoresPara(envio.Serie) is { Count: > 0 } alternas)
                    {
                        await EnviarProveedorConSeriesAsync(documento, envio, alternas, configuracion, cancelacion);
                    }
                }
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                envio = new SinubeEnvioDto
                {
                    EmpresaId = empresa.EmpresaId,
                    Tipo = tipo,
                    Uuid = Path.GetFileNameWithoutExtension(archivo).ToUpperInvariant(),
                    Archivo = Path.GetFileName(archivo),
                    Estatus = ex is XmlException or CfdiInvalidoException ? "INVALIDO" : "ERROR",
                    Mensaje = ex.Message
                };
            }

            _repo.RegistrarEnvioSinube(envio);
            await progreso(envio, i + 1, archivos.Count);
        }
    }

    private sealed class CfdiInvalidoException(string mensaje) : Exception(mensaje);

    private static string Atributo(XmlNode? nodo, string nombre)
    {
        if (nodo?.Attributes is null)
            return "";

        // CFDI 3.2 usa minúsculas (serie, folio, fecha, rfc...); 3.3 y 4.0, mayúscula inicial.
        return nodo.Attributes[nombre]?.Value
            ?? nodo.Attributes[char.ToLowerInvariant(nombre[0]) + nombre[1..]]?.Value
            ?? "";
    }

    private static XmlNode? Hijo(XmlNode? nodo, string nombreLocal) =>
        nodo?.ChildNodes.Cast<XmlNode>().FirstOrDefault(n => n.LocalName == nombreLocal);

    private static XmlNode Comprobante(XmlDocument documento) =>
        documento.DocumentElement is { LocalName: "Comprobante" } raiz
            ? raiz
            : throw new CfdiInvalidoException("El comprobante no es de tipo CFDI/CFD o está mal formado.");

    private static string Uuid(XmlNode comprobante) =>
        Atributo(Hijo(Hijo(comprobante, "Complemento"), "TimbreFiscalDigital"), "UUID").ToUpperInvariant();

    /// <summary>Validaciones de frmImportarFacturasDeXmlV2.comunica.</summary>
    private static SinubeEnvioDto ValidarCliente(XmlDocument documento, SinubeConfiguracionDto configuracion)
    {
        XmlNode comprobante = Comprobante(documento);
        string version = Atributo(comprobante, "Version");

        if (version == "")
            throw new CfdiInvalidoException("No existe el atributo version.");

        if (!DateTime.TryParse(Atributo(comprobante, "Fecha"), out DateTime fecha))
            throw new CfdiInvalidoException("No existe la fecha.");

        XmlNode emisor = Hijo(comprobante, "Emisor") ?? throw new CfdiInvalidoException("El nodo emisor no existe.");
        string rfcEmisor = Atributo(emisor, "Rfc");

        if (rfcEmisor == "")
            throw new CfdiInvalidoException("El rfc emisor no existe.");

        XmlNode receptor = Hijo(comprobante, "Receptor") ?? throw new CfdiInvalidoException("No existe el nodo receptor.");

        if (Hijo(comprobante, "Conceptos") is null)
            throw new CfdiInvalidoException("No existe el nodo Conceptos.");

        XmlNode? complemento = Hijo(comprobante, "Complemento");

        if (version.StartsWith('3') || version.StartsWith('4'))
        {
            if (Hijo(complemento, "TimbreFiscalDigital") is null)
                throw new CfdiInvalidoException("El CFDI no está timbrado.");
        }

        // SINUBE no importa nómina: se identifica para omitirla en vez de contarla como inválida.
        if (Hijo(complemento, "Nomina") is not null || Atributo(comprobante, "TipoDeComprobante") == "N")
        {
            return new SinubeEnvioDto
            {
                Uuid = Uuid(comprobante),
                Serie = Atributo(comprobante, "Serie"),
                Folio = Atributo(comprobante, "Folio"),
                Rfc = Atributo(receptor, "Rfc"),
                Nombre = Atributo(receptor, "Nombre"),
                TipoComprobante = "N",
                FechaCfdi = fecha
            };
        }

        if (!configuracion.Empresa.StartsWith(rfcEmisor, StringComparison.OrdinalIgnoreCase))
            throw new CfdiInvalidoException("El RFC del archivo no coincide con el configurado en Herramientas → Acceso a SINUBE.");

        if (configuracion.ModoProducto is SinubeModosProducto.CodigoSat6 or SinubeModosProducto.CodigoSatComoViene
            && version != "3.3" && version != "4.0")
        {
            throw new CfdiInvalidoException("Las opciones de código producto SAT son solo para CFDI 3.3 o 4.0.");
        }

        return new SinubeEnvioDto
        {
            Uuid = Uuid(comprobante),
            Serie = Atributo(comprobante, "Serie"),
            Folio = Atributo(comprobante, "Folio"),
            Rfc = Atributo(receptor, "Rfc"),
            Nombre = Atributo(receptor, "Nombre"),
            TipoComprobante = Atributo(comprobante, "TipoDeComprobante"),
            FechaCfdi = fecha
        };
    }

    /// <summary>Validaciones de frmFacturasProveedorXMLV2.comunica.</summary>
    private static SinubeEnvioDto ValidarProveedor(XmlDocument documento)
    {
        XmlNode comprobante = Comprobante(documento);
        string version = Atributo(comprobante, "Version");

        if (version == "")
            throw new CfdiInvalidoException("No existe el atributo version.");

        if (version is not ("3.2" or "3.3" or "4.0"))
            throw new CfdiInvalidoException($"Versión no válida ({version}).");

        XmlNode? emisor = Hijo(comprobante, "Emisor");
        string rfcEmisor = Atributo(emisor, "Rfc");

        if (rfcEmisor == "")
            throw new CfdiInvalidoException("No existe el RFC del emisor.");

        if (version != "3.2" && Hijo(Hijo(comprobante, "Complemento"), "TimbreFiscalDigital") is null)
            throw new CfdiInvalidoException("El CFDI no está timbrado.");

        string textoFecha = Atributo(comprobante, "Fecha");

        if (!DateTime.TryParse(textoFecha, out DateTime fecha))
            throw new CfdiInvalidoException("No existe la fecha de la factura.");

        if (Hijo(comprobante, "Conceptos") is null)
            throw new CfdiInvalidoException("No existe el nodo Conceptos.");

        return new SinubeEnvioDto
        {
            Uuid = Uuid(comprobante),
            Serie = Atributo(comprobante, "Serie"),
            Folio = Atributo(comprobante, "Folio"),
            Rfc = rfcEmisor,
            Nombre = Atributo(emisor, "Nombre"),
            TipoComprobante = Atributo(comprobante, "TipoDeComprobante") switch
            {
                "ingreso" => "I",
                "egreso" => "E",
                "traslado" => "T",
                var t => t
            },
            FechaCfdi = fecha
        };
    }

    /// <summary>
    /// Conceptos con ClaveProdServ de IEPS cuyo NoIdentificacion existe en SINUBE sin tasa IEPS
    /// (tipo=6 responde "0"). Null si no hay ninguno.
    /// </summary>
    private async Task<string?> RevisarIepsAsync(
        XmlDocument documento,
        SinubeConfiguracionDto configuracion,
        string password,
        Dictionary<string, bool> cache,
        CancellationToken cancelacion)
    {
        XmlNode comprobante = Comprobante(documento);
        string version = Atributo(comprobante, "Version");

        if (version != "3.3" && version != "4.0")
            return null;

        var avisos = new List<string>();

        foreach (XmlNode concepto in Hijo(comprobante, "Conceptos")!.ChildNodes)
        {
            string clave = Atributo(concepto, "ClaveProdServ");
            string noIdentificacion = Atributo(concepto, "NoIdentificacion");

            if (noIdentificacion == "" || !ClavesIeps.Contains(clave))
                continue;

            if (!cache.TryGetValue(noIdentificacion, out bool sinIeps))
            {
                string parametros =
                    "tipo=6\n" +
                    "emp=" + configuracion.Empresa + "\n" +
                    "pwd=" + password + "\n" +
                    "prod=" + noIdentificacion;

                sinIeps = (await PostAsync(configuracion.Ambiente, parametros, "", cancelacion)).Trim() == "0";
                cache[noIdentificacion] = sinIeps;
            }

            if (sinIeps)
                avisos.Add($"NoIdentificacion [{noIdentificacion}] ClaveProdServ [{clave}]");
        }

        return avisos.Count > 0 ? string.Join(", ", avisos) : null;
    }

    /// <summary>
    /// Envía un pago de clientes probando sus series en orden (la original y las alternas con el folio libre):
    /// si SINUBE responde que ya existe un depósito con ese serie-folio, intenta con la siguiente.
    /// </summary>
    private async Task EnviarPagoConSeriesAsync(
        XmlDocument documento,
        SinubeEnvioDto envio,
        List<string> series,
        SinubeConfiguracionDto configuracion,
        string password,
        string rfcEmpresa,
        CancellationToken cancelacion)
    {
        string original = $"{envio.Serie}-{envio.Folio}";
        string serieOriginal = envio.Serie;

        foreach (string serie in series)
        {
            ((XmlElement)Comprobante(documento)).SetAttribute("Serie", serie);
            envio.Serie = serie;

            await EnviarClienteAsync(documento, envio, configuracion, password, rfcEmpresa, null, cancelacion);

            // El que ya existe es el UUID: el pago ya está en SINUBE, no se reintenta con otra serie.
            if (UuidYaExiste(envio))
            {
                envio.Estatus = "EXISTE";
                envio.Serie = serieOriginal;
                return;
            }

            if (envio.Estatus == "OK" || !EsDepositoDuplicado(envio.Mensaje))
                break;
        }

        if (envio.Estatus == "OK" && !string.Equals(envio.Serie, serieOriginal, StringComparison.OrdinalIgnoreCase))
        {
            envio.Mensaje = $"Serie cambiada: {original} ya existe en SINUBE, se subió como {envio.Serie}-{envio.Folio}. " + envio.Mensaje;
        }
        else if (envio.Estatus != "OK" && EsDepositoDuplicado(envio.Mensaje))
        {
            envio.Estatus = "FOLIO";
            envio.Mensaje = FolioOcupado(envio, configuracion);
            envio.Serie = serieOriginal;
        }
    }

    /// <summary>
    /// Proveedores: el folio ya lo ocupa otro comprobante del proveedor en SINUBE. Se manda con cada serie alterna en
    /// orden hasta que SINUBE la acepte; el XML de la bóveda no cambia (solo la copia que se envía).
    /// </summary>
    private async Task EnviarProveedorConSeriesAsync(
        XmlDocument documento,
        SinubeEnvioDto envio,
        List<string> series,
        SinubeConfiguracionDto configuracion,
        CancellationToken cancelacion)
    {
        string serieOriginal = envio.Serie;
        string original = $"{serieOriginal}-{envio.Folio}".TrimStart('-');

        foreach (string serie in series)
        {
            ((XmlElement)Comprobante(documento)).SetAttribute("Serie", serie);
            envio.Serie = serie;

            await EnviarProveedorAsync(documento, envio, configuracion, cancelacion);

            if (envio.Estatus != "EXISTE")
                break;
        }

        if (envio.Estatus == "OK")
        {
            envio.Mensaje = $"Serie cambiada: {original} ya existe en SINUBE, se subió como {envio.Serie}-{envio.Folio}. " + envio.Mensaje;
        }
        else if (envio.Estatus == "EXISTE")
        {
            // Ocupado también con las alternas: no es el mismo UUID (ya se revisó), así que no cuenta como subido.
            envio.Estatus = "FOLIO";
            envio.Mensaje = $"El folio {original} ya lo tiene otro comprobante del proveedor en SINUBE, también con " +
                            $"{string.Join(", ", series)}. Agrega otra serie alterna de proveedores en Herramientas.";
            envio.Serie = serieOriginal;
        }
        else
        {
            envio.Serie = serieOriginal;
        }
    }

    // "PASO 8.4. El CFDI 'RFC-SERIE-FOLIO' ya existe en la sucursal [Matriz] como Depósito." (o "como factura":
    // pagos y facturas sin serie comparten la numeración).
    private static bool EsDepositoDuplicado(string? mensaje) =>
        mensaje is not null && mensaje.Contains("ya existe en", StringComparison.OrdinalIgnoreCase) &&
        (mensaje.Contains("como Dep", StringComparison.OrdinalIgnoreCase) || mensaje.Contains("como factura", StringComparison.OrdinalIgnoreCase));

    // "PASO 8.4. El CFDI 'RFC-uuid' ya existe en alguna de sus sucursales como Depósito.": el mismo pago ya está.
    private static bool UuidYaExiste(SinubeEnvioDto envio) =>
        envio.Estatus != "OK" && EsDepositoDuplicado(envio.Mensaje) &&
        envio.Uuid != "" && envio.Mensaje.Contains(envio.Uuid, StringComparison.OrdinalIgnoreCase);

    private static string FolioOcupado(SinubeEnvioDto envio, SinubeConfiguracionDto configuracion)
    {
        List<string> alternas = configuracion.SeriesAlternasPara(envio.Serie);
        return alternas.Count == 0
            ? $"El folio {envio.Serie}-{envio.Folio} ya lo tiene otro comprobante en SINUBE. Configura las series alternas en Herramientas."
            : $"El folio {envio.Folio} ya lo tiene otro comprobante en SINUBE con la serie original y con {string.Join(", ", alternas)}. Agrega otra serie alterna en Herramientas.";
    }

    private async Task EnviarClienteAsync(
        XmlDocument documento,
        SinubeEnvioDto envio,
        SinubeConfiguracionDto configuracion,
        string password,
        string rfcEmpresa,
        SinubeSinFolioDto? sinFolio,
        CancellationToken cancelacion)
    {
        bool esRespaldo = configuracion.Empresa.Length > rfcEmpresa.Length;
        string modo = configuracion.ModoProducto;

        string comunes =
            "tipo=5\n" +
            "suc=" + QuitaCaracteresHttp(configuracion.Sucursal) + "\n" +
            "pwd=" + QuitaCaracteresHttp(password) + "\n" +
            "difzh=" + DiferenciaHoraria() + "\n" +
            "empresa=" + configuracion.Empresa + "\n" +
            "esRespaldo=" + Bandera(esRespaldo) + "\n" +
            "omiteNoId=" + Bandera(modo == SinubeModosProducto.OmitirNoIdentificacion) + "\n" +
            "usarCodSat6Pos=" + Bandera(modo == SinubeModosProducto.CodigoSat6) + "\n";

        // Mismo orden que frmImportarFacturasDeXmlV2 y frmImportarFacturasDeXmlsfV2 (sin folio).
        string parametros = sinFolio is null
            ? comunes +
              "serieFolioDeFolio=" + Bandera(configuracion.SerieFolioDeFolio) + "\n" +
              "agruparxcodsat=" + Bandera(configuracion.AgruparCodigoSat) + "\n" +
              "usarCodSatComoVieneCfdi=" + Bandera(modo == SinubeModosProducto.CodigoSatComoViene) + "\n" +
              "usarProdCtes=" + Bandera(configuracion.UsarProductoClientes) + "\n" +
              "taskqueue=1"
            : comunes +
              "usarCodSatComoVieneCfdi=" + Bandera(modo == SinubeModosProducto.CodigoSatComoViene) + "\n" +
              "agruparxcodsat=" + Bandera(configuracion.AgruparCodigoSat) + "\n" +
              "llaveSerFol=" + sinFolio.LlaveFoliador + "\n" +
              "usarProdCtes=" + Bandera(configuracion.UsarProductoClientes) + "\n" +
              "taskqueue=1";

        string xml = TextoXml(documento);
        string respuesta = "";

        // XML grande: SINUBE lo procesa en segundo plano y responde "...[TQ]"; se consulta
        // de nuevo cada 5 s hasta 9 veces.
        for (int consulta = 1; consulta <= 9; consulta++)
        {
            respuesta = await PostConReintentosAsync(configuracion.Ambiente, parametros, xml, cancelacion);

            if (!respuesta.EndsWith("[TQ]"))
                break;

            await Task.Delay(TimeSpan.FromSeconds(5), cancelacion);
        }

        if (respuesta.StartsWith("SINUBE_OK"))
        {
            envio.Estatus = "OK";
            envio.Mensaje = respuesta;
        }
        else
        {
            envio.Estatus = "ERROR";
            envio.Mensaje = respuesta.EndsWith("[TQ]")
                ? "SINUBE sigue procesando el XML en segundo plano; revisa en SINUBE antes de reenviarlo."
                : respuesta;
        }
    }

    private async Task EnviarProveedorAsync(
        XmlDocument documento,
        SinubeEnvioDto envio,
        SinubeConfiguracionDto configuracion,
        CancellationToken cancelacion)
    {
        bool reducido = false;

        if (documento.OuterXml.Length > MaximoCaracteresProveedor)
        {
            Hijo(Comprobante(documento), "Conceptos")!.RemoveAll();
            reducido = true;
        }

        string parametros =
            "tipo=49\n" +
            "tkn=_herramsin\n" +
            "emp=" + configuracion.Empresa + "\n" +
            "suc=" + QuitaCaracteresHttp(configuracion.Sucursal) + "\n" +
            "usr=" + configuracion.Empresa + "\n" +
            "difzh=" + DiferenciaHoraria();

        string respuesta = await PostConReintentosAsync(configuracion.Ambiente, parametros, TextoXml(documento), cancelacion);

        if (respuesta.StartsWith("0_"))
        {
            envio.Estatus = "OK";
            envio.Mensaje = "CORRECTO" + (reducido ? " (TAMAÑO EXCEDIDO. XML REDUCIDO)" : "");
        }
        else if (respuesta.Contains("El comprobante ya existe"))
        {
            envio.Estatus = "EXISTE";
            envio.Mensaje = respuesta;
        }
        else
        {
            envio.Estatus = "ERROR";
            envio.Mensaje = respuesta;
        }
    }

    /// <summary>Hasta 4 intentos si falla la comunicación (igual que la herramienta original).</summary>
    private async Task<string> PostConReintentosAsync(
        string ambiente,
        string parametros,
        string xml,
        CancellationToken cancelacion)
    {
        for (int intento = 1; ; intento++)
        {
            try
            {
                return await PostAsync(ambiente, parametros, xml, cancelacion);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancelacion.IsCancellationRequested)
            {
                if (intento >= 4)
                {
                    throw new InvalidOperationException(
                        "Ocurrió un error al establecer la comunicación con el servidor de SINUBE. Se hicieron 4 intentos: " + ex.Message);
                }

                _logger.LogWarning(ex, "SINUBE: intento {Intento} fallido.", intento);
            }
        }
    }

    /// <summary>
    /// ClsUtilerias.doPOST: POST text/xml a blob?par=Base64(parámetros) con el XML en el cuerpo.
    /// </summary>
    private async Task<string> PostAsync(
        string ambiente,
        string parametros,
        string xml,
        CancellationToken cancelacion)
    {
        string url = UrlBlob(ambiente) + Convert.ToBase64String(Encoding.UTF8.GetBytes(parametros));

        using var contenido = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
        contenido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/xml");

        using HttpResponseMessage respuesta =
            await _http.CreateClient(ClienteHttp).PostAsync(url, contenido, cancelacion);

        string texto = await respuesta.Content.ReadAsStringAsync(cancelacion);

        if (!respuesta.IsSuccessStatusCode && string.IsNullOrWhiteSpace(texto))
            throw new HttpRequestException($"SINUBE respondió {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}.");

        return texto;
    }

    /// <summary>El XML como lo guarda XmlDocument.Save, con líneas separadas por CRLF.</summary>
    private static string TextoXml(XmlDocument documento)
    {
        using var memoria = new MemoryStream();
        documento.Save(memoria);
        memoria.Position = 0;

        using var lector = new StreamReader(memoria, Encoding.UTF8);
        var texto = new StringBuilder();

        while (lector.ReadLine() is string linea)
            texto.Append(linea).Append("\r\n");

        return texto.ToString();
    }

    private static string Bandera(bool valor) => valor ? "1" : "0";

    /// <summary>Diferencia con UTC en horas, formato "zz" (p. ej. -06).</summary>
    private static string DiferenciaHoraria() => DateTime.Now.ToString("zz");

    /// <summary>Acentos y ñ como entidades HTML (ClsUtilerias.quitaCaracteresHTTP).</summary>
    private static string QuitaCaracteresHttp(string texto) =>
        texto
            .Replace("á", "&aacute;").Replace("é", "&eacute;").Replace("í", "&iacute;")
            .Replace("ó", "&oacute;").Replace("ú", "&uacute;").Replace("ñ", "&ntilde;")
            .Replace("Á", "&Aacute;").Replace("É", "&Eacute;").Replace("Í", "&Iacute;")
            .Replace("Ó", "&Oacute;").Replace("Ú", "&Uacute;").Replace("Ñ", "&Ntilde;");

    // ───────────── Herramientas: XML renombrados ─────────────

    /// <summary>
    /// ZIP con copias de los XML del periodo nombradas según los criterios de la pestaña
    /// "Descarga XMLs del SAT". La carpeta de la empresa no se modifica.
    /// </summary>
    public (byte[] Contenido, string NombreArchivo, int Archivos) GenerarZipRenombrado(
        EmpresaDto empresa,
        SinubeConfiguracionDto configuracion,
        bool emitidos,
        DateTime periodo)
    {
        List<string> archivos = Archivos(empresa, emitidos, periodo);
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var memoria = new MemoryStream();

        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string archivo in archivos)
            {
                string nombre;

                try
                {
                    nombre = NombreXml(XDocument.Load(archivo), configuracion, emitidos);
                }
                catch (XmlException)
                {
                    nombre = Path.GetFileNameWithoutExtension(archivo);
                }

                string unico = nombre;

                for (int n = 2; !usados.Add(unico); n++)
                    unico = $"{nombre}_{n}";

                zip.CreateEntryFromFile(archivo, unico + ".xml", CompressionLevel.Optimal);
            }
        }

        string tipo = emitidos ? "Emitidos" : "Recibidos";

        return (memoria.ToArray(), $"{empresa.Rfc}_{tipo}_{periodo:yyyy-MM}.zip", archivos.Count);
    }

    /// <summary>Nombre del XML según criterio, emisor/receptor por RFC o razón social, UUID y tipo.</summary>
    public static string NombreXml(XDocument xml, SinubeConfiguracionDto configuracion, bool emitido)
    {
        XElement comprobante = xml.Root!;

        string Valor(XElement? elemento, string nombre) =>
            elemento?.Attribute(nombre)?.Value
            ?? elemento?.Attribute(char.ToLowerInvariant(nombre[0]) + nombre[1..])?.Value
            ?? "";

        XElement? Nodo(XElement? padre, string local) =>
            padre?.Elements().FirstOrDefault(e => e.Name.LocalName == local);

        // Emitidos: se identifica al receptor (cliente); recibidos: al emisor (proveedor).
        XElement? contraparte = Nodo(comprobante, emitido ? "Receptor" : "Emisor");

        string persona = configuracion.EmisorReceptor == "RAZON_SOCIAL"
            ? Valor(contraparte, "Nombre")
            : Valor(contraparte, "Rfc");

        string fecha = DateTime.TryParse(Valor(comprobante, "Fecha"), out DateTime f) ? f.ToString("yyyy-MM-dd") : "";
        string serieFolio = string.Join("-", new[] { Valor(comprobante, "Serie"), Valor(comprobante, "Folio") }.Where(v => v != ""));
        string uuid = configuracion.IncluirUuid
            ? Valor(Nodo(Nodo(comprobante, "Complemento"), "TimbreFiscalDigital"), "UUID").ToUpperInvariant()
            : "";

        string[] partes = configuracion.CriterioNombre switch
        {
            2 => [fecha, persona, serieFolio, uuid],
            3 => [fecha, serieFolio, persona, uuid],
            _ => [persona, fecha, serieFolio, uuid]
        };

        if (configuracion.IncluirTipo)
        {
            string tipo = Valor(comprobante, "TipoDeComprobante").ToUpperInvariant() switch
            {
                "I" or "INGRESO" => "INGRESO",
                "E" or "EGRESO" => "EGRESO",
                "T" or "TRASLADO" => "TRASLADO",
                "P" => "PAGO",
                "N" => "NOMINA",
                var t => t
            };

            partes = [.. partes, tipo];
        }

        string nombre = string.Join("-", partes.Where(p => !string.IsNullOrWhiteSpace(p)));

        foreach (char invalido in Path.GetInvalidFileNameChars())
            nombre = nombre.Replace(invalido, '_');

        nombre = nombre.Trim();

        if (nombre.Length > 180)
            nombre = nombre[..180];

        return nombre == "" ? "SIN_NOMBRE" : nombre;
    }
}
