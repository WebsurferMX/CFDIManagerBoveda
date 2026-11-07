using System.Text.RegularExpressions;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>Logo por RFC emisor para la representación impresa: Storage/Logos/{RFC}.png|.jpg.</summary>
public partial class CatalogoLogosService
{
    private readonly string _carpeta;

    private readonly IHttpClientFactory? _http;

    public CatalogoLogosService(IWebHostEnvironment ambiente, IHttpClientFactory? http = null)
    {
        _carpeta = CarpetaStorage.Ruta("Logos");
        _http = http;
    }

    [GeneratedRegex("""<img[^>]+src\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ImagenHtml();

    /// <summary>
    /// Logo desde la dirección de la imagen o desde el HTML del formato de impresión de SINUBE (toma el primer
    /// &lt;img src&gt;). Solo direcciones públicas http/https.
    /// </summary>
    public async Task ImportarAsync(string rfc, string direccionOHtml, CancellationToken ct = default)
    {
        string texto = (direccionOHtml ?? "").Trim();
        string url = ImagenHtml().Match(texto) is { Success: true } m ? m.Groups[1].Value : texto;

        if (!Uri.TryCreate(System.Net.WebUtility.HtmlDecode(url), UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Pega la dirección (http/https) de la imagen o el HTML del formato con su <img src=…>.");

        // No se descargan direcciones de la red interna.
        System.Net.IPAddress[] ips = await System.Net.Dns.GetHostAddressesAsync(uri.Host, ct);
        if (ips.Length == 0 || ips.Any(EsInterna))
            throw new InvalidOperationException("La dirección del logo debe ser de internet.");

        using HttpClient cliente = _http?.CreateClient() ?? new HttpClient();
        cliente.Timeout = TimeSpan.FromSeconds(20);
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, uri);
        // PNG o JPG (no WebP, que no se usa en el PDF).
        solicitud.Headers.Accept.ParseAdd("image/png,image/jpeg;q=0.9");
        using HttpResponseMessage respuesta = await cliente.SendAsync(solicitud, HttpCompletionOption.ResponseHeadersRead, ct);
        respuesta.EnsureSuccessStatusCode();

        if (respuesta.Content.Headers.ContentLength is > 5 * 1024 * 1024)
            throw new InvalidOperationException("El logo pesa más de 5 MB.");

        byte[] imagen = await respuesta.Content.ReadAsByteArrayAsync(ct);
        Guardar(rfc, imagen);
    }

    private static bool EsInterna(System.Net.IPAddress ip)
    {
        if (System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            return true;

        byte[] b = ip.MapToIPv4().GetAddressBytes();
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork || ip.IsIPv4MappedToIPv6
            ? b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) ||
              (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127) || b[0] == 0
            : false;
    }

    [GeneratedRegex("^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$")]
    private static partial Regex RfcValido();

    public List<string> Listar()
    {
        return Directory.Exists(_carpeta)
            ? Directory.EnumerateFiles(_carpeta)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Order()
                .ToList()
            : new();
    }

    public byte[]? Leer(string rfc)
    {
        string? ruta = Ruta(rfc);
        return ruta is null ? null : File.ReadAllBytes(ruta);
    }

    public string? TipoContenido(string rfc) =>
        Ruta(rfc) is string r ? (r.EndsWith(".png") ? "image/png" : "image/jpeg") : null;

    public void Guardar(string rfc, byte[] imagen)
    {
        rfc = rfc.Trim().ToUpperInvariant();

        if (!RfcValido().IsMatch(rfc))
        {
            throw new InvalidOperationException("RFC inválido.");
        }

        string? extension =
            imagen.Length > 4 && imagen[0] == 0x89 && imagen[1] == 0x50 && imagen[2] == 0x4E && imagen[3] == 0x47 ? ".png"
            : imagen.Length > 2 && imagen[0] == 0xFF && imagen[1] == 0xD8 ? ".jpg"
            : null;

        if (extension is null)
        {
            throw new InvalidOperationException("El logo debe ser PNG o JPG.");
        }

        Directory.CreateDirectory(_carpeta);
        Quitar(rfc);
        File.WriteAllBytes(Path.Combine(_carpeta, rfc + extension), imagen);
    }

    public void Quitar(string rfc)
    {
        if (Ruta(rfc) is string ruta)
        {
            File.Delete(ruta);
        }
    }

    private string? Ruta(string rfc)
    {
        rfc = (rfc ?? "").Trim().ToUpperInvariant();

        if (!RfcValido().IsMatch(rfc))
        {
            return null;
        }

        return new[] { ".png", ".jpg" }
            .Select(ext => Path.Combine(_carpeta, rfc + ext))
            .FirstOrDefault(File.Exists);
    }
}
