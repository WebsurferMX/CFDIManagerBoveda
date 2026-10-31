namespace CFDIParser.Models;

public class CfdiRelacionadoDto
{
    public string TipoRelacion { get; set; } =
        "";

    public string UUID { get; set; } =
        "";

    // Solo en consultas de Administración de XML: datos del otro comprobante cuando está
    // en la base, y si es él quien relaciona al seleccionado (p. ej. la factura que lo sustituye).
    public bool RelacionaAEste { get; set; }

    public bool EnBase { get; set; }

    public string TipoCfdi { get; set; } = "";

    public string Serie { get; set; } = "";

    public string Folio { get; set; } = "";

    public DateTime? FechaEmision { get; set; }

    public decimal Total { get; set; }

    public string EstadoSat { get; set; } = "";
}
