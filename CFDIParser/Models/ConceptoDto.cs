namespace CFDIParser.Models;

public class ConceptoDto
{
    public string ClaveProdServ { get; set; } =
        "";

    public string NoIdentificacion { get; set; } =
        "";

    public decimal Cantidad { get; set; }

    public string ClaveUnidad { get; set; } =
        "";

    public string Unidad { get; set; } =
        "";

    public string Descripcion { get; set; } =
        "";

    public decimal ValorUnitario { get; set; }

    public decimal Importe { get; set; }

    public decimal? Descuento { get; set; }

    public string ObjetoImp { get; set; } =
        "";

    public string CuentaPredial { get; set; } =
        "";

    public List<ImpuestoDto> Impuestos { get; set; } =
        new();
}
