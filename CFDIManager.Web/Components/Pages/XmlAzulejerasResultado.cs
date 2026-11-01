namespace CFDIManager.Web.Services;

public sealed class XmlAzulejerasResultado
{
    public string NombreArchivo { get; set; } = "";

    public string TipoContenido { get; set; } =
        "application/zip";

    public byte[] Contenido { get; set; } =
        Array.Empty<byte>();

    public int Leidos { get; set; }

    public int Modificados { get; set; }

    public int SinCambios { get; set; }

    public int Errores { get; set; }

    public decimal ImporteTotal { get; set; }

    public decimal ImporteModificados { get; set; }

    public decimal ImporteSinCambios { get; set; }

    public List<XmlAzulejeraDetalleDto> Detalles { get; set; } = new();

    /// <summary>Carpeta del servidor con los XML modificados (Storage\Azulejeras\RFC\fecha), para subirlos a SINUBE.</summary>
    public string Carpeta { get; set; } = "";

    /// <summary>XML modificados guardados en <see cref="Carpeta"/>.</summary>
    public List<string> Archivos { get; set; } = new();
}

/// <summary>Una serie de los emitidos del periodo (botón Analizar de XML Azulejeras).</summary>
public sealed class SerieAzulejeraDto
{
    public string Serie { get; set; } = "";

    /// <summary>Comprobantes por tipo (I, E, P, T, N).</summary>
    public Dictionary<string, int> PorTipo { get; set; } = new();

    public int Total => PorTipo.Values.Sum();

    /// <summary>Comprobantes a público general (XAXX010101000): los que se modifican.</summary>
    public int PublicoGeneral { get; set; }

    public decimal ImportePublicoGeneral { get; set; }

    /// <summary>Serie a la que se convierte; vacía si la serie no tiene equivalencia.</summary>
    public string SerieNueva { get; set; } = "";

    public string NombreReceptor { get; set; } = "";

    public bool TieneEquivalencia => SerieNueva != "";

}