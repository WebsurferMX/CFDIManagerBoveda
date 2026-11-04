namespace CFDIParser.Models;

/// <summary>Recibo de nómina emitido registrado en la bóveda (el detalle se lee de su XML).</summary>
public sealed class NominaCfdiDto
{
    public long CfdiId { get; set; }

    public string Uuid { get; set; } = "";

    public DateTime FechaEmision { get; set; }

    public string EstadoSat { get; set; } = "";

    public string RutaXml { get; set; } = "";
}
