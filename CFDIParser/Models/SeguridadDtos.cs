namespace CFDIParser.Models;

// Usuario de CFDI Manager. Es también el TUser de ASP.NET Identity (ver UsuarioStore en la web).
public class UsuarioDto
{
    public long UsuarioId { get; set; }

    public string NombreUsuario { get; set; } = "";

    public string NombreUsuarioNormalizado { get; set; } = "";

    public string NombreCompleto { get; set; } = "";

    public string Email { get; set; } = "";

    public string? PasswordHash { get; set; }

    public string? SecurityStamp { get; set; }

    public string? ConcurrencyStamp { get; set; }

    public bool LockoutHabilitado { get; set; } = true;

    public DateTimeOffset? LockoutFin { get; set; }

    public int AccesosFallidos { get; set; }

    public bool Activo { get; set; } = true;

    // Contraseña temporal (alta o restablecimiento): se pide cambiarla al iniciar sesión.
    public bool DebeCambiarPassword { get; set; }

    public DateTime FechaAlta { get; set; }

    public DateTime? UltimoAcceso { get; set; }

    // Solo para el listado de usuarios.
    public string Rol { get; set; } = "";

    public int TotalEmpresas { get; set; }

    /// <summary>Tareas del rol que se le quitaron (usuario_tareas_denegadas).</summary>
    public int TareasDenegadas { get; set; }

    public string NombreParaMostrar =>
        string.IsNullOrWhiteSpace(NombreCompleto)
            ? NombreUsuario
            : NombreCompleto;
}

public class RolDto
{
    public long RolId { get; set; }

    public string Nombre { get; set; } = "";

    public string NombreNormalizado { get; set; } = "";

    public string Descripcion { get; set; } = "";

    public string? ConcurrencyStamp { get; set; }
}
