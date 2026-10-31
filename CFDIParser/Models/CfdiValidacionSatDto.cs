namespace CFDIParser.Models;

public sealed class CfdiValidacionSatDto
{
    public long CfdiId { get; set; }

    public long EmpresaId { get; set; }

    public string UUID { get; set; } =
        "";

    public string RfcEmisor { get; set; } =
        "";

    public string RfcReceptor { get; set; } =
        "";

    public decimal Total { get; set; }

    public string? EstadoSat { get; set; }

    public DateTime? FechaValidacionSat { get; set; }
}
