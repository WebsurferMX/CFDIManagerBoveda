namespace CFDIParser.Models;

/// <summary>CFDI de la bóveda local para la conciliación contra SINUBE (recibido o emitido).</summary>
public class CfdiConciliacionDto
{
    public long CfdiId { get; set; }

    public string Uuid { get; set; } = "";

    public string TipoCfdi { get; set; } = "";

    public DateTime FechaEmision { get; set; }

    public string Serie { get; set; } = "";

    public string Folio { get; set; } = "";

    public decimal Total { get; set; }

    public string Moneda { get; set; } = "";

    public string EstadoSat { get; set; } = "";

    /// <summary>Emisor en recibidos (proveedor) o receptor en emitidos (cliente).</summary>
    public string RfcContraparte { get; set; } = "";

    public string NombreContraparte { get; set; } = "";

    /// <summary>RFC del PAC que certificó (TimbreFiscalDigital/RfcProvCertif).</summary>
    public string RfcPac { get; set; } = "";
}
