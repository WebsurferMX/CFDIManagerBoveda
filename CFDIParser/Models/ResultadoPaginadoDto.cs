namespace CFDIParser.Models;

public class ResultadoPaginadoDto<T>
{
    public List<T> Registros { get; set; } =
        new();

    public long TotalRegistros { get; set; }

    public int PaginaActual { get; set; }

    public int RegistrosPorPagina { get; set; }

    public int TotalPaginas
    {
        get
        {
            if (RegistrosPorPagina <= 0)
            {
                return 0;
            }

            return (int)Math.Ceiling(
                TotalRegistros /
                (decimal)RegistrosPorPagina);
        }
    }

    public bool TienePaginaAnterior =>
        PaginaActual > 1;

    public bool TienePaginaSiguiente =>
        PaginaActual < TotalPaginas;

    public long RegistroInicial
    {
        get
        {
            if (TotalRegistros == 0)
            {
                return 0;
            }

            return
                ((PaginaActual - 1) *
                RegistrosPorPagina) + 1;
        }
    }

    public long RegistroFinal
    {
        get
        {
            long final =
                PaginaActual *
                RegistrosPorPagina;

            return Math.Min(
                final,
                TotalRegistros);
        }
    }
}