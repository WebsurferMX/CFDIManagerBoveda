namespace CFDIManager.Web.Services;

public sealed class XmlAzulejeraDetalleDto
{
    public long CfdiId { get; set; }

    public DateTime FechaEmision { get; set; }

    public string TipoCfdi { get; set; } = "";

    public string UUID { get; set; } = "";

    public string SerieOriginal { get; set; } = "";

    public string SerieNueva { get; set; } = "";

    public string NombreOriginal { get; set; } = "";

    public string NombreNuevo { get; set; } = "";

    public decimal Total { get; set; }

    public string Resultado { get; set; } = "";
}