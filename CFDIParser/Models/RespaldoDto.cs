namespace CFDIParser.Models;

/// <summary>Un respaldo (base de datos + Storage) del historial de Herramientas.</summary>
public class RespaldoDto
{
    public long RespaldoId { get; set; }
    public string Equipo { get; set; } = "";

    /// <summary>Diario, Mensual o Manual.</summary>
    public string Tipo { get; set; } = "";

    public DateTime Inicio { get; set; }
    public DateTime? Fin { get; set; }

    /// <summary>En curso, Correcto o Error.</summary>
    public string Estado { get; set; } = "";

    public string Archivo { get; set; } = "";
    public long Tamano { get; set; }
    public string Detalle { get; set; } = "";
}
