namespace CFDIParser.Models;

public sealed class CfdiExportacionDto
{
    // Identificación interna

    public long CfdiId { get; set; }

    public long EmpresaId { get; set; }

    public string Clasificacion { get; set; } =
        "";

    // Verificación SAT

    public string? VerificadoAsociado { get; set; }

    public string? EstadoSat { get; set; }

    public string? EsCancelable { get; set; }

    public string? EstatusCancelacion { get; set; }

    public string? ValidacionEfos { get; set; }

    public DateTime? FechaValidacionSat { get; set; }

    // Datos principales del CFDI

    public string Tipo { get; set; } =
        "";

    public DateTime FechaEmision { get; set; }

    public DateTime? FechaTimbrado { get; set; }

    public string? Serie { get; set; }

    public string? Folio { get; set; }

    public string UUID { get; set; } =
        "";

    public string? UUIDRelacion { get; set; }

    public string? TipoRelacion { get; set; }

    // Emisor

    public string RfcEmisor { get; set; } =
        "";

    public string? NombreEmisor { get; set; }

    public string? LugarExpedicion { get; set; }

    public string? RegimenFiscalEmisor { get; set; }

    // Receptor

    public string RfcReceptor { get; set; } =
        "";

    public string? NombreReceptor { get; set; }

    public string? ResidenciaFiscal { get; set; }

    public string? NumRegIdTrib { get; set; }

    public string? UsoCfdi { get; set; }

    public string? RegimenFiscalReceptor { get; set; }

    public string? DomicilioFiscalReceptor { get; set; }

    // Importes generales

    public decimal SubTotal { get; set; }

    public decimal? Descuento { get; set; }

    public decimal Total { get; set; }

    public decimal? TotalOriginal { get; set; }

    // Impuestos federales

    public decimal? TotalIeps { get; set; }

    public decimal? Iva16 { get; set; }

    public decimal? Iva8 { get; set; }

    public decimal? RetenidoIva { get; set; }

    public decimal? RetenidoIsr { get; set; }

    public decimal? IvaRetenido6 { get; set; }

    public decimal? TotalTrasladados { get; set; }

    public decimal? TotalRetenidos { get; set; }

    // Impuestos locales

    public decimal? Ish { get; set; }

    public decimal? TotalLocalTrasladado { get; set; }

    public decimal? TotalLocalRetenido { get; set; }

    // Complementos

    public string? Complemento { get; set; }

    public decimal? MontoTotalPagos { get; set; }

    // Moneda y datos de pago

    public string Moneda { get; set; } =
        "";

    public decimal? TipoCambio { get; set; }

    public string? FormaPago { get; set; }

    public string? MetodoPago { get; set; }

    public string? NumCtaPago { get; set; }

    public string? CondicionPago { get; set; }

    // Conceptos y combustibles

    public string? Conceptos { get; set; }

    public string? Combustible { get; set; }

    // Desglose de IEPS

    public decimal? Ieps3 { get; set; }

    public decimal? Ieps6 { get; set; }

    public decimal? Ieps7 { get; set; }

    public decimal? Ieps8 { get; set; }

    public decimal? Ieps9 { get; set; }

    public decimal? Ieps265 { get; set; }

    public decimal? Ieps30 { get; set; }

    public decimal? Ieps304 { get; set; }

    public decimal? Ieps53 { get; set; }

    public decimal? Ieps160 { get; set; }

    // Archivo XML

    public string ArchivoXml { get; set; } =
        "";

    // Domicilio del emisor

    public string? DireccionEmisor { get; set; }

    public string? LocalidadEmisor { get; set; }

    // Domicilio del receptor

    public string? DireccionReceptor { get; set; }

    public string? LocalidadReceptor { get; set; }

    // Datos adicionales

    public string? Exportacion { get; set; }

    public string? VersionCfdi { get; set; }

    public string? NoCertificado { get; set; }

    public string? NoCertificadoSat { get; set; }

    public string? RfcProvCertif { get; set; }

    // Propiedades calculadas

    public bool TieneRelacion =>
        !string.IsNullOrWhiteSpace(
            UUIDRelacion);

    public bool TieneComplemento =>
        !string.IsNullOrWhiteSpace(
            Complemento);

    public bool EsMonedaNacional =>
        string.Equals(
            Moneda,
            "MXN",
            StringComparison.OrdinalIgnoreCase);

    public decimal? TotalCalculadoMxn
    {
        get
        {
            decimal totalBase =
                TotalOriginal
                ?? Total;

            if (EsMonedaNacional)
            {
                return totalBase;
            }

            if (!TipoCambio.HasValue)
            {
                return null;
            }

            return
                totalBase *
                TipoCambio.Value;
        }
    }

    public string EtiquetaVerificadoAsociado
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(
                    VerificadoAsociado))
            {
                return VerificadoAsociado;
            }

            if (!string.IsNullOrWhiteSpace(
                    EstadoSat))
            {
                return EstadoSat;
            }

            if (TieneRelacion)
            {
                return "ASOC.";
            }

            return "PENDIENTE";
        }
    }
}
