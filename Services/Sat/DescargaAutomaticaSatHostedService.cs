using System.Collections.Concurrent;
using CFDIManager.Web.Models;
using CFDIManager.Web.Services.Interfaces;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Sat;

/// <summary>Resultado del último ciclo de descarga automática de una empresa.</summary>
public sealed class ResultadoDescargaAutomatica
{
    public long EmpresaId { get; init; }

    public string Rfc { get; init; } = "";

    public DateTime Fin { get; set; }

    public int SolicitudesNuevas { get; set; }

    public int SolicitudesDescargadas { get; set; }

    /// <summary>Solicitudes automáticas que el SAT aún no termina.</summary>
    public int Pendientes { get; set; }

    public int Importados { get; set; }

    public string Aviso { get; set; } = "";
}

/// <summary>Último resultado por empresa y disparador de ciclos manuales.</summary>
public class EstadoDescargaAutomaticaSat
{
    private readonly SemaphoreSlim _ahora = new(0, 1);

    public ConcurrentDictionary<long, ResultadoDescargaAutomatica> PorEmpresa { get; } = new();

    public bool EnCurso { get; set; }

    public DateTime? UltimaPasada { get; set; }

    public DateTime? SiguientePasada { get; set; }

    public event Action? Cambio;

    public void Notificar() => Cambio?.Invoke();

    /// <summary>Pide un ciclo inmediato; si ya hay uno pedido, no hace nada.</summary>
    public void SolicitarAhora()
    {
        if (_ahora.CurrentCount == 0)
        {
            _ahora.Release();
        }
    }

    internal Task EsperarSolicitudAsync(TimeSpan maximo, CancellationToken ct) => _ahora.WaitAsync(maximo, ct);
}

/// <summary>
/// Descarga masiva automática para las empresas con "Descargar automáticamente del SAT"
/// (Empresas → Acceso SAT o el switch de Administración de XML). Cada
/// DescargaAutomaticaSat:Minutos (30 por omisión):
/// 1. verifica las solicitudes automáticas pendientes y descarga las terminadas (los XML
///    se importan a RFC\Emitidos|Recibidos\AAAA\MM como cualquier importación);
/// 2. pide al SAT, mes por mes, los periodos desde "Descargar desde" hasta ayer que
///    ninguna solicitud automática cubre. La primera vez es un mes por solicitud; después
///    solo lo nuevo. Un periodo rechazado o vencido se vuelve a pedir en el siguiente ciclo.
/// </summary>
public class DescargaAutomaticaSatHostedService : BackgroundService
{
    /// <summary>Estados con los que una solicitud ya cubre su periodo.</summary>
    private static readonly HashSet<string> EstadosVigentes =
        new() { "Aceptada", "En proceso", "Terminada", "Descargada" };

    private readonly IServiceScopeFactory _scopes;
    private readonly EstadoDescargaAutomaticaSat _estado;
    private readonly ILogger<DescargaAutomaticaSatHostedService> _log;
    private readonly TimeSpan _intervalo;
    private readonly int _maximoSolicitudes;

