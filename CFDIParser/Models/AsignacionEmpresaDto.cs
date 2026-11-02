namespace CFDIParser.Models;

/// <summary>Un usuario y una empresa que tiene asignada (reporte "Empresas asignadas por usuario").</summary>
public class AsignacionEmpresaDto
{
    public long UsuarioId { get; set; }
    public string Usuario { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public string Rol { get; set; } = "";
    public bool Activo { get; set; }

    /// <summary>El Administrador ve todas las empresas aunque no tenga asignaciones.</summary>
    public bool EsAdministrador { get; set; }

    /// <summary>Null si el usuario no tiene empresas asignadas.</summary>
    public long? EmpresaId { get; set; }

    public string Rfc { get; set; } = "";
    public string Empresa { get; set; } = "";
    public DateTime? FechaAsignacion { get; set; }
}
