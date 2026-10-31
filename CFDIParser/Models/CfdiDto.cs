namespace CFDIParser.Models;

public class CfdiDto
{
    // Datos generales del comprobante

    public string Version { get; set; } =
        "";

    public string Serie { get; set; } =
        "";

    public string Folio { get; set; } =
        "";

    public DateTime FechaEmision { get; set; }

    public string TipoComprobante { get; set; } =
        "";

    public string Moneda { get; set; } =
        "";

    public decimal? TipoCambio { get; set; }

    public decimal SubTotal { get; set; }

    public decimal? Descuento { get; set; }

    public decimal Total { get; set; }

    public string FormaPago { get; set; } =
        "";

    public string MetodoPago { get; set; } =
        "";

    public string CondicionesDePago { get; set; } =
        "";

    public string LugarExpedicion { get; set; } =
        "";

    public string Exportacion { get; set; } =
        "";

    public string NoCertificado { get; set; } =
        "";

    public string Certificado { get; set; } =
        "";

    public string Sello { get; set; } =
        "";

    public string Confirmacion { get; set; } =
        "";

    // Datos del emisor

    public string RFCEmisor { get; set; } =
        "";

    public string NombreEmisor { get; set; } =
        "";

    public string RegimenFiscalEmisor { get; set; } =
        "";

    public string FacAtrAdquirente { get; set; } =
        "";

    // Datos del receptor

    public string RFCReceptor { get; set; } =
        "";

    public string NombreReceptor { get; set; } =
        "";

    public string DomicilioFiscalReceptor { get; set; } =
        "";

    public string RegimenFiscalReceptor { get; set; } =
        "";

    public string UsoCfdi { get; set; } =
        "";

    public string ResidenciaFiscal { get; set; } =
        "";

    public string NumRegIdTrib { get; set; } =
        "";

    // Impuestos globales

    public decimal? TotalImpuestosTrasladados { get; set; }

    public decimal? TotalImpuestosRetenidos { get; set; }

    // Timbre fiscal digital

    public string UUID { get; set; } =
        "";

    public DateTime? FechaTimbrado { get; set; }

    public string RfcProvCertif { get; set; } =
        "";

    public string NoCertificadoSat { get; set; } =
        "";

    public string VersionTimbre { get; set; } =
        "";

    public string SelloCfd { get; set; } =
        "";

    public string SelloSat { get; set; } =
        "";

    // Complemento de pagos

    public decimal? MontoTotalPagos { get; set; }

    // Elementos relacionados

    public List<ConceptoDto> Conceptos { get; set; } =
        new();

    public List<ImpuestoDto> Impuestos { get; set; } =
        new();

    public List<CfdiRelacionadoDto> Relacionados { get; set; } =
        new();
}

