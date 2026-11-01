using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace CFDIManager.Web.Services.Sat;

/// <summary>Declaración listada en el portal del SAT (antes de bajar su acuse).</summary>
public sealed class DeclaracionPortalSat
{
    /// <summary>MENSUAL o ANUAL.</summary>
    public string Tipo { get; init; } = "MENSUAL";
    public int Ejercicio { get; init; }

    /// <summary>Mes 1-12; 0 en la anual o si no se reconoce.</summary>
    public int Periodo { get; init; }

    public string PeriodoTexto { get; init; } = "";
    public string TipoDeclaracion { get; init; } = "";
    public string NumeroOperacion { get; init; } = "";
    public DateTime? FechaPresentacion { get; init; }

    /// <summary>Solo anuales: liga del acuse que pide GeneraArchivoPdf.</summary>
    public string UriBlob { get; init; } = "";
    public string TipoDocumento { get; init; } = "";
}

/// <summary>
/// Portales de declaraciones del SAT con RFC + CIEC + captcha (como lo hace onefacture):
/// mensuales/provisionales en pstcdypisr.clouda.sat.gob.mx (ejercicios 2025 en adelante) y
/// anuales en anualpf.clouda.sat.gob.mx. Cada portal tiene su propio inicio de sesión.
/// </summary>
public sealed class PortalSatDeclaraciones : IDisposable
{
    private const string UrlMensual = "https://pstcdypisr.clouda.sat.gob.mx/";
    private const string UrlAnual = "https://anualpf.clouda.sat.gob.mx/";

    private HttpClient _http;
    private readonly string? _carpetaDiagnostico;

    private Uri? _accionLogin;
    private Dictionary<string, string> _camposLogin = new();
    private string _token = "";

    public PortalSatDeclaraciones(string? carpetaDiagnostico = null)
    {
        _http = CrearCliente();
        _carpetaDiagnostico = carpetaDiagnostico;
    }

    /// <summary>"MENSUAL" o "ANUAL" con sesión abierta, o null.</summary>
    public string? PortalSesion { get; private set; }

    public string? RfcSesion { get; private set; }

    // Portal del captcha pedido; al iniciar sesión pasa a ser el de la sesión.
    private string _portalPedido = "MENSUAL";

    private Uri Base => new(_portalPedido == "ANUAL" ? UrlAnual : UrlMensual);

    // ---------- inicio de sesión ----------

