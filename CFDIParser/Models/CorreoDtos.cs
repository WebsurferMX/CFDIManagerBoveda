namespace CFDIParser.Models;

// Servidor SMTP de la aplicación (un solo registro, Configuración → Correo).
public class ConfiguracionCorreoDto
{
    public string Servidor { get; set; } = "";

    public int Puerto { get; set; } = 587;

    /// <summary>AUTO, SSL, STARTTLS o NINGUNA.</summary>
    public string Seguridad { get; set; } = "AUTO";

    public string Usuario { get; set; } = "";

    /// <summary>Cifrada con Data Protection (prefijo "dp:").</summary>
    public string? Password { get; set; }

    public string RemitenteEmail { get; set; } = "";

    public string RemitenteNombre { get; set; } = "CFDI Manager";

    /// <summary>Dirección con la que los usuarios abren la aplicación; va en los correos de bienvenida.</summary>
    public string UrlAplicacion { get; set; } = "";

    public bool Habilitado { get; set; }

    public DateTime? FechaActualizacion { get; set; }

    public bool EstaConfigurado =>
        Habilitado &&
        !string.IsNullOrWhiteSpace(Servidor) &&
        !string.IsNullOrWhiteSpace(RemitenteEmail);
}

// Bitácora de correos enviados.
public class CorreoEnviadoDto
{
    public long CorreoId { get; set; }

    public DateTime Fecha { get; set; }

    public string Tipo { get; set; } = "";

    public string Destinatarios { get; set; } = "";

    public string Asunto { get; set; } = "";

    public string Adjuntos { get; set; } = "";

    public bool Enviado { get; set; }

    public string Error { get; set; } = "";

    public string EnviadoPor { get; set; } = "";

    public long? EmpresaId { get; set; }
}
