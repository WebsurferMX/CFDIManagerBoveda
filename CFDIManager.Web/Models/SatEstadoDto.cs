namespace CFDIManager.Web.Models;

/// <summary>Resultado de abrir y revisar la e.firma configurada de una empresa.</summary>
public class SatEstadoDto
{
    public bool TieneConfiguracion { get; set; }
    public bool ExisteCer { get; set; }
    public bool ExisteKey { get; set; }

    public string? RutaCer { get; set; }
    public string? RutaKey { get; set; }

    public DateTime? FechaVencimiento { get; set; }

    /// <summary>La llave abrió con la contraseña y corresponde al certificado.</summary>
    public bool LlaveValida { get; set; }

    public string RfcCertificado { get; set; } = "";

    public bool RfcCoincide { get; set; }

    /// <summary>Es e.firma (FIEL) y no certificado de sello digital (CSD).</summary>
    public bool EsFiel { get; set; }

    public bool Vigente { get; set; }

    /// <summary>Lista para usarse en la descarga masiva.</summary>
    public bool Lista =>
        LlaveValida && RfcCoincide && EsFiel && Vigente;

    public string Mensaje { get; set; } = "";
}
