namespace CFDIParser.Models;

public class ImportacionWebResultadoDto
{
    public string Archivo { get; set; } = "";

    public string UUID { get; set; } = "";

    public string Clasificacion { get; set; } = "";

    public string Estatus { get; set; } = "";

    public string Mensaje { get; set; } = "";

    public string RutaXml { get; set; } = "";

    public bool EsCorrecto =>
        Estatus == "OK";

    public bool EsDuplicado =>
        Estatus == "DUPLICADO";

    public bool EsError =>
        Estatus == "ERROR";

    public bool EsNoIdentificado =>
        Estatus == "NO_IDENTIFICADO" ||
        Clasificacion == "NO_IDENTIFICADO";

    public string ClasificacionContable =>
        Clasificacion switch
        {
            "EMITIDO" =>
                "INGRESO",

            "RECIBIDO" =>
                "EGRESO",

            "PROPIO" =>
                "PROPIO",

            "NO_IDENTIFICADO" =>
                "NO IDENTIFICADO",

            _ =>
                Clasificacion
        };
}
