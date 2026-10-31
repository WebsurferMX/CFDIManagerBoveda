using System.Net.Http.Headers;
using System.Text.Json;

namespace CFDIManager.Web.Services.Sat;

/// <summary>
/// API de la app SAT Móvil: con RFC y CIEC entrega en PDF la opinión de cumplimiento y la
/// constancia de situación fiscal, sin captcha (así lo hace onefacture).
/// </summary>
public sealed class SatMovilCliente
{
    private const string UrlBase = "https://sm-sat-movilzuul-sm-servicios-moviles.cnh.cloudb.sat.gob.mx/satmovil/v1";

    public const string Opinion = "opinionc";
    public const string Constancia = "csf";

    private readonly IHttpClientFactory _fabrica;

    public SatMovilCliente(IHttpClientFactory fabrica)
    {
        _fabrica = fabrica;
    }

    /// <summary>PDF del documento (<see cref="Opinion"/> o <see cref="Constancia"/>) del RFC.</summary>
    public async Task<byte[]> DescargarAsync(string rfc, string ciec, string documento, CancellationToken ct = default)
    {
        rfc = rfc.Trim().ToUpperInvariant();
        HttpClient http = _fabrica.CreateClient("SatMovil");
        string token = await TokenAsync(http, rfc, ciec, ct);

        // El SAT a veces responde sin el documento la primera vez; onefacture reintenta 3 veces.
        for (int intento = 1; ; intento++)
        {
            using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"{UrlBase}/{documento}/{Uri.EscapeDataString(rfc)}");
            solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using HttpResponseMessage respuesta = await http.SendAsync(solicitud, ct);
            string cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            if (LeerDocumento(cuerpo) is byte[] pdf)
                return pdf;

            if (intento >= 3)
                throw new InvalidOperationException($"El SAT no entregó el documento ({(int)respuesta.StatusCode}). Intenta más tarde.");

            await Task.Delay(1500, ct);
        }
    }

    private static async Task<string> TokenAsync(HttpClient http, string rfc, string ciec, CancellationToken ct)
    {
        string url = $"{UrlBase}/smnovel?grant_type=password&username={Uri.EscapeDataString(rfc)}" +
                     $"&password={Uri.EscapeDataString(ciec)}&scope=satmovil&resourceServer=satmovil";

        // Sin reintentos: repetir una contraseña equivocada puede bloquear la CIEC.
        using HttpResponseMessage respuesta = await http.PostAsync(url, null, ct);
        string cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        try
        {
            using JsonDocument json = JsonDocument.Parse(cuerpo);

            if (json.RootElement.TryGetProperty("access_token", out JsonElement token) && token.GetString() is { Length: > 0 } valor)
                return valor;
        }
        catch (JsonException)
        {
            // Respuesta que no es JSON: se informa abajo.
        }

        throw new InvalidOperationException(
            (int)respuesta.StatusCode is 400 or 401
                ? "El SAT no aceptó el RFC y la contraseña (CIEC). Revísala en Accesos SAT."
                : $"No se pudo acceder al SAT Móvil ({(int)respuesta.StatusCode}). Intenta más tarde.");
    }

    /// <summary>{"bodyResponse": {"constancia": "&lt;PDF en base64&gt;", ...}}</summary>
    private static byte[]? LeerDocumento(string cuerpo)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(cuerpo);

            if (json.RootElement.TryGetProperty("bodyResponse", out JsonElement body) &&
                body.ValueKind == JsonValueKind.Object &&
                body.TryGetProperty("constancia", out JsonElement constancia) &&
                constancia.GetString() is { Length: > 0 } base64)
            {
                return Convert.FromBase64String(base64);
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            // Respuesta incompleta: se reintenta.
        }

        return null;
    }
}
