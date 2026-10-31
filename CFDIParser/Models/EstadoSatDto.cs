namespace CFDIParser.Models;

public sealed class EstadoSatDto
{
    public string CodigoEstatus { get; set; } =
        "";

    public string Estado { get; set; } =
        "";

    public string EsCancelable { get; set; } =
        "";

    public string EstatusCancelacion { get; set; } =
        "";

    public string ValidacionEfos { get; set; } =
        "";

    public DateTime FechaConsulta { get; set; } =
        DateTime.Now;

    public bool ConsultaExitosa { get; set; }

    public string? MensajeError { get; set; }
}