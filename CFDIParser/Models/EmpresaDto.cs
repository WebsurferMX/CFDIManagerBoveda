namespace CFDIParser.Models;

public class EmpresaDto
{
    public long EmpresaId { get; set; }

    public string Rfc { get; set; } = "";

    public string RazonSocial { get; set; } = "";

    public string NombreCorto { get; set; } = "";

    public string RutaXml { get; set; } = "";

    public bool Activa { get; set; }

    public string NombreParaMostrar =>
        string.IsNullOrWhiteSpace(NombreCorto)
            ? RazonSocial
            : NombreCorto;

    public bool TieneFiel { get; set; }            

    public DateTime? FechaVencimientoFiel { get; set; }

    
}
