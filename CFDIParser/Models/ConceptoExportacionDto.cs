namespace CFDIParser.Models;

public sealed class ConceptoExportacionDto
{
    public long CfdiId { get; set; }
    public long ConceptoId { get; set; }
    public long EmpresaId { get; set; }
    public string Clasificacion { get; set; } = "";
    public string TipoCfdi { get; set; } = "";
    public DateTime FechaEmision { get; set; }
    public DateTime? FechaTimbrado { get; set; }
    public string? Serie { get; set; }
    public string? Folio { get; set; }
    public string UUID { get; set; } = "";
    public string RfcEmisor { get; set; } = "";
    public string? NombreEmisor { get; set; }
    public string RfcReceptor { get; set; } = "";
    public string? NombreReceptor { get; set; }
    public string? ClaveProdServ { get; set; }
    public string? NoIdentificacion { get; set; }
    public string? Descripcion { get; set; }
    public decimal Cantidad { get; set; }
    public string? ClaveUnidad { get; set; }
    public string? Unidad { get; set; }
    public decimal ValorUnitario { get; set; }
    public decimal Importe { get; set; }
    public decimal? Descuento { get; set; }
    public string? ObjetoImp { get; set; }
    public string? CuentaPredial { get; set; }
    public decimal? Iva16 { get; set; }
    public decimal? Iva8 { get; set; }
    public decimal? IvaCero { get; set; }
    public decimal? IvaExento { get; set; }
    public decimal? IvaRetenido { get; set; }
    public decimal? IvaRetenido6 { get; set; }
    public decimal? IsrRetenido { get; set; }
    public decimal? Ieps { get; set; }
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
    public decimal? TotalImpuestos { get; set; }
    public string ArchivoXml { get; set; } = "";
}
