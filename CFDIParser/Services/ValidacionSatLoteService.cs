using CFDIParser.Models;
using CFDIParser.Services;


namespace CFDIParser.Services;

public sealed class ValidacionSatLoteService
{
    private readonly PostgreSqlRepository _repositorio;
    private readonly ConsultaEstadoSatService _consultaSat;

    public ValidacionSatLoteService(
        PostgreSqlRepository repositorio,
        ConsultaEstadoSatService consultaSat)
    {
        _repositorio = repositorio;
        _consultaSat = consultaSat;
    }

    public async Task<ResultadoValidacionSatLote> ValidarPendientesAsync(
        long empresaId,
        int cantidad,
        IProgress<ProgresoValidacionSat>? progreso = null,
        CancellationToken cancellationToken = default)
    {
        if (empresaId <= 0)
        {
            throw new InvalidOperationException(
                "La empresa seleccionada no es válida.");
        }

        int limite = Math.Clamp(cantidad, 1, 100);

        List<CfdiValidacionSatDto> comprobantes =
            _repositorio.ObtenerCfdiPendientesValidacionSat(
                empresaId,
                limite);

        var resultado = new ResultadoValidacionSatLote
        {
            Total = comprobantes.Count
        };

        foreach (CfdiValidacionSatDto cfdi in comprobantes)
        {
            cancellationToken.ThrowIfCancellationRequested();


            if (string.IsNullOrWhiteSpace(cfdi.UUID))
            {
                resultado.Errores++;
                continue;
            }

            EstadoSatDto estado =
                await _consultaSat.ConsultarAsync(
                    cfdi.UUID,
                    cfdi.RfcEmisor,
                    cfdi.RfcReceptor,
                    cfdi.Total,
                    cancellationToken);

            resultado.Procesados++;

            if (estado.ConsultaExitosa)
            {
                _repositorio.ActualizarEstadoSat(
                    empresaId,
                    cfdi.CfdiId,
                    estado);

                resultado.Correctos++;

                switch ((estado.Estado ?? "").Trim().ToUpperInvariant())
                {
                    case "VIGENTE":
                        resultado.Vigentes++;
                        break;

                    case "CANCELADO":
                        resultado.Cancelados++;
                        break;

                    case "NO ENCONTRADO":
                        resultado.NoEncontrados++;
                        break;

                    default:
                        resultado.Otros++;
                        break;
                }
            }
            else
            {
                resultado.Errores++;
            }

            progreso?.Report(
                new ProgresoValidacionSat
                {
                    Procesados = resultado.Procesados,
                    Total = resultado.Total,

                    UUID = cfdi.UUID ?? "",

                    Estado = estado.Estado ?? "",

                    CodigoEstatus =
                        estado.CodigoEstatus ?? "",

                    Mensaje =
                        estado.MensajeError ?? ""
                });

            // Consulta secuencial y pausada para evitar ráfagas al servicio.
            if (resultado.Procesados < resultado.Total)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(400),
                    cancellationToken);
            }
        }

        return resultado;
    }
}

public sealed class ResultadoValidacionSatLote
{
    public int Total { get; set; }
    public int Procesados { get; set; }
    public int Correctos { get; set; }
    public int Vigentes { get; set; }
    public int Cancelados { get; set; }
    public int NoEncontrados { get; set; }
    public int Otros { get; set; }
    public int Errores { get; set; }
}

public sealed class ProgresoValidacionSat
{
    public int Procesados { get; set; }

    public int Total { get; set; }

    public string UUID { get; set; } = "";

    public string Estado { get; set; } = "";

    public string CodigoEstatus { get; set; } = "";

    public string Mensaje { get; set; } = "";

    public double Porcentaje =>
        Total <= 0
            ? 0
            : (double)Procesados / Total * 100d;
}