    public DescargaAutomaticaSatHostedService(
        IServiceScopeFactory scopes,
        EstadoDescargaAutomaticaSat estado,
        IConfiguration configuracion,
        ILogger<DescargaAutomaticaSatHostedService> log)
    {
        _scopes = scopes;
        _estado = estado;
        _log = log;
        _intervalo = TimeSpan.FromMinutes(Math.Max(5, configuracion.GetValue("DescargaAutomaticaSat:Minutos", 30)));
        _maximoSolicitudes = Math.Max(1, configuracion.GetValue("DescargaAutomaticaSat:SolicitudesPorCiclo", 12));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Después del arranque y de la primera sincronización de Storage.
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        while (!ct.IsCancellationRequested)
        {
            await PasadaAsync(ct);

            _estado.SiguientePasada = DateTime.Now + _intervalo;
            _estado.Notificar();

            try
            {
                await _estado.EsperarSolicitudAsync(_intervalo, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PasadaAsync(CancellationToken ct)
    {
        _estado.EnCurso = true;
        _estado.Notificar();

        try
        {
            using IServiceScope scope = _scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();
            var descargas = scope.ServiceProvider.GetRequiredService<ISatDescargaService>();

            foreach (EmpresaDto empresa in repo.ObtenerEmpresasActivas())
            {
                ct.ThrowIfCancellationRequested();

                EmpresaSatDto? sat = repo.ObtenerEmpresaSat(empresa.EmpresaId);

                if (sat is not { DescargaAutomatica: true })
                {
                    _estado.PorEmpresa.TryRemove(empresa.EmpresaId, out _);
                    continue;
                }

                var resultado = new ResultadoDescargaAutomatica { EmpresaId = empresa.EmpresaId, Rfc = empresa.Rfc };

                try
                {
                    await CicloEmpresaAsync(empresa, sat, descargas, resultado, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Falló la descarga automática del SAT de {Rfc}", empresa.Rfc);
                    resultado.Aviso = $"Error: {ex.Message}";
                }

                resultado.Fin = DateTime.Now;
                _estado.PorEmpresa[empresa.EmpresaId] = resultado;
                _estado.Notificar();
            }

            _estado.UltimaPasada = DateTime.Now;
        }
        catch (OperationCanceledException)
        {
            // La app se está cerrando.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falló la descarga automática del SAT");
        }
        finally
        {
            _estado.EnCurso = false;
            _estado.Notificar();
        }
    }

    private async Task CicloEmpresaAsync(
        EmpresaDto empresa,
        EmpresaSatDto sat,
        ISatDescargaService descargas,
        ResultadoDescargaAutomatica resultado,
        CancellationToken ct)
    {
        SatEstadoDto fiel = await descargas.ValidarConfiguracionAsync(empresa);

        if (!fiel.LlaveValida || !fiel.EsFiel || !fiel.RfcCoincide || !fiel.Vigente)
        {
            resultado.Aviso = fiel.Mensaje;
            return;
        }

        List<SolicitudSatDto> automaticas =
            (await descargas.ListarSolicitudesAsync(empresa.EmpresaId)).Where(s => s.Automatica).ToList();

        // 1. Avanzar lo que ya se pidió.
        foreach (SolicitudSatDto pendiente in automaticas.Where(s => s.Pendiente || s.ListaParaDescargar).ToList())
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                SolicitudSatDto s = pendiente.Pendiente
                    ? await descargas.VerificarAsync(empresa, pendiente.Id, ct)
                    : pendiente;

                if (s.ListaParaDescargar)
                {
                    s = await descargas.DescargarAsync(empresa, s.Id, cancelacion: ct);
                    resultado.SolicitudesDescargadas++;
                    resultado.Importados += s.Importados;
                }

                Reemplazar(automaticas, s);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Solicitud automática {Id} de {Rfc}", pendiente.IdSolicitudSat, empresa.Rfc);
                resultado.Aviso = ex.Message;
            }
        }

        // 2. Pedir los periodos que falten, mes por mes.
        DateTime desde = (sat.DescargaDesde ?? new DateTime(DateTime.Today.Year, 1, 1)).Date;
        DateTime ayer = DateTime.Today.AddDays(-1);

        foreach ((bool recibidos, bool metadatos) in Tipos(sat))
        {
            List<SolicitudSatDto> cubiertas = automaticas
                .Where(s => s.Recibidos == recibidos && s.Metadatos == metadatos && CubrePeriodo(s))
                .ToList();

            foreach ((DateTime inicio, DateTime fin) in PeriodosFaltantes(desde, ayer, cubiertas))
            {
                if (resultado.SolicitudesNuevas >= _maximoSolicitudes)
                {
                    break;
                }

                ct.ThrowIfCancellationRequested();

                try
                {
                    SolicitudSatDto nueva = await descargas.CrearSolicitudAsync(
                        empresa,
                        new ParametrosSolicitudSat
                        {
                            FechaInicial = inicio,
                            FechaFinal = fin.AddDays(1).AddSeconds(-1),
                            Recibidos = recibidos,
                            Metadatos = metadatos
                        },
                        ct,
                        automatica: true);

                    automaticas.Add(nueva);
                    resultado.SolicitudesNuevas++;

                    if (nueva.Estado == "Rechazada")
                    {
                        resultado.Aviso = $"{nueva.Descripcion} {inicio:dd/MM/yyyy}: {nueva.Mensaje}";
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "No se pudo pedir {Inicio:d}–{Fin:d} de {Rfc}", inicio, fin, empresa.Rfc);
                    resultado.Aviso = ex.Message;
                }
            }
        }

        resultado.Pendientes = automaticas.Count(s => s.Pendiente);
    }

    /// <summary>Emitidos y Recibidos (XML) y, si se pidió, sus metadatos.</summary>
    private static IEnumerable<(bool Recibidos, bool Metadatos)> Tipos(
        EmpresaSatDto sat)
    {
        if (sat.DescargaEmitidos) yield return (false, false);
        if (sat.DescargaRecibidos) yield return (true, false);

        if (sat.DescargaMetadatos)
        {
            bool ninguno = !sat.DescargaEmitidos && !sat.DescargaRecibidos;

            if (sat.DescargaEmitidos || ninguno) yield return (false, true);
            if (sat.DescargaRecibidos || ninguno) yield return (true, true);
        }
    }

    /// <summary>
    /// Vigente o terminada, o rechazada por 5004 (el SAT no encontró CFDI en el periodo):
    /// no hay que volver a pedirla. Rechazos de otro tipo, errores y vencidas se reintentan.
    /// </summary>
    private static bool CubrePeriodo(
        SolicitudSatDto s) =>
        EstadosVigentes.Contains(s.Estado) ||
        s.Mensaje.StartsWith("5004", StringComparison.Ordinal);

    /// <summary>Días de [desde, hasta] sin solicitud que los cubra, en tramos que no cruzan de mes.</summary>
    private static IEnumerable<(DateTime Inicio, DateTime Fin)> PeriodosFaltantes(
        DateTime desde,
        DateTime hasta,
        List<SolicitudSatDto> cubiertas)
    {
        DateTime dia = desde;

        while (dia <= hasta)
        {
            SolicitudSatDto? cubre =
                cubiertas.FirstOrDefault(s => s.FechaInicial.Date <= dia && s.FechaFinal.Date >= dia);

            if (cubre is not null)
            {
                dia = cubre.FechaFinal.Date.AddDays(1);
                continue;
            }

            DateTime fin = new DateTime(dia.Year, dia.Month, DateTime.DaysInMonth(dia.Year, dia.Month));

            if (fin > hasta)
            {
                fin = hasta;
            }

            DateTime? siguiente = cubiertas
                .Where(s => s.FechaInicial.Date > dia)
                .Select(s => (DateTime?)s.FechaInicial.Date)
                .Min();

            if (siguiente is DateTime inicioSiguiente && inicioSiguiente <= fin)
            {
                fin = inicioSiguiente.AddDays(-1);
            }

            yield return (dia, fin);

            dia = fin.AddDays(1);
        }
    }

    private static void Reemplazar(
        List<SolicitudSatDto> lista,
        SolicitudSatDto solicitud)
    {
        int i = lista.FindIndex(s => s.Id == solicitud.Id);

        if (i >= 0)
        {
            lista[i] = solicitud;
        }
    }
}
