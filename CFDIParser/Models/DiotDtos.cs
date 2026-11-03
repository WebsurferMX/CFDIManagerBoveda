namespace CFDIParser.Models;

/// <summary>
/// CFDI recibido que entra al cálculo de la DIOT desde la bóveda: factura PUE o nota de crédito del mes (con su base
/// e IVA al 16 %), o complemento de pago (el IVA sale de su XML, por fecha de pago).
/// </summary>
public sealed class DiotComprobanteDto
{
    public long CfdiId { get; set; }

    public string Uuid { get; set; } = "";

    /// <summary>I, E o P.</summary>
    public string Tipo { get; set; } = "";

    public string Serie { get; set; } = "";

    public string Folio { get; set; } = "";

    public DateTime FechaEmision { get; set; }

    public string RfcEmisor { get; set; } = "";

    public string NombreEmisor { get; set; } = "";

    public string MetodoPago { get; set; } = "";

    public string Moneda { get; set; } = "";

    public decimal? TipoCambio { get; set; }

    /// <summary>Base y IVA trasladado al 16 % (impuestos globales del comprobante, en su moneda).</summary>
    public decimal Base16 { get; set; }

    public decimal Iva16 { get; set; }

    public string RutaXml { get; set; } = "";
}
