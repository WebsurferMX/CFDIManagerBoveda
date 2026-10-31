using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace CFDIManager.Web.Services.Sat;

/// <summary>CFDI listado en el portal "Factura electrónica" del SAT.</summary>
public sealed class CfdiPortalSat
{
    public string Uuid { get; init; } = "";
    public string RfcEmisor { get; init; } = "";
    public string NombreEmisor { get; init; } = "";
    public string RfcReceptor { get; init; } = "";
    public string NombreReceptor { get; init; } = "";
    public string FechaEmision { get; init; } = "";
    public string FechaCertificacion { get; init; } = "";
    public string Total { get; init; } = "";
    public string Efecto { get; init; } = "";
    public string Estado { get; init; } = "";

    /// <summary>Liga RecuperaCfdi.aspx del XML; vacía si el SAT no permite descargarlo (p. ej. recibido cancelado).</summary>
    public string UrlXml { get; init; } = "";

    public bool YaImportado { get; set; }
}

/// <summary>
/// Cliente del portal de facturas del SAT (portalcfdi.facturaelectronica.sat.gob.mx) con RFC y
/// CIEC. No hay web service para la CIEC: se navega como un navegador (inicio de sesión con
/// captcha y formularios ASP.NET del portal). Cada consulta del portal regresa máximo 500 CFDI,
/// por eso se consulta por día y, si llega al tope, por rangos de horas.
/// </summary>
public sealed class PortalSatCiec : IDisposable
{
    private const string UrlLogin = "https://cfdiau.sat.gob.mx/nidp/wsfed/ep?id=SATUPCFDiCon&sid=0&option=credential&sid=0";
    private const string UrlPortal = "https://portalcfdi.facturaelectronica.sat.gob.mx/";
    private const string UrlEmitidos = UrlPortal + "ConsultaEmisor.aspx";
    private const string UrlRecibidos = UrlPortal + "ConsultaReceptor.aspx";

    public const int MaximoPorConsulta = 500;

    private HttpClient _http;

    // Opciones de cada <select> del formulario, para mandar el valor tal como lo espera el portal.
    private readonly Dictionary<string, List<string>> _opciones = new();

    // Respuestas del portal de la última consulta (sin la CIEC), para revisar cambios de formato del SAT.
    private readonly string? _carpetaDiagnostico;

