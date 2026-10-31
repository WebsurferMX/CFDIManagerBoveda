namespace CFDIParser.Models;

public class CfdiFiltroDto
{
    public long EmpresaId { get; set; }

    public DateTime FechaInicio { get; set; }

    public DateTime FechaFin { get; set; }

    public string Clasificacion { get; set; } = "";

    public string TipoCfdi { get; set; } = "";

    public string Busqueda { get; set; } = "";

    public int Pagina { get; set; } = 1;

    public int RegistrosPorPagina { get; set; } = 25;

    public string OrdenarPor { get; set; } =
        "fecha_emision";

    public bool OrdenDescendente { get; set; } =
        true;
}