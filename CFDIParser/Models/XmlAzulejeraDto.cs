namespace CFDIParser.Models;

public class XmlAzulejeraDto
{
    public long CfdiId { get; set; }

    public string UUID { get; set; } = "";

    public string Serie { get; set; } = "";

    public string RfcReceptor { get; set; } = "";

    public string NombreReceptor { get; set; } = "";

    public string RutaXml { get; set; } = "";
}