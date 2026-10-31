namespace CFDIParser.Models;

public class CfdiConsultaDto
{
    public decimal ImporteTotal { get; set; }
    public long CfdiId { get; set; }

    public string UUID { get; set; } = "";

    public string Clasificacion { get; set; } = "";

    public string TipoCfdi { get; set; } = "";

    public DateTime FechaEmision { get; set; }

    public DateTime? FechaTimbrado { get; set; }

    public string Serie { get; set; } = "";

    public string Folio { get; set; } = "";

    public string RfcEmisor { get; set; } = "";

    public string NombreEmisor { get; set; } = "";

    public string RfcReceptor { get; set; } = "";

    public string NombreReceptor { get; set; } = "";

    public string RfcContraparte { get; set; } = "";

    public string NombreContraparte { get; set; } = "";

    public decimal Subtotal { get; set; }

    public decimal Total { get; set; }

    public string Moneda { get; set; } = "";

    public string EstadoSat { get; set; } = "";

    /// <summary>Cuándo se obtuvo el estado SAT (consulta uno por uno o metadatos del SAT); nulo si nunca.</summary>
    public DateTime? FechaValidacionSat { get; set; }

    public string OrigenValidacionSat { get; set; } = "";

    public string RutaXml { get; set; } = "";
}