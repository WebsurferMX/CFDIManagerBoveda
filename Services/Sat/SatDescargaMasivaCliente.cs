using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace CFDIManager.Web.Services.Sat;

/// <summary>
/// Cliente del servicio web de Descarga Masiva del SAT (CFDI regulares).
/// Mismo protocolo que la librería NodeCfdi usada en Mi Bóveda CFDI:
/// autenticación con token, solicitud, verificación y descarga de paquetes,
/// todo firmado con la e.firma.
/// </summary>
public sealed class SatDescargaMasivaCliente
{
    private const string UrlAutenticacion =
        "https://cfdidescargamasivasolicitud.clouda.sat.gob.mx/Autenticacion/Autenticacion.svc";

    private const string UrlSolicitud =
        "https://cfdidescargamasivasolicitud.clouda.sat.gob.mx/SolicitaDescargaService.svc";

    private const string UrlVerificacion =
        "https://cfdidescargamasivasolicitud.clouda.sat.gob.mx/VerificaSolicitudDescargaService.svc";

    private const string UrlDescarga =
        "https://cfdidescargamasiva.clouda.sat.gob.mx/DescargaMasivaService.svc";

    private const string NsDescarga =
        "http://DescargaMasivaTerceros.sat.gob.mx";

    private const string NsUtilidad =
        "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

    private const string TipoX509 =
        "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3";

    private readonly FielSat _fiel;
    private readonly HttpClient _http;

    private string _token =
        "";

    private DateTime _tokenExpiraUtc;

    public SatDescargaMasivaCliente(
        FielSat fiel,
        HttpClient http)
    {
        _fiel =
            fiel;

        _http =
            http;
    }

