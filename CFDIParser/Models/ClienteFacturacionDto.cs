namespace CFDIParser.Models;

public class ClienteFacturacionDto
{
    public string RFC { get; set; } = "";

    public string Nombre { get; set; } = "";

    public long CantidadCfdi { get; set; }

    public decimal TotalFacturado { get; set; }

    public DateTime PrimeraFactura { get; set; }

    public DateTime UltimaFactura { get; set; }
}