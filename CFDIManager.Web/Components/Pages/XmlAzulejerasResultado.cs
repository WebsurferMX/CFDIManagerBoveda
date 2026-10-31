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


}