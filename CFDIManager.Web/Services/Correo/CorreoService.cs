using CFDIParser.Models;
using CFDIParser.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using MimeKit;

namespace CFDIManager.Web.Services.Correo;

public sealed record AdjuntoCorreo(string Nombre, string TipoContenido, byte[] Contenido);

public sealed class MensajeCorreo
{
    public string Tipo { get; init; } = "General";

    public IReadOnlyList<string> Para { get; init; } = [];

    public IReadOnlyList<string> Cc { get; init; } = [];

    public string Asunto { get; init; } = "";

    public string CuerpoHtml { get; init; } = "";

    public string CuerpoTexto { get; init; } = "";

    public IReadOnlyList<AdjuntoCorreo> Adjuntos { get; init; } = [];

    /// <summary>Usuario que envía (bitácora).</summary>
    public string EnviadoPor { get; init; } = "";

    public long? EmpresaId { get; init; }
}

// Envío de correos por el servidor SMTP de Configuración → Correo. Todo envío, exitoso o no,
// queda en la tabla correos_enviados.
public sealed class CorreoService(PostgreSqlRepository repo, IDataProtectionProvider proteccion)
{
    private const string Prefijo = "dp:";

    /// <summary>Límite de adjuntos de la mayoría de los servidores (Gmail y Outlook: 20–25 MB).</summary>
    public const long LimiteAdjuntosBytes = 20L * 1024 * 1024;

    private readonly IDataProtector _protector = proteccion.CreateProtector("CFDIManager.Smtp");

    public ConfiguracionCorreoDto ObtenerConfiguracion() => repo.ObtenerConfiguracionCorreo();

    public bool EstaConfigurado()
    {
        try
        {
            return repo.ObtenerConfiguracionCorreo().EstaConfigurado;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Guarda la configuración; con passwordNueva vacía se conserva la contraseña guardada.</summary>
    public void GuardarConfiguracion(ConfiguracionCorreoDto configuracion, string? passwordNueva)
    {
        if (!string.IsNullOrEmpty(passwordNueva))
            configuracion.Password = Prefijo + _protector.Protect(passwordNueva);
        else
            configuracion.Password = repo.ObtenerConfiguracionCorreo().Password;

        repo.GuardarConfiguracionCorreo(configuracion);
    }

    public static bool EsCorreoValido(string? correo) =>
        !string.IsNullOrWhiteSpace(correo) &&
        MailboxAddress.TryParse(correo.Trim(), out MailboxAddress? direccion) &&
        direccion.Address.Contains('@');

    /// <summary>Separa "a@x.com; b@y.com, c@z.com" en direcciones; lanza si alguna no es válida.</summary>
    public static List<string> SepararCorreos(string? texto)
    {
        List<string> correos = (texto ?? "")
            .Split([',', ';', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? invalido = correos.FirstOrDefault(c => !EsCorreoValido(c));

        if (invalido is not null)
            throw new InvalidOperationException($"La dirección '{invalido}' no es un correo válido.");

        return correos;
    }

    /// <summary>Envía el mensaje. Con configuracionPrueba se usa esa configuración en vez de la guardada.</summary>
    public async Task EnviarAsync(
        MensajeCorreo mensaje,
        ConfiguracionCorreoDto? configuracionPrueba = null,
        string? passwordPrueba = null,
        CancellationToken cancelacion = default)
    {
        ConfiguracionCorreoDto configuracion = configuracionPrueba ?? repo.ObtenerConfiguracionCorreo();
        string? error = null;

        try
        {
            if (configuracionPrueba is null && !configuracion.EstaConfigurado)
                throw new InvalidOperationException("El envío de correos no está configurado. Configúralo en Configuración → Correo.");

            if (mensaje.Para.Count == 0)
                throw new InvalidOperationException("Indica al menos un destinatario.");

            if (mensaje.Adjuntos.Sum(a => (long)a.Contenido.Length) > LimiteAdjuntosBytes)
                throw new InvalidOperationException("Los adjuntos superan 20 MB; reduce el periodo del reporte.");

            string? password = !string.IsNullOrEmpty(passwordPrueba)
                ? passwordPrueba
                : DescifrarPassword(configuracion.Password);

            using MimeMessage correo = CrearMensaje(configuracion, mensaje);
            using var cliente = new SmtpClient { Timeout = 60_000 };

            await cliente.ConnectAsync(configuracion.Servidor, configuracion.Puerto, OpcionSeguridad(configuracion.Seguridad), cancelacion);

            if (!string.IsNullOrWhiteSpace(configuracion.Usuario))
                await cliente.AuthenticateAsync(configuracion.Usuario, password ?? "", cancelacion);

            await cliente.SendAsync(correo, cancelacion);
            await cliente.DisconnectAsync(true, cancelacion);
        }
        catch (AuthenticationException)
        {
            error = "El servidor rechazó el usuario o la contraseña del correo.";
            throw new InvalidOperationException(error);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or System.Net.Sockets.SocketException or SslHandshakeException)
        {
            error = $"No se pudo enviar el correo: {ex.Message}";
            throw new InvalidOperationException(error, ex);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            Registrar(mensaje, error);
        }
    }

    private static MimeMessage CrearMensaje(ConfiguracionCorreoDto configuracion, MensajeCorreo mensaje)
    {
        var correo = new MimeMessage();

        correo.From.Add(new MailboxAddress(configuracion.RemitenteNombre, configuracion.RemitenteEmail));
        correo.To.AddRange(mensaje.Para.Select(MailboxAddress.Parse));
        correo.Cc.AddRange(mensaje.Cc.Select(MailboxAddress.Parse));
        correo.Subject = mensaje.Asunto;

        var cuerpo = new BodyBuilder
        {
            HtmlBody = mensaje.CuerpoHtml,
            TextBody = mensaje.CuerpoTexto
        };

        foreach (AdjuntoCorreo adjunto in mensaje.Adjuntos)
            cuerpo.Attachments.Add(adjunto.Nombre, adjunto.Contenido, ContentType.Parse(adjunto.TipoContenido));

        correo.Body = cuerpo.ToMessageBody();
        return correo;
    }

    private static SecureSocketOptions OpcionSeguridad(string seguridad) => seguridad switch
    {
        "SSL" => SecureSocketOptions.SslOnConnect,
        "STARTTLS" => SecureSocketOptions.StartTls,
        "NINGUNA" => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };

    private string? DescifrarPassword(string? guardada)
    {
        if (string.IsNullOrEmpty(guardada) || !guardada.StartsWith(Prefijo))
            return guardada;

        try
        {
            return _protector.Unprotect(guardada[Prefijo.Length..]);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new InvalidOperationException(
                "No se pudo leer la contraseña del correo (cambiaron las llaves de cifrado). Captúrala de nuevo en Configuración → Correo.");
        }
    }

    private void Registrar(MensajeCorreo mensaje, string? error)
    {
        try
        {
            repo.RegistrarCorreoEnviado(new CorreoEnviadoDto
            {
                Tipo = mensaje.Tipo,
                Destinatarios = string.Join(", ", mensaje.Para.Concat(mensaje.Cc)),
                Asunto = mensaje.Asunto,
                Adjuntos = string.Join(", ", mensaje.Adjuntos.Select(a => a.Nombre)),
                Enviado = error is null,
                Error = error ?? "",
                EnviadoPor = mensaje.EnviadoPor,
                EmpresaId = mensaje.EmpresaId
            });
        }
        catch (Exception)
        {
            // La bitácora no debe ocultar el resultado del envío.
        }
    }
}