    /// <summary>Obtiene un token nuevo; sirve también para probar la conexión.</summary>
    public async Task<DateTime> AutenticarAsync(
        CancellationToken cancelacion = default)
    {
        DateTime creado =
            DateTime.UtcNow;

        DateTime expira =
            creado.AddMinutes(5);

        string idToken =
            CrearIdToken();

        string timestamp =
            $"<u:Created>{FormatoSat(creado)}</u:Created><u:Expires>{FormatoSat(expira)}</u:Expires>";

        string keyInfo =
            $"<KeyInfo><o:SecurityTokenReference><o:Reference URI=\"#{idToken}\" ValueType=\"{TipoX509}\"/></o:SecurityTokenReference></KeyInfo>";

        string firma =
            CrearFirma(
                $"<u:Timestamp xmlns:u=\"{NsUtilidad}\" u:Id=\"_0\">{timestamp}</u:Timestamp>",
                "#_0",
                keyInfo);

        string sobre =
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
            $"xmlns:u=\"{NsUtilidad}\">" +
            "<s:Header>" +
            "<o:Security xmlns:o=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\" s:mustUnderstand=\"1\">" +
            $"<u:Timestamp u:Id=\"_0\">{timestamp}</u:Timestamp>" +
            $"<o:BinarySecurityToken u:Id=\"{idToken}\" ValueType=\"{TipoX509}\" " +
            "EncodingType=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary\">" +
            _fiel.CertificadoBase64 +
            "</o:BinarySecurityToken>" +
            firma +
            "</o:Security>" +
            "</s:Header>" +
            "<s:Body><Autentica xmlns=\"http://DescargaMasivaTerceros.gob.mx\"/></s:Body>" +
            "</s:Envelope>";

        XElement respuesta =
            await EnviarAsync(
                UrlAutenticacion,
                "http://DescargaMasivaTerceros.gob.mx/IAutenticacion/Autentica",
                sobre,
                usarToken: false,
                cancelacion);

        string token =
            Buscar(respuesta, "Body", "AutenticaResponse", "AutenticaResult")?.Value ?? "";

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "El SAT no devolvió un token de autenticación.");
        }

        _token =
            token;

        _tokenExpiraUtc =
            expira;

        return expira.ToLocalTime();
    }

    public async Task<ResultadoSolicitudSat> SolicitarAsync(
        ParametrosSolicitudSat p,
        CancellationToken cancelacion = default)
    {
        string rfc =
            _fiel.Rfc;

        bool recibidos =
            p.Recibidos;

        string nodo =
            recibidos
                ? "SolicitaDescargaRecibidos"
                : "SolicitaDescargaEmitidos";

        // El SAT no entrega XML recibidos cancelados: esas solicitudes se piden solo vigentes.
        string estado =
            recibidos && !p.Metadatos
                ? "Vigente"
                : "Todos";

        var atributos =
            new SortedDictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["RfcSolicitante"] = rfc,
                ["TipoSolicitud"] = p.Metadatos ? "Metadata" : "CFDI",
                ["FechaInicial"] = p.FechaInicial.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                ["FechaFinal"] = p.FechaFinal.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                ["EstadoComprobante"] = estado,
            };

        if (!string.IsNullOrWhiteSpace(p.TipoComprobante))
        {
            atributos["TipoComprobante"] =
                p.TipoComprobante;
        }

        string extra =
            "";

        string contraparte =
            (p.RfcContraparte ?? "").Trim().ToUpperInvariant();

        if (recibidos)
        {
            atributos["RfcReceptor"] =
                rfc;

            if (contraparte != "")
            {
                atributos["RfcEmisor"] =
                    contraparte;
            }
        }
        else
        {
            atributos["RfcEmisor"] =
                rfc;

            if (contraparte != "")
            {
                extra =
                    $"<des:RfcReceptores><des:RfcReceptor>{Escapar(contraparte)}</des:RfcReceptor></des:RfcReceptores>";
            }
        }

        XElement resultado =
            await EnviarFirmadoAsync(
                UrlSolicitud,
                $"{NsDescarga}/ISolicitaDescargaService/{nodo}",
                nodo,
                atributos,
                extra,
                cancelacion);

        return new ResultadoSolicitudSat(
            Entero(resultado, "CodEstatus"),
            Atributo(resultado, "Mensaje"),
            Atributo(resultado, "IdSolicitud"));
    }

    /// <summary>Solicitud de un solo CFDI por su UUID (folio fiscal).</summary>
    public async Task<ResultadoSolicitudSat> SolicitarPorUuidAsync(
        string uuid,
        CancellationToken cancelacion = default)
    {
        var atributos =
            new SortedDictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["Folio"] = uuid.Trim(),
                ["RfcSolicitante"] = _fiel.Rfc,
            };

        XElement resultado =
            await EnviarFirmadoAsync(
                UrlSolicitud,
                $"{NsDescarga}/ISolicitaDescargaService/SolicitaDescargaFolio",
                "SolicitaDescargaFolio",
                atributos,
                "",
                cancelacion);

        return new ResultadoSolicitudSat(
            Entero(resultado, "CodEstatus"),
            Atributo(resultado, "Mensaje"),
            Atributo(resultado, "IdSolicitud"));
    }

    public async Task<ResultadoVerificacionSat> VerificarAsync(
        string idSolicitud,
        CancellationToken cancelacion = default)
    {
        var atributos =
            new SortedDictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["IdSolicitud"] = idSolicitud,
                ["RfcSolicitante"] = _fiel.Rfc,
            };

        XElement resultado =
            await EnviarFirmadoAsync(
                UrlVerificacion,
                $"{NsDescarga}/IVerificaSolicitudDescargaService/VerificaSolicitudDescarga",
                "VerificaSolicitudDescarga",
                atributos,
                "",
                cancelacion);

        List<string> paquetes =
            resultado.Elements()
                .Where(e => e.Name.LocalName.Equals("IdsPaquetes", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value.Trim())
                .Where(v => v != "")
                .ToList();

        return new ResultadoVerificacionSat(
            Entero(resultado, "CodEstatus"),
            Atributo(resultado, "Mensaje"),
            Entero(resultado, "EstadoSolicitud"),
            Entero(resultado, "CodigoEstadoSolicitud"),
            Entero(resultado, "NumeroCFDIs"),
            paquetes);
    }

    public async Task<ResultadoDescargaSat> DescargarPaqueteAsync(
        string idPaquete,
        CancellationToken cancelacion = default)
    {
        var atributos =
            new SortedDictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["IdPaquete"] = idPaquete,
                ["RfcSolicitante"] = _fiel.Rfc,
            };

        XElement sobre =
            await EnviarFirmadoAsync(
                UrlDescarga,
                $"{NsDescarga}/IDescargaMasivaTercerosService/Descargar",
                "PeticionDescargaMasivaTercerosEntrada",
                atributos,
                "",
                cancelacion,
                nodoSolicitud: "peticionDescarga",
                devolverSobre: true);

        XElement? respuesta =
            Buscar(sobre, "Header", "respuesta");

        string paquete =
            Buscar(sobre, "Body", "RespuestaDescargaMasivaTercerosSalida", "Paquete")?.Value ?? "";

        return new ResultadoDescargaSat(
            respuesta is null ? 0 : Entero(respuesta, "CodEstatus"),
            respuesta is null ? "" : Atributo(respuesta, "Mensaje"),
            paquete == "" ? [] : Convert.FromBase64String(paquete));
    }

    private async Task<XElement> EnviarFirmadoAsync(
        string url,
        string soapAction,
        string nodo,
        SortedDictionary<string, string> atributos,
        string extra,
        CancellationToken cancelacion,
        string nodoSolicitud = "solicitud",
        bool devolverSobre = false)
    {
        string textoAtributos =
            string.Join(
                " ",
                atributos.Select(a => $"{a.Key}=\"{Escapar(a.Value)}\""));

        string firma =
            CrearFirma(
                $"<des:{nodo} xmlns:des=\"{NsDescarga}\"><des:{nodoSolicitud} {textoAtributos}>{extra}</des:{nodoSolicitud}></des:{nodo}>");

        string sobre =
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
            $"xmlns:des=\"{NsDescarga}\" xmlns:xd=\"http://www.w3.org/2000/09/xmldsig#\">" +
            "<s:Header/>" +
            $"<s:Body><des:{nodo}><des:{nodoSolicitud} {textoAtributos}>{extra}{firma}</des:{nodoSolicitud}></des:{nodo}></s:Body>" +
            "</s:Envelope>";

        XElement respuesta =
            await EnviarAsync(
                url,
                soapAction,
                sobre,
                usarToken: true,
                cancelacion);

        if (devolverSobre)
        {
            return respuesta;
        }

        // La respuesta trae <XxxResponse><XxxResult atributos...>.
        return Buscar(respuesta, "Body")?
                   .Elements().FirstOrDefault()?
                   .Elements().FirstOrDefault()
               ?? throw new InvalidOperationException(
                   "Respuesta inesperada del SAT.");
    }

    private async Task<XElement> EnviarAsync(
        string url,
        string soapAction,
        string sobre,
        bool usarToken,
        CancellationToken cancelacion)
    {
        if (usarToken && (_token == "" || DateTime.UtcNow >= _tokenExpiraUtc.AddSeconds(-20)))
        {
            await AutenticarAsync(cancelacion);
        }

        using var solicitud =
            new HttpRequestMessage(
                HttpMethod.Post,
                url)
            {
                Content =
                    new StringContent(
                        sobre,
                        Encoding.UTF8,
                        "text/xml")
            };

        solicitud.Headers.Add(
            "SOAPAction",
            soapAction);

        solicitud.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/xml"));

        if (usarToken)
        {
            solicitud.Headers.TryAddWithoutValidation(
                "Authorization",
                $"WRAP access_token=\"{_token}\"");
        }

        using HttpResponseMessage respuesta =
            await _http.SendAsync(
                solicitud,
                cancelacion);

        string contenido =
            await respuesta.Content.ReadAsStringAsync(
                cancelacion);

        XElement sobreRespuesta;

        try
        {
            sobreRespuesta =
                XElement.Parse(
                    contenido);
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                $"El SAT respondió HTTP {(int)respuesta.StatusCode} sin un XML válido.");
        }

        XElement? falla =
            Buscar(sobreRespuesta, "Body", "Fault");

        if (falla is not null)
        {
            string detalle =
                falla.Elements().FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value
                ?? falla.Value;

            throw new InvalidOperationException(
                $"El SAT rechazó la petición: {detalle}");
        }

        return sobreRespuesta;
    }

    private string CrearFirma(
        string porDigerir,
        string uri = "",
        string keyInfo = "")
    {
        string digesto =
            Convert.ToBase64String(
                SHA1.HashData(
                    Encoding.UTF8.GetBytes(
                        porDigerir)));

        string signedInfo =
            "<SignedInfo xmlns=\"http://www.w3.org/2000/09/xmldsig#\">" +
            "<CanonicalizationMethod Algorithm=\"http://www.w3.org/2001/10/xml-exc-c14n#\"></CanonicalizationMethod>" +
            "<SignatureMethod Algorithm=\"http://www.w3.org/2000/09/xmldsig#rsa-sha1\"></SignatureMethod>" +
            $"<Reference URI=\"{uri}\">" +
            "<Transforms><Transform Algorithm=\"http://www.w3.org/2001/10/xml-exc-c14n#\"></Transform></Transforms>" +
            "<DigestMethod Algorithm=\"http://www.w3.org/2000/09/xmldsig#sha1\"></DigestMethod>" +
            $"<DigestValue>{digesto}</DigestValue>" +
            "</Reference>" +
            "</SignedInfo>";

        string valorFirma =
            _fiel.Firmar(
                signedInfo);

        if (keyInfo == "")
        {
            keyInfo =
                "<KeyInfo><X509Data><X509IssuerSerial>" +
                $"<X509IssuerName>{Escapar(_fiel.EmisorCertificado)}</X509IssuerName>" +
                $"<X509SerialNumber>{_fiel.SerieDecimal}</X509SerialNumber>" +
                "</X509IssuerSerial>" +
                $"<X509Certificate>{_fiel.CertificadoBase64}</X509Certificate>" +
                "</X509Data></KeyInfo>";
        }

        // El SignedInfo se firma con su xmlns y se inserta heredándolo de <Signature>.
        return "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\">" +
               signedInfo.Replace(
                   "<SignedInfo xmlns=\"http://www.w3.org/2000/09/xmldsig#\">",
                   "<SignedInfo>") +
               $"<SignatureValue>{valorFirma}</SignatureValue>" +
               keyInfo +
               "</Signature>";
    }

    private static string CrearIdToken()
    {
        string md5 =
            Convert.ToHexStringLower(
                MD5.HashData(
                    Guid.NewGuid().ToByteArray()));

        return $"uuid-{md5[..8]}-{md5[4..8]}-{md5[4..12]}-{md5[4..16]}-{md5[20..]}-1";
    }

    private static string FormatoSat(
        DateTime utc)
    {
        return utc.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);
    }

    private static string Escapar(
        string texto)
    {
        return texto
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#039;");
    }

    private static XElement? Buscar(
        XElement raiz,
        params string[] nombres)
    {
        XElement? actual =
            raiz;

        foreach (string nombre in nombres)
        {
            actual =
                actual?.Elements().FirstOrDefault(
                    e => e.Name.LocalName.Equals(nombre, StringComparison.OrdinalIgnoreCase));
        }

        return actual;
    }

    private static string Atributo(
        XElement elemento,
        string nombre)
    {
        return elemento.Attributes()
            .FirstOrDefault(a => a.Name.LocalName.Equals(nombre, StringComparison.OrdinalIgnoreCase))?
            .Value ?? "";
    }

    private static int Entero(
        XElement elemento,
        string nombre)
    {
        return int.TryParse(
            Atributo(elemento, nombre),
            out int valor)
            ? valor
            : 0;
    }
}

