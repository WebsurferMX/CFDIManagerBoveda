namespace CFDIParser.Models;

public class ImportacionDto
{
    public long ImportacionId { get; set; }

    public string Archivo { get; set; } = "";

    public string UUID { get; set; } = "";

    public DateTime FechaImportacion { get; set; }

    public string Estatus { get; set; } = "";

    public string MensajeError { get; set; } = "";
}