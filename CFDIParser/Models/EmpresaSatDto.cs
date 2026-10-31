namespace CFDIParser.Models;



public class EmpresaSatDto
{
    public long EmpresaId { get; set; }

    public string TipoAccesoSat { get; set; } = "EFIRMA";

    public string? SatUsuario { get; set; }

    public string? SatPassword { get; set; }

    public string? RutaCer { get; set; }

    public string? RutaKey { get; set; }

    public string? PasswordKey { get; set; }

    public DateTime? FechaVencimiento { get; set; }

    public bool UsaValidacionSat { get; set; } = true;

    public bool UsaDescargaSat { get; set; } = true;

    public bool UsaEfos { get; set; } = true;

    public bool UsaXmlAzulejeras { get; set; }

    /// <summary>Descarga masiva periódica con la e.firma (servicio en segundo plano).</summary>
    public bool DescargaAutomatica { get; set; }

    public bool DescargaEmitidos { get; set; } = true;

    public bool DescargaRecibidos { get; set; } = true;

    public bool DescargaMetadatos { get; set; }

    /// <summary>Primer día que pide la descarga automática; después solo pide lo nuevo.</summary>
    public DateTime? DescargaDesde { get; set; }

    /// <summary>Última vez que se probó la CIEC contra el portal del SAT.</summary>
    public DateTime? CiecUltimaPrueba { get; set; }

    /// <summary>Resultado de esa prueba ("Correcta" o el error del SAT).</summary>
    public string? CiecResultado { get; set; }

    public bool TieneCiec =>
        !string.IsNullOrWhiteSpace(SatPassword);

    public bool TieneEFirma =>
        !string.IsNullOrWhiteSpace(RutaCer) &&
        !string.IsNullOrWhiteSpace(RutaKey);
}