public sealed class ParametrosSolicitudSat
{
    public DateTime FechaInicial { get; set; }

    public DateTime FechaFinal { get; set; }

    public bool Recibidos { get; set; }

    public bool Metadatos { get; set; }

    /// <summary>I, E, T, N, P o vacío para todos.</summary>
    public string TipoComprobante { get; set; } =
        "";

    public string? RfcContraparte { get; set; }
}

public sealed record ResultadoSolicitudSat(
    int CodEstatus,
    string Mensaje,
    string IdSolicitud)
{
    /// <summary>5000 = solicitud recibida con éxito.</summary>
    public bool Aceptada =>
        CodEstatus == 5000;
}

public sealed record ResultadoVerificacionSat(
    int CodEstatus,
    string Mensaje,
    int EstadoSolicitud,
    int CodigoEstadoSolicitud,
    int NumeroCfdis,
    List<string> Paquetes)
{
    public bool Aceptada =>
        CodEstatus == 5000;

    /// <summary>1 Aceptada, 2 En proceso, 3 Terminada, 4 Error, 5 Rechazada, 6 Vencida.</summary>
    public string EstadoTexto =>
        EstadoSolicitud switch
        {
            1 => "Aceptada",
            2 => "En proceso",
            3 => "Terminada",
            4 => "Error",
            5 => "Rechazada",
            6 => "Vencida",
            _ => "Desconocido"
        };
}

public sealed record ResultadoDescargaSat(
    int CodEstatus,
    string Mensaje,
    byte[] Paquete)
{
    public bool Aceptada =>
        CodEstatus == 5000;
}