    /// <summary>Sesión nueva en el portal (MENSUAL o ANUAL); regresa el captcha como data URI.</summary>
    public async Task<string> ObtenerCaptchaAsync(string portal, CancellationToken ct = default)
    {
        _http.Dispose();
        _http = CrearCliente();
        PortalSesion = null;
        RfcSesion = null;
        _token = "";
        _portalPedido = portal == "ANUAL" ? "ANUAL" : "MENSUAL";

        // El portal redirige al inicio de sesión con formularios que el navegador envía solo.
        (string html, Uri actual) = await GetAsync(Base, ct);
        (html, actual) = await SeguirFormulariosAsync(html, actual, ct);

        // El de anuales puede abrir primero la pantalla de e.firma: se cambia a contraseña.
        if (!TieneCaptcha(html) && html.Contains("certform", StringComparison.OrdinalIgnoreCase))
        {
            (html, actual) = await GetAsync(UrlContrasena(actual), ct);
            (html, actual) = await SeguirFormulariosAsync(html, actual, ct);
        }

        Diagnostico($"{_portalPedido.ToLowerInvariant()}-1-captcha.html", $"<!-- {actual} -->\n{html}");

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        HtmlNode? img =
            doc.DocumentNode.SelectSingleNode("//*[@id='divCaptcha']//img") ??
            doc.DocumentNode.SelectSingleNode("//img[contains(translate(@src,'CAPTCHA','captcha'),'captcha') or starts-with(@src,'data:image')]");

        string src = HtmlEntity.DeEntitize(img?.GetAttributeValue("src", "") ?? "");

        if (src == "")
            throw new InvalidOperationException("El SAT no mostró el captcha; intenta de nuevo en unos minutos.");

        // Formulario del login: se manda con sus campos ocultos más RFC, CIEC y captcha.
        HtmlNode? form =
            doc.DocumentNode.SelectSingleNode("//form[.//input[@name='Ecom_Password']]") ??
            doc.DocumentNode.SelectSingleNode("//form[.//input[@name='userCaptcha']]") ??
            doc.DocumentNode.SelectSingleNode("//form");

        string accion = HtmlEntity.DeEntitize(form?.GetAttributeValue("action", "") ?? "");
        _accionLogin = accion == "" ? actual : new Uri(actual, accion);
        _camposLogin = form is null ? new() : CamposOcultos(form);

        if (src.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            return src;

        byte[] imagen = await _http.GetByteArrayAsync(new Uri(actual, src), ct);
        return $"data:image/jpeg;base64,{Convert.ToBase64String(imagen)}";
    }

    /// <summary>Inicia sesión con el captcha de <see cref="ObtenerCaptchaAsync"/>. Lanza con el motivo si el SAT la rechaza.</summary>
    public async Task IniciarSesionAsync(string rfc, string ciec, string captcha, CancellationToken ct = default)
    {
        if (_accionLogin is null)
            throw new InvalidOperationException("Pide primero el captcha.");

        rfc = rfc.Trim().ToUpperInvariant();
        captcha = captcha.Trim().ToUpperInvariant();

        var campos = new Dictionary<string, string>(_camposLogin)
        {
            ["option"] = "credential",
            ["Ecom_User_ID"] = rfc,
            ["Ecom_Password"] = ciec,
            ["userCaptcha"] = captcha,
            ["jcaptcha"] = captcha,
            ["submit"] = "Enviar"
        };

        using HttpResponseMessage login = await _http.PostAsync(_accionLogin, new FormUrlEncodedContent(campos), ct);
        string htmlLogin = await login.Content.ReadAsStringAsync(ct);
        Uri actual = login.RequestMessage?.RequestUri ?? _accionLogin;

        Diagnostico($"{_portalPedido.ToLowerInvariant()}-2-login.html", $"<!-- {actual} -->\n{htmlLogin}");

        if (htmlLogin.Contains("Ecom_Password", StringComparison.OrdinalIgnoreCase) || TieneCaptcha(htmlLogin))
            throw new InvalidOperationException(MotivoRechazo(htmlLogin));

        // El SAT entrega la sesión al portal con formularios de WS-Federation que se envían solos.
        (string html, actual) = await SeguirFormulariosAsync(htmlLogin, actual, ct);

        if (!EsDelPortal(actual))
        {
            (html, actual) = await GetAsync(new Uri($"https://{_accionLogin.Host}/nidp/wsfed/ep?sid=0"), ct);
            (html, actual) = await SeguirFormulariosAsync(html, actual, ct);
        }

        try
        {
            (html, actual) = await AbrirConsultaAsync("1", ct);
        }
        catch (SesionSatExpiradaException)
        {
            throw new InvalidOperationException("El SAT aceptó la contraseña pero no abrió el portal de declaraciones. Intenta de nuevo.");
        }

        Diagnostico($"{_portalPedido.ToLowerInvariant()}-3-consulta.html", $"<!-- {actual} -->\n{html}");

        if (!EsDelPortal(actual) || _token == "")
            throw new InvalidOperationException("El SAT aceptó la contraseña pero no abrió el portal de declaraciones. Intenta de nuevo.");

        PortalSesion = _portalPedido;
        RfcSesion = rfc;
    }

    // ---------- consultas ----------

    /// <summary>Declaraciones presentadas del ejercicio en el portal de la sesión.</summary>
    public Task<List<DeclaracionPortalSat>> ConsultarAsync(int ejercicio, CancellationToken ct = default) =>
        PortalSesion switch
        {
            "MENSUAL" => ConsultarMensualesAsync(ejercicio, ct),
            "ANUAL" => ConsultarAnualesAsync(ejercicio, ct),
            _ => throw new SesionSatExpiradaException()
        };

    private async Task<List<DeclaracionPortalSat>> ConsultarMensualesAsync(int ejercicio, CancellationToken ct)
    {
        // tipoDocumento 3 = reimpresión de acuse: las declaraciones presentadas con su número de operación.
        string datos = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["tipoDocumento"] = "3",
            ["idDeclaracion"] = "001",
            ["tipoDeclaracion"] = "",
            ["tipoComplementaria"] = "",
            ["ejercicio"] = ejercicio.ToString(CultureInfo.InvariantCulture),
            ["periodicidad"] = "",
            ["periodo"] = "",
            ["fechaCausacion"] = "",
            ["fechaPresentacion"] = "",
            ["numeroOperacion"] = 0,
            ["lineaCaptura"] = "",
            ["conceptos"] = Array.Empty<object>()
        })));

        await AbrirConsultaAsync("3", ct);

        string cuerpo = "";

        for (int intento = 1; intento <= 3; intento++)
        {
            cuerpo = await PostAjaxAsync(new Uri(Base, "Consulta/Buscar"), new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = _token,
                ["data"] = datos
            }, ct);

            if (!cuerpo.TrimStart().StartsWith('<'))
                break;

            // HTML en vez de JSON: el portal pidió reenviar la sesión o cambió el token.
            await AbrirConsultaAsync("3", ct);
        }

        Diagnostico($"mensual-4-buscar-{ejercicio}.json", cuerpo);

        if (cuerpo.TrimStart().StartsWith('<'))
            throw new SesionSatExpiradaException();

        using JsonDocument json = JsonDocument.Parse(cuerpo);

        if (Valor(json.RootElement, "IdMensaje") == "NoResultadoConsulta" ||
            !json.RootElement.TryGetProperty("Declaraciones", out JsonElement lista))
        {
            return [];
        }

        // "Declaraciones" a veces viene como texto con el arreglo JSON adentro.
        using JsonDocument? interno = lista.ValueKind == JsonValueKind.String ? JsonDocument.Parse(lista.GetString() ?? "[]") : null;
        JsonElement arreglo = interno?.RootElement ?? lista;

        if (arreglo.ValueKind != JsonValueKind.Array)
            return [];

        var resultado = new List<DeclaracionPortalSat>();

        foreach (JsonElement d in arreglo.EnumerateArray())
        {
            string periodo = Valor(d, "Periodo", "Período");
            string tipo = Valor(d, "TipoDeclaracion", "Tipo de Declaración");
            string complementaria = Valor(d, "TipoComplementaria", "Tipo de Complementaria");

            resultado.Add(new DeclaracionPortalSat
            {
                Tipo = "MENSUAL",
                Ejercicio = int.TryParse(Valor(d, "Ejercicio"), out int e) ? e : ejercicio,
                Periodo = Mes(periodo),
                PeriodoTexto = periodo,
                TipoDeclaracion = complementaria != "" && !Normalizar(tipo).Contains(Normalizar(complementaria)) ? $"{tipo} - {complementaria}".Trim(' ', '-') : tipo,
                NumeroOperacion = Valor(d, "NumeroOperacion", "No. de Operación", "NoOperacion"),
                FechaPresentacion = Fecha(Valor(d, "FechaPresentacion", "Fecha Presentación", "FechaDeclaracion", "Fecha Declaración")),
                TipoDocumento = "3"
            });
        }

        return resultado.Where(d => d.NumeroOperacion != "").ToList();
    }

    private async Task<List<DeclaracionPortalSat>> ConsultarAnualesAsync(int ejercicio, CancellationToken ct)
    {
        // Personas morales (RFC de 12) consultan "impresión de acuse" (3); físicas "reimpresión" (1).
        string tipoDocumento = RfcSesion?.Length == 12 ? "3" : "1";

        await AbrirConsultaAsync(tipoDocumento, ct);

        string cuerpo = await PostAjaxAsync(new Uri(Base, "Consulta/Buscar?Length=8"), new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = _token,
            ["IdDocumento"] = tipoDocumento,
            ["IdDeclaracion"] = "002",
            ["Rfc"] = RfcSesion ?? "",
            ["IdTipoDeclaracion"] = "",
            ["IdTipoComplementaria"] = "",
            ["IdPeriodo"] = "",
            ["IdEjercicio"] = ejercicio.ToString(CultureInfo.InvariantCulture),
            ["FechaPresentacion"] = "",
            ["NumeroOperacion"] = "",
            ["LineaCaptura"] = "",
            ["hidConsulta"] = ""
        }, ct, new Uri(Base, $"Consulta/Consulta?tipoDocumento={tipoDocumento}"));

        Diagnostico($"anual-4-buscar-{ejercicio}.html", cuerpo);

        if (cuerpo.Contains("No hay informaci", StringComparison.OrdinalIgnoreCase))
            return [];

        var doc = new HtmlDocument();
        doc.LoadHtml(cuerpo);

        var resultado = new List<DeclaracionPortalSat>();

        // Cada declaración es un panel con renglones "Etiqueta:" seguidos de su valor.
        foreach (HtmlNode panel in doc.DocumentNode.SelectNodes("//*[contains(concat(' ',normalize-space(@class),' '),' panel-collapse ')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var datos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? etiqueta = null;

            foreach (string linea in HtmlEntity.DeEntitize(panel.InnerText).Split('\n').Select(l => l.Trim()).Where(l => l != ""))
            {
                if (linea.EndsWith(':'))
                {
                    etiqueta = Clave(linea[..^1]);
                    datos.TryAdd(etiqueta, "");
                }
                else if (etiqueta is not null)
                {
                    datos[etiqueta] = linea;
                    etiqueta = null;
                }
            }

            string Dato(params string[] nombres) =>
                nombres.Select(n => datos.GetValueOrDefault(Clave(n), "")).FirstOrDefault(v => v != "") ?? "";

            resultado.Add(new DeclaracionPortalSat
            {
                Tipo = "ANUAL",
                Ejercicio = int.TryParse(Dato("Ejercicio"), out int e) ? e : ejercicio,
                TipoDeclaracion = Dato("Tipo de Declaración", "TipoDeclaracion"),
                NumeroOperacion = Regex.Match(Dato("No. de Operación", "Número de Operación", "NumeroOperacion"), @"\d+").Value,
                FechaPresentacion = Fecha(Dato("Fecha de Presentación", "FechaPresentacion")),
                UriBlob = HtmlEntity.DeEntitize(Regex.Match(panel.InnerHtml, @"https?://[-a-zA-Z0-9@:%._+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b[-a-zA-Z0-9()@:%_+.~#?&/=;]*").Value),
                TipoDocumento = tipoDocumento
            });
        }

        return resultado.Where(d => d.NumeroOperacion != "").ToList();
    }

    /// <summary>PDF del acuse, o null si el SAT no lo entrega.</summary>
    public async Task<byte[]?> DescargarAcuseAsync(DeclaracionPortalSat d, CancellationToken ct = default)
    {
        Uri url = d.Tipo == "ANUAL"
            ? new Uri(Base, $"GeneraArchivo/GeneraArchivoPdf?enLinea=0&numeroOperacion={d.NumeroOperacion}&tipoDocumento={(d.TipoDocumento == "2" ? 2 : 1)}" +
                            $"&uriBlob={Uri.EscapeDataString(d.UriBlob)}&ejercicio={d.Ejercicio}")
            : new Uri(Base, $"Consulta/RecuperarArchivo?enLinea=0&tipoDocumento=3&numeroOperacion={d.NumeroOperacion}&ejercicio={d.Ejercicio}");

        for (int intento = 1; intento <= 3; intento++)
        {
            using HttpResponseMessage r = await _http.GetAsync(url, ct);
            byte[] cuerpo = await r.Content.ReadAsByteArrayAsync(ct);

            if (cuerpo.Length > 4 && cuerpo[0] == '%' && cuerpo[1] == 'P' && cuerpo[2] == 'D' && cuerpo[3] == 'F')
                return cuerpo;

            // Página intermedia (formulario PostIt o token nuevo): se envía y se vuelve a pedir.
            string html = Encoding.UTF8.GetString(cuerpo);
            Diagnostico($"acuse-{d.NumeroOperacion}.html", html);

            if (!html.Contains("<form", StringComparison.OrdinalIgnoreCase))
                return null;

            await SeguirFormulariosAsync(html, r.RequestMessage?.RequestUri ?? url, ct);
        }

        return null;
    }

    public void Dispose() => _http.Dispose();

    // ---------- navegación ----------

    /// <summary>Abre la pantalla de consulta (enviando los formularios intermedios) y toma su token antifalsificación.</summary>
    private async Task<(string Html, Uri Url)> AbrirConsultaAsync(string tipoDocumento, CancellationToken ct)
    {
        (string html, Uri actual) = await GetAsync(new Uri(Base, $"Consulta/Consulta?tipoDocumento={tipoDocumento}"), ct);
        (html, actual) = await SeguirFormulariosAsync(html, actual, ct);

        // Tras el reenvío de la sesión el portal a veces cae en su inicio: se pide otra vez la consulta.
        if (EsDelPortal(actual) && !actual.AbsolutePath.Contains("/Consulta/Consulta", StringComparison.OrdinalIgnoreCase))
        {
            (html, actual) = await GetAsync(new Uri(Base, $"Consulta/Consulta?tipoDocumento={tipoDocumento}"), ct);
            (html, actual) = await SeguirFormulariosAsync(html, actual, ct);
        }

        if (Token(html) is { Length: > 0 } token)
            _token = token;

        if (!EsDelPortal(actual))
            throw new SesionSatExpiradaException();

        return (html, actual);
    }

    /// <summary>
    /// Envía los formularios que la página manda sola (WS-Federation, PostIt, deviceRecon):
    /// los que solo tienen campos ocultos. Se detiene en el captcha o en una página normal.
    /// </summary>
    private async Task<(string Html, Uri Url)> SeguirFormulariosAsync(string html, Uri actual, CancellationToken ct)
    {
        for (int i = 0; i < 6 && !TieneCaptcha(html); i++)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            HtmlNode? form = doc.DocumentNode.SelectNodes("//form")?.FirstOrDefault(f => EsAutomatico(f, html));

            if (form is null)
                break;

            string accion = HtmlEntity.DeEntitize(form.GetAttributeValue("action", ""));

            // El login de anuales manda sola la opción de e.firma (responde 500 sin certificado): se pide la de contraseña.
            Uri destino = SinEFirma(accion == "" ? actual : new Uri(actual, accion));

            using var solicitud = new HttpRequestMessage(HttpMethod.Post, destino) { Content = new FormUrlEncodedContent(CamposOcultos(form)) };
            solicitud.Headers.Referrer = actual;

            using HttpResponseMessage r = await _http.SendAsync(solicitud, ct);
            html = await r.Content.ReadAsStringAsync(ct);
            actual = r.RequestMessage?.RequestUri ?? destino;
        }

        return (html, actual);
    }

    /// <summary>
    /// Formulario sin campos para el usuario que la página envía sola: con campos ocultos, o vacío
    /// cuando la página lo manda por script (document.forms[0].submit(), como hace el login del SAT).
    /// </summary>
    private static bool EsAutomatico(HtmlNode form, string html)
    {
        List<HtmlNode> inputs = form.SelectNodes(".//input[@name]")?.ToList() ?? [];

        if (form.SelectSingleNode(".//select|.//textarea") is not null)
            return false;

        if (inputs.Count == 0)
            return html.Contains(".submit()", StringComparison.OrdinalIgnoreCase);

        return inputs.All(i => i.GetAttributeValue("type", "text").ToLowerInvariant() is "hidden" or "submit");
    }

    private static Dictionary<string, string> CamposOcultos(HtmlNode form)
    {
        var campos = new Dictionary<string, string>();

        foreach (HtmlNode input in form.SelectNodes(".//input[@name]") ?? Enumerable.Empty<HtmlNode>())
        {
            if (input.GetAttributeValue("type", "text").ToLowerInvariant() is "hidden")
                campos[HtmlEntity.DeEntitize(input.GetAttributeValue("name", ""))] = HtmlEntity.DeEntitize(input.GetAttributeValue("value", ""));
        }

        return campos;
    }

    private async Task<(string Html, Uri Url)> GetAsync(Uri url, CancellationToken ct)
    {
        using HttpResponseMessage r = await _http.GetAsync(url, ct);
        return (await r.Content.ReadAsStringAsync(ct), r.RequestMessage?.RequestUri ?? url);
    }

    private async Task<string> PostAjaxAsync(Uri url, Dictionary<string, string> campos, CancellationToken ct, Uri? referer = null)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(campos) };
        solicitud.Headers.Add("X-Requested-With", "XMLHttpRequest");
        solicitud.Headers.Referrer = referer ?? new Uri(Base, "Consulta/Consulta?tipoDocumento=3");
        solicitud.Headers.Add("Origin", Base.GetLeftPart(UriPartial.Authority));

        using HttpResponseMessage r = await _http.SendAsync(solicitud, ct);
        return await r.Content.ReadAsStringAsync(ct);
    }

    // ---------- utilerías ----------

    private bool EsDelPortal(Uri url) => url.Host.Equals(Base.Host, StringComparison.OrdinalIgnoreCase);

    private static bool TieneCaptcha(string html) =>
        html.Contains("divCaptcha", StringComparison.OrdinalIgnoreCase) || html.Contains("userCaptcha", StringComparison.OrdinalIgnoreCase);

    /// <summary>De la pantalla de e.firma del login de anuales, a la de contraseña (como onefacture).</summary>
    private static Uri UrlContrasena(Uri actual)
    {
        string url = actual.ToString();

        if (url.Contains("contr-dual-eFirma-totp", StringComparison.OrdinalIgnoreCase))
            return new Uri(url.Replace("contr-dual-eFirma-totp", "contr-dual-totp-eFirma", StringComparison.OrdinalIgnoreCase));

        if (url.Contains("id=fiel", StringComparison.OrdinalIgnoreCase))
            return new Uri(url.Replace("id=fiel", "id=contr-dual-totp-eFirma", StringComparison.OrdinalIgnoreCase));

        return new Uri("https://login.mat.sat.gob.mx/nidp/app/login?id=contr-dual-totp-eFirma&sid=0&option=credential&sid=0");
    }

    /// <summary>La misma URL, pero con la opción de contraseña si apunta a la de e.firma.</summary>
    private static Uri SinEFirma(Uri url) =>
        url.ToString().Contains("contr-dual-eFirma-totp", StringComparison.OrdinalIgnoreCase)
            ? new Uri(url.ToString().Replace("contr-dual-eFirma-totp", "contr-dual-totp-eFirma", StringComparison.OrdinalIgnoreCase))
            : url;

    private static string Token(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        HtmlNode? input =
            doc.DocumentNode.SelectSingleNode("//*[@id='__AjaxAntiForgeryForm']//input[@name='__RequestVerificationToken']") ??
            doc.DocumentNode.SelectSingleNode("//input[@name='__RequestVerificationToken']");

        return HtmlEntity.DeEntitize(input?.GetAttributeValue("value", "") ?? "");
    }

    private static string MotivoRechazo(string html)
    {
        if (html.Contains("contraseña son incorrectos", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("contrase&ntilde;a son incorrectos", StringComparison.OrdinalIgnoreCase))
        {
            return "El RFC o la contraseña (CIEC) son incorrectos.";
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        string? mensaje = doc.DocumentNode
            .SelectNodes("//*[@id='msgError' or contains(@class,'error') or contains(@class,'alert')]")?
            .Select(n => Regex.Replace(HtmlEntity.DeEntitize(n.InnerText), @"\s+", " ").Trim())
            .FirstOrDefault(t => t.Length is > 3 and < 300);

        return string.IsNullOrWhiteSpace(mensaje)
            ? "El SAT no aceptó el inicio de sesión: revisa el captcha e intenta de nuevo."
            : $"El SAT no aceptó el inicio de sesión: {mensaje}";
    }

    /// <summary>Valor de una propiedad del JSON del SAT buscando por nombre sin acentos ni espacios; acepta {"value": ...}.</summary>
    private static string Valor(JsonElement objeto, params string[] nombres)
    {
        if (objeto.ValueKind != JsonValueKind.Object)
            return "";

        foreach (string nombre in nombres)
        {
            foreach (JsonProperty p in objeto.EnumerateObject())
            {
                if (Clave(p.Name) != Clave(nombre))
                    continue;

                JsonElement v = p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("value", out JsonElement interno) ? interno : p.Value;

                string texto = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString() ?? "",
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
                    _ => ""
                };

                if (texto.Trim() != "")
                    return texto.Trim();
            }
        }

        return "";
    }

    private static string Clave(string texto) => Regex.Replace(Normalizar(texto), "[^a-z0-9]", "");

    private static string Normalizar(string texto) =>
        new string(texto.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray()).Trim();

    private static readonly CultureInfo Mx = new("es-MX");

    /// <summary>"Enero", "Enero-Enero", "01"… → 1..12; 0 si no se reconoce.</summary>
    private static int Mes(string periodo)
    {
        string texto = Normalizar(periodo);

        for (int m = 1; m <= 12; m++)
        {
            if (texto.Contains(Normalizar(Mx.DateTimeFormat.GetMonthName(m))))
                return m;
        }

        if (texto.Contains("setiembre"))
            return 9;

        Match numero = Regex.Match(texto, @"\b(1[0-2]|0?[1-9])\b");
        return numero.Success ? int.Parse(numero.Value, CultureInfo.InvariantCulture) : 0;
    }

    private static DateTime? Fecha(string texto)
    {
        Match m = Regex.Match(texto, @"\d{2}/\d{2}/\d{4}|\d{4}-\d{2}-\d{2}");

        return m.Success && DateTime.TryParseExact(m.Value, ["dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime f)
            ? f
            : null;
    }

    private void Diagnostico(string nombre, string contenido)
    {
        if (_carpetaDiagnostico is null)
            return;

        try
        {
            Directory.CreateDirectory(_carpetaDiagnostico);
            File.WriteAllText(Path.Combine(_carpetaDiagnostico, nombre), contenido);
        }
        catch (IOException)
        {
            // El diagnóstico nunca debe interrumpir la consulta.
        }
    }

    private static HttpClient CrearCliente()
    {
        var manejador = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        };

        var cliente = new HttpClient(manejador) { Timeout = TimeSpan.FromMinutes(3) };

        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        cliente.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es-MX,es;q=0.9");

        return cliente;
    }
}
