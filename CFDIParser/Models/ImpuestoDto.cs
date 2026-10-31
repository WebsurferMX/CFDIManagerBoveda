namespace CFDIParser.Models;

public class ImpuestoDto
{
    public string Tipo { get; set; } =
        "";

    public string Impuesto { get; set; } =
        "";

    public decimal Base { get; set; }

    public string TipoFactor { get; set; } =
        "";

    public decimal Tasa { get; set; }

    public decimal Importe { get; set; }
}