namespace CFDIManager.Web.Models;

/// <summary>Un renglón del archivo de metadatos que entrega el SAT.</summary>
public class SatCfdiDto
{
    public string Uuid { get; set; } = "";

    public DateTime Fecha { get; set; }

    public string RfcEmisor { get; set; } = "";

    public string NombreEmisor { get; set; } = "";

    public string RfcReceptor { get; set; } = "";

    public string NombreReceptor { get; set; } = "";

    public string Tipo { get; set; } = "";

    public decimal Monto { get; set; }

    /// <summary>Vigente o Cancelado.</summary>
    public string Estatus { get; set; } = "";

    public DateTime? FechaCancelacion { get; set; }

    /// <summary>El UUID ya está registrado en CFDI Manager para la empresa.</summary>
    public bool YaImportado { get; set; }
}