    public PortalSatCiec(string? carpetaDiagnostico = null)
    {
        _http = CrearCliente();
        _carpetaDiagnostico = carpetaDiagnostico;
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

    /// <summary>RFC con sesión abierta, o null.</summary>
    public string? RfcSesion { get; private set; }

    /// <summary>
    /// Empieza una sesión nueva (descarta cookies anteriores) y regresa el captcha del inicio de
    /// sesión como data URI para mostrarlo en un &lt;img&gt;.
    /// </summary>
    public async Task<string> ObtenerCaptchaAsync(CancellationToken ct = default)
    {
        _http.Dispose();
        _http = CrearCliente();
        RfcSesion = null;

        // El inicio de sesión solo responde si se llega desde el portal: este redirige a una
        // página con un formulario que el navegador envía solo y que abre la pantalla del captcha.
        using HttpResponseMessage portal = await _http.GetAsync(UrlPortal, ct);
        string html = await portal.Content.ReadAsStringAsync(ct);
        Uri actual = portal.RequestMessage?.RequestUri ?? new Uri(UrlPortal);

        for (int i = 0; i < 3 && !html.Contains("divCaptcha", StringComparison.OrdinalIgnoreCase); i++)
        {
            (html, actual) = await EnviarFormularioAsync(html, actual, "//form", ct);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        HtmlNode? img =
            doc.DocumentNode.SelectSingleNode("//*[@id='divCaptcha']//img") ??
            doc.DocumentNode.SelectSingleNode("//img[contains(translate(@src,'CAPTCHA','captcha'),'captcha') or starts-with(@src,'data:image')]");

        string src = HtmlEntity.DeEntitize(img?.GetAttributeValue("src", "") ?? "");

        if (src == "")
            throw new InvalidOperationException("El SAT no mostró el captcha; intenta de nuevo en unos minutos.");

        if (src.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            return src;

        byte[] imagen = await _http.GetByteArrayAsync(new Uri(new Uri(UrlLogin), src), ct);

        return $"data:image/jpeg;base64,{Convert.ToBase64String(imagen)}";
    }

    /// <summary>Inicia sesión con el captcha obtenido en <see cref="ObtenerCaptchaAsync"/>. Lanza con el motivo si el SAT la rechaza.</summary>
    public async Task IniciarSesionAsync(string rfc, string ciec, string captcha, CancellationToken ct = default)
    {
        RfcSesion = null;
        rfc = rfc.Trim().ToUpperInvariant();

        using HttpResponseMessage login = await _http.PostAsync(UrlLogin, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Ecom_User_ID"] = rfc,
            ["Ecom_Password"] = ciec,
            ["option"] = "credential",
            ["submit"] = "Enviar",
            // La página del SAT lo convierte a mayúsculas al escribirlo.
            ["userCaptcha"] = captcha.Trim().ToUpperInvariant()
        }), ct);

        string htmlLogin = await login.Content.ReadAsStringAsync(ct);

        // Tras el login, el SAT entrega al portal la sesión con un formulario oculto (WS-Federation)
        // que el navegador envía solo; aquí se envía a mano.
        await EnviarFormulariosFederacionAsync(htmlLogin, login.RequestMessage?.RequestUri ?? new Uri(UrlLogin), ct);

        using HttpResponseMessage portal = await _http.GetAsync(UrlPortal, ct);
        await EnviarFormulariosFederacionAsync(await portal.Content.ReadAsStringAsync(ct), portal.RequestMessage?.RequestUri ?? new Uri(UrlPortal), ct);

        using HttpResponseMessage prueba = await _http.GetAsync(UrlRecibidos, ct);

        if (EsLogin(prueba))
            throw new InvalidOperationException(MotivoRechazo(htmlLogin));

        RfcSesion = rfc;
    }

    /// <summary>
    /// CFDI de un día. Consulta las 24 horas y, si el portal llega al tope de 500, parte el rango
    /// de horas a la mitad hasta quedar por debajo. <paramref name="aviso"/> recibe las horas que
    /// aun así llegaron al tope.
    /// </summary>
    public async Task<List<CfdiPortalSat>> ConsultarDiaAsync(
        bool recibidos, DateTime dia, string estado, string rfcContraparte,
        Action<string>? aviso, CancellationToken ct = default)
    {
        var resultado = new List<CfdiPortalSat>();
        var pendientes = new Stack<(int Desde, int Hasta)>();
        pendientes.Push((0, 23));

        while (pendientes.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var (desde, hasta) = pendientes.Pop();
            List<CfdiPortalSat> lote = await ConsultarAsync(recibidos, dia, desde, hasta, estado, rfcContraparte, ct);

            if (lote.Count >= MaximoPorConsulta && desde < hasta)
            {
                int medio = (desde + hasta) / 2;
                pendientes.Push((medio + 1, hasta));
                pendientes.Push((desde, medio));
                continue;
            }

            if (lote.Count >= MaximoPorConsulta)
                aviso?.Invoke($"{dia:dd/MM/yyyy} {desde:00}:00 h tiene {MaximoPorConsulta} o más CFDI; el portal solo muestra {MaximoPorConsulta}. Usa la descarga con e.firma para ese día.");

            resultado.AddRange(lote);
        }

        return resultado
            .GroupBy(c => c.Uuid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>XML del CFDI con la liga de la consulta.</summary>
    public async Task<string> DescargarXmlAsync(CfdiPortalSat cfdi, CancellationToken ct = default)
    {
        if (cfdi.UrlXml == "")
            throw new InvalidOperationException("El SAT no permite descargar este XML desde el portal.");

        using HttpResponseMessage r = await _http.GetAsync(new Uri(new Uri(UrlPortal), cfdi.UrlXml), ct);

        if (EsLogin(r))
            throw new SesionSatExpiradaException();

        string xml = await r.Content.ReadAsStringAsync(ct);

        if (!xml.Contains("Comprobante", StringComparison.Ordinal))
            throw new InvalidOperationException($"El SAT no regresó el XML de {cfdi.Uuid}.");

        return xml;
    }

    public void Dispose() => _http.Dispose();

    // ---------- consulta ----------

    private async Task<List<CfdiPortalSat>> ConsultarAsync(
        bool recibidos, DateTime dia, int horaDesde, int horaHasta, string estado, string rfcContraparte, CancellationToken ct)
    {
        string url = recibidos ? UrlRecibidos : UrlEmitidos;

        using HttpResponseMessage pagina = await _http.GetAsync(url, ct);

        if (EsLogin(pagina))
            throw new SesionSatExpiradaException();

        var campos = new Dictionary<string, string>();
        _opciones.Clear();
        string htmlPagina = await pagina.Content.ReadAsStringAsync(ct);
        Diagnostico("1-pagina.html", htmlPagina);
        LeerCampos(htmlPagina, campos);

        // "Folio fiscal" y "Fecha de emisión" son radios del mismo grupo (FiltroCentral); la página
        // llega con "folio fiscal" marcado. Si no se cambia, el SAT busca por UUID vacío y responde
        // "No existen registros" aunque haya CFDI en el periodo.
        const string Filtro = "ctl00$MainContent$FiltroCentral";

        // 1. Elegir "búsqueda por fecha": el portal regresa los controles de fecha.
        campos["__EVENTTARGET"] = "ctl00$MainContent$RdoFechas";
        campos["__EVENTARGUMENT"] = "";
        campos["__LASTFOCUS"] = "";
        campos["__ASYNCPOST"] = "true";
        campos["ctl00$ScriptManager1"] = "ctl00$MainContent$UpnlBusqueda|ctl00$MainContent$RdoFechas";
        campos[Filtro] = "RdoFechas";

        await PostAsincronoAsync(url, campos, ct, "2-fechas");

        // 2. Buscar.
        campos["__EVENTTARGET"] = "";
        campos["__EVENTARGUMENT"] = "";
        campos["__LASTFOCUS"] = "";
        campos["__ASYNCPOST"] = "true";
        campos["ctl00$ScriptManager1"] = "ctl00$MainContent$UpnlBusqueda|ctl00$MainContent$BtnBusqueda";
        campos["ctl00$MainContent$BtnBusqueda"] = "Buscar CFDI";
        campos[Filtro] = "RdoFechas";
        campos["ctl00$MainContent$TxtUUID"] = "";
        campos["ctl00$MainContent$DdlEstadoComprobante"] = Opcion("ctl00$MainContent$DdlEstadoComprobante", estado);
        campos["ctl00$MainContent$ddlComplementos"] = Opcion("ctl00$MainContent$ddlComplementos", "-1");
        campos["ctl00$MainContent$hfInicialBool"] = "false";
        campos["ctl00$MainContent$hfDescarga"] = "";
        campos["ctl00$MainContent$hfParametrosMetadata"] = "";
        campos.TryAdd("ctl00$MainContent$ddlVigente", "0");
        campos.TryAdd("ctl00$MainContent$ddlCancelado", "0");

        if (recibidos)
        {
            campos["ctl00$MainContent$TxtRfcEmisor"] = rfcContraparte;
            campos["ctl00$MainContent$CldFecha$DdlAnio"] = Opcion("ctl00$MainContent$CldFecha$DdlAnio", dia.Year);
            campos["ctl00$MainContent$CldFecha$DdlMes"] = Opcion("ctl00$MainContent$CldFecha$DdlMes", dia.Month);
            campos["ctl00$MainContent$CldFecha$DdlDia"] = Opcion("ctl00$MainContent$CldFecha$DdlDia", dia.Day);
            campos["ctl00$MainContent$CldFecha$DdlHora"] = Opcion("ctl00$MainContent$CldFecha$DdlHora", horaDesde);
            campos["ctl00$MainContent$CldFecha$DdlMinuto"] = Opcion("ctl00$MainContent$CldFecha$DdlMinuto", 0);
            campos["ctl00$MainContent$CldFecha$DdlSegundo"] = Opcion("ctl00$MainContent$CldFecha$DdlSegundo", 0);
            campos["ctl00$MainContent$CldFecha$DdlHoraFin"] = Opcion("ctl00$MainContent$CldFecha$DdlHoraFin", horaHasta);
            campos["ctl00$MainContent$CldFecha$DdlMinutoFin"] = Opcion("ctl00$MainContent$CldFecha$DdlMinutoFin", 59);
            campos["ctl00$MainContent$CldFecha$DdlSegundoFin"] = Opcion("ctl00$MainContent$CldFecha$DdlSegundoFin", 59);
        }
        else
        {
            string fecha = dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            campos["ctl00$MainContent$TxtRfcReceptor"] = rfcContraparte;
            campos["ctl00$MainContent$hfInicial"] = dia.Year.ToString(CultureInfo.InvariantCulture);
            campos["ctl00$MainContent$hfFinal"] = dia.Year.ToString(CultureInfo.InvariantCulture);
            campos["ctl00$MainContent$CldFechaInicial2$Calendario_text"] = fecha;
            campos["ctl00$MainContent$CldFechaInicial2$DdlHora"] = Opcion("ctl00$MainContent$CldFechaInicial2$DdlHora", horaDesde);
            campos["ctl00$MainContent$CldFechaInicial2$DdlMinuto"] = Opcion("ctl00$MainContent$CldFechaInicial2$DdlMinuto", 0);
            campos["ctl00$MainContent$CldFechaInicial2$DdlSegundo"] = Opcion("ctl00$MainContent$CldFechaInicial2$DdlSegundo", 0);
            campos["ctl00$MainContent$CldFechaFinal2$Calendario_text"] = fecha;
            campos["ctl00$MainContent$CldFechaFinal2$DdlHora"] = Opcion("ctl00$MainContent$CldFechaFinal2$DdlHora", horaHasta);
            campos["ctl00$MainContent$CldFechaFinal2$DdlMinuto"] = Opcion("ctl00$MainContent$CldFechaFinal2$DdlMinuto", 59);
            campos["ctl00$MainContent$CldFechaFinal2$DdlSegundo"] = Opcion("ctl00$MainContent$CldFechaFinal2$DdlSegundo", 59);
        }

        string html = await PostAsincronoAsync(url, campos, ct, "3-busqueda");

        return LeerResultados(html);
    }

    /// <summary>
    /// POST parcial de ASP.NET AJAX (UpdatePanel). Actualiza <paramref name="campos"/> con los
    /// campos ocultos que regresa el portal y devuelve el HTML de los paneles actualizados.
    /// </summary>
    private async Task<string> PostAsincronoAsync(string url, Dictionary<string, string> campos, CancellationToken ct, string paso)
    {
        Diagnostico(paso + "-enviado.txt", string.Join("\n", campos
            .Where(c => !c.Key.StartsWith("__VIEWSTATE") && c.Key != "__EVENTVALIDATION")
            .Select(c => $"{c.Key} = {c.Value}")));

        using var solicitud = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(campos)
        };

        solicitud.Headers.Add("X-MicrosoftAjax", "Delta=true");
        solicitud.Headers.Add("X-Requested-With", "XMLHttpRequest");
        solicitud.Headers.Referrer = new Uri(url);

        using HttpResponseMessage r = await _http.SendAsync(solicitud, ct);

        if (EsLogin(r))
            throw new SesionSatExpiradaException();

        string cuerpo = await r.Content.ReadAsStringAsync(ct);
        Diagnostico(paso + "-respuesta.txt", cuerpo);
        var paneles = new System.Text.StringBuilder();

        foreach (var (tipo, id, contenido) in LeerDelta(cuerpo))
        {
            switch (tipo)
            {
                case "hiddenField":
                    campos[id] = contenido;
                    break;
                case "updatePanel":
                case "html":
                    paneles.Append(contenido);
                    break;
                case "pageRedirect":
                    throw new SesionSatExpiradaException();
                case "error":
                    throw new InvalidOperationException($"El portal del SAT respondió con error: {contenido}");
            }
        }

        string html = paneles.ToString();

        // Los paneles traen controles nuevos (p. ej. las fechas): se suman sin pisar lo ya fijado.
        var nuevos = new Dictionary<string, string>();
        LeerCampos(html, nuevos);

        foreach (var (nombre, valor) in nuevos)
            campos.TryAdd(nombre, valor);

        return html;
    }

    /// <summary>Respuesta "longitud|tipo|id|contenido|" de ASP.NET AJAX; si no tiene ese formato, se toma completa como HTML.</summary>
    private static List<(string Tipo, string Id, string Contenido)> LeerDelta(string cuerpo)
    {
        var partes = new List<(string, string, string)>();
        int i = 0;

        try
        {
            while (i < cuerpo.Length)
            {
                int a = cuerpo.IndexOf('|', i);
                int longitud = int.Parse(cuerpo[i..a], CultureInfo.InvariantCulture);
                int b = cuerpo.IndexOf('|', a + 1);
                int c = cuerpo.IndexOf('|', b + 1);

                partes.Add((cuerpo[(a + 1)..b], cuerpo[(b + 1)..c], cuerpo.Substring(c + 1, longitud)));
                i = c + 1 + longitud + 1;
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return new() { ("html", "", cuerpo) };
        }

        return partes;
    }

    internal static List<CfdiPortalSat> LeerResultados(string html)
    {
        int inicio = html.IndexOf("ctl00_MainContent_tblResult", StringComparison.Ordinal);

        if (inicio < 0)
        {
            if (html.Contains("No existen registros", StringComparison.OrdinalIgnoreCase))
                return new();

            throw new InvalidOperationException("El portal del SAT no regresó la tabla de resultados; puede haber cambiado su formato.");
        }

        // El encabezado "Acciones" del portal trae un </div> suelto que el parser toma como cierre del
        // contenedor: corta la tabla después del encabezado y las filas quedan fuera (0 resultados).
        // Los div solo son de presentación, así que se quitan antes de leer la tabla.
        inicio = html.LastIndexOf("<table", inicio, StringComparison.OrdinalIgnoreCase);
        string htmlTabla = Regex.Replace(html[inicio..], @"</?div\b[^>]*>", "", RegexOptions.IgnoreCase);

        var doc = new HtmlDocument();
        doc.LoadHtml(htmlTabla);

        HtmlNode? tabla = doc.DocumentNode.SelectSingleNode("//table[@id='ctl00_MainContent_tblResult']");

        // Solo renglones propios: la columna Acciones de cada CFDI trae otra tabla anidada.
        List<HtmlNode> renglones = tabla?.SelectNodes("./tr|./tbody/tr")?.ToList() ?? new();

        if (renglones.Count == 0)
            return new();

        // Columnas por su encabezado: el orden del portal ha cambiado con el tiempo.
        List<string> encabezados = renglones[0].SelectNodes("./th|./td")?
            .Select(c => Normalizar(Texto(c)))
            .ToList() ?? new();

        int Columna(params string[] palabras) =>
            encabezados.FindIndex(e => palabras.All(e.Contains));

        int iUuid = Columna("folio fiscal");
        int iRfcEmisor = Columna("rfc", "emisor");
        int iNombreEmisor = encabezados.FindIndex(e => e.Contains("emisor") && !e.Contains("rfc"));
        int iRfcReceptor = Columna("rfc", "receptor");
        int iNombreReceptor = encabezados.FindIndex(e => e.Contains("receptor") && !e.Contains("rfc"));
        int iEmision = Columna("fecha", "emision");
        int iCertificacion = Columna("fecha", "certificacion");
        int iTotal = Columna("total");
        int iEfecto = Columna("efecto");
        int iEstado = Columna("estado del comprobante");

        var resultado = new List<CfdiPortalSat>();

        foreach (HtmlNode tr in renglones.Skip(1))
        {
            List<string> celdas = tr.SelectNodes("./td")?.Select(Texto).ToList() ?? new();

            string Celda(int indice) => indice >= 0 && indice < celdas.Count ? celdas[indice] : "";

            string uuid = Celda(iUuid);

            if (!Regex.IsMatch(uuid, "^[0-9A-Fa-f-]{36}$"))
                uuid = Regex.Match(tr.OuterHtml, "[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}").Value;

            if (uuid == "")
                continue;

            Match liga = Regex.Match(HtmlEntity.DeEntitize(tr.OuterHtml), @"RecuperaCfdi\.aspx\?Datos=[^'""\s]+");

            resultado.Add(new CfdiPortalSat
            {
                Uuid = uuid.ToUpperInvariant(),
                RfcEmisor = Celda(iRfcEmisor),
                NombreEmisor = Celda(iNombreEmisor),
                RfcReceptor = Celda(iRfcReceptor),
                NombreReceptor = Celda(iNombreReceptor),
                FechaEmision = Celda(iEmision),
                FechaCertificacion = Celda(iCertificacion),
                Total = Celda(iTotal),
                Efecto = Celda(iEfecto),
                Estado = Celda(iEstado),
                UrlXml = liga.Success ? liga.Value : ""
            });
        }

        return resultado;
    }

    // ---------- formularios ----------

    /// <summary>Valores que mandaría el navegador: inputs (sin botones), radios/checks marcados y la opción elegida de cada select.</summary>
    private void LeerCampos(string html, Dictionary<string, string> campos)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        foreach (HtmlNode input in doc.DocumentNode.SelectNodes("//input[@name]") ?? Enumerable.Empty<HtmlNode>())
        {
            string tipo = input.GetAttributeValue("type", "text").ToLowerInvariant();

            if (tipo is "submit" or "button" or "image" or "reset" or "file")
                continue;

            if (tipo is "radio" or "checkbox" && !input.Attributes.Contains("checked"))
                continue;

            campos[HtmlEntity.DeEntitize(input.GetAttributeValue("name", ""))] =
                HtmlEntity.DeEntitize(input.GetAttributeValue("value", tipo == "checkbox" ? "on" : ""));
        }

        foreach (HtmlNode select in doc.DocumentNode.SelectNodes("//select[@name]") ?? Enumerable.Empty<HtmlNode>())
        {
            string nombre = HtmlEntity.DeEntitize(select.GetAttributeValue("name", ""));
            List<HtmlNode> opciones = select.SelectNodes(".//option")?.ToList() ?? new();

            _opciones[nombre] = opciones.Select(o => HtmlEntity.DeEntitize(o.GetAttributeValue("value", o.InnerText.Trim()))).ToList();

            HtmlNode? elegida = opciones.FirstOrDefault(o => o.Attributes.Contains("selected")) ?? opciones.FirstOrDefault();

            if (elegida is not null)
                campos[nombre] = HtmlEntity.DeEntitize(elegida.GetAttributeValue("value", elegida.InnerText.Trim()));
        }

        foreach (HtmlNode area in doc.DocumentNode.SelectNodes("//textarea[@name]") ?? Enumerable.Empty<HtmlNode>())
            campos[HtmlEntity.DeEntitize(area.GetAttributeValue("name", ""))] = HtmlEntity.DeEntitize(area.InnerText);
    }

    /// <summary>Valor de la opción del select que equivale a <paramref name="numero"/> ("5" o "05", según lo use el portal).</summary>
    private string Opcion(string select, int numero) =>
        _opciones.TryGetValue(select, out List<string>? valores)
            ? valores.FirstOrDefault(v => int.TryParse(v, out int n) && n == numero) ?? numero.ToString(CultureInfo.InvariantCulture)
            : numero.ToString(CultureInfo.InvariantCulture);

    private string Opcion(string select, string valor) =>
        _opciones.TryGetValue(select, out List<string>? valores) && !valores.Contains(valor) && valores.Count > 0
            ? valores[0]
            : valor;

    /// <summary>Envía los formularios de WS-Federation (wresult) que el navegador mandaría solo.</summary>
    private async Task EnviarFormulariosFederacionAsync(string html, Uri actual, CancellationToken ct)
    {
        for (int i = 0; i < 4 && html.Contains("wresult", StringComparison.OrdinalIgnoreCase); i++)
        {
            (html, actual) = await EnviarFormularioAsync(html, actual, "//form[.//input[@name='wresult']]", ct);
        }
    }

    /// <summary>Envía un formulario de la página con sus campos (como el "document.forms[0].submit()" del SAT).</summary>
    private async Task<(string Html, Uri Url)> EnviarFormularioAsync(string html, Uri actual, string xpath, CancellationToken ct)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        HtmlNode? form = doc.DocumentNode.SelectSingleNode(xpath);

        if (form is null)
            return (html, actual);

        var campos = new Dictionary<string, string>();

        foreach (HtmlNode input in form.SelectNodes(".//input[@name]") ?? Enumerable.Empty<HtmlNode>())
            campos[input.GetAttributeValue("name", "")] = HtmlEntity.DeEntitize(input.GetAttributeValue("value", ""));

        string accion = HtmlEntity.DeEntitize(form.GetAttributeValue("action", ""));
        Uri destino = accion == "" ? actual : new Uri(actual, accion);

        using HttpResponseMessage r = await _http.PostAsync(destino, new FormUrlEncodedContent(campos), ct);

        return (await r.Content.ReadAsStringAsync(ct), r.RequestMessage?.RequestUri ?? destino);
    }

    // ---------- utilerías ----------

    private static bool EsLogin(HttpResponseMessage r) =>
        r.RequestMessage?.RequestUri?.Host.StartsWith("cfdiau", StringComparison.OrdinalIgnoreCase) == true;

    private static string MotivoRechazo(string htmlLogin)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(htmlLogin);

        string? mensaje = doc.DocumentNode
            .SelectNodes("//*[@id='msgError' or contains(@class,'error') or contains(@class,'alert')]")?
            .Select(Texto)
            .FirstOrDefault(t => t.Length is > 3 and < 300);

        return string.IsNullOrWhiteSpace(mensaje)
            ? "El SAT no aceptó el inicio de sesión: revisa el RFC, la contraseña (CIEC) y el captcha."
            : $"El SAT no aceptó el inicio de sesión: {mensaje}";
    }

    private static string Texto(HtmlNode nodo) =>
        Regex.Replace(HtmlEntity.DeEntitize(nodo.InnerText), @"\s+", " ").Trim();

    private static string Normalizar(string texto) =>
        new string(texto.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

    private static HttpClient CrearCliente()
    {
        var manejador = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        };

        var cliente = new HttpClient(manejador) { Timeout = TimeSpan.FromMinutes(2) };

        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        cliente.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es-MX,es;q=0.9");

        return cliente;
    }
}

/// <summary>El portal regresó a la pantalla de inicio de sesión: hay que capturar otro captcha.</summary>
public sealed class SesionSatExpiradaException : InvalidOperationException
{
    public SesionSatExpiradaException()
        : base("La sesión del SAT terminó; inicia sesión de nuevo con el captcha.")
    {
    }
}
