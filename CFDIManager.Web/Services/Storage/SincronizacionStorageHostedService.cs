using System.Collections.Concurrent;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Storage;

/// <summary>Último resultado por empresa y disparador de pasadas manuales.</summary>
public class EstadoSincronizacionStorage
{
    private readonly SemaphoreSlim _ahora = new(0, 1);

    /// <summary>Una sola pasada sobre las carpetas a la vez: la automática o Re-sincronizar (Herramientas).</summary>
    public SemaphoreSlim Candado { get; } = new(1, 1);

    public ConcurrentDictionary<long, ResultadoSincronizacion> PorEmpresa { get; } = new();

    public bool EnCurso { get; set; }

    public DateTime? UltimaPasada { get; set; }

    public DateTime? SiguientePasada { get; set; }

    public event Action? Cambio;

    public void Notificar() => Cambio?.Invoke();

    /// <summary>Pide una pasada inmediata; si ya hay una pedida, no hace nada.</summary>
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
/// Sincroniza la carpeta de XML de todas las empresas activas al arrancar y cada
/// SincronizacionStorage:Minutos (10 por omisión). Solo procesa archivos nuevos o
/// modificados, así que las pasadas sin cambios tardan segundos.
/// </summary>
public class SincronizacionStorageHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly EstadoSincronizacionStorage _estado;
    private readonly ILogger<SincronizacionStorageHostedService> _log;
    private readonly TimeSpan _intervalo;

    public SincronizacionStorageHostedService(
        IServiceScopeFactory scopes,
        EstadoSincronizacionStorage estado,
        IConfiguration configuracion,
        ILogger<SincronizacionStorageHostedService> log)
    {
        _scopes = scopes;
        _estado = estado;
        _log = log;
        _intervalo = TimeSpan.FromMinutes(Math.Max(1, configuracion.GetValue("SincronizacionStorage:Minutos", 10)));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Deja terminar el arranque de la app antes de la primera pasada.
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

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
        try
        {
            await _estado.Candado.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _estado.EnCurso = true;
        _estado.Notificar();

        try
        {
            using IServiceScope scope = _scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();
            var servicio = scope.ServiceProvider.GetRequiredService<SincronizacionStorageService>();

            foreach (EmpresaDto empresa in repo.ObtenerEmpresasActivas())
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var progreso = new Progress<ResultadoSincronizacion>(r =>
                    {
                        _estado.PorEmpresa[r.EmpresaId] = r;
                        _estado.Notificar();
                    });

                    ResultadoSincronizacion r = await Task.Run(() => servicio.SincronizarEmpresa(empresa, progreso, ct), ct);
                    _estado.PorEmpresa[empresa.EmpresaId] = r;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "No se pudo sincronizar la carpeta de {Rfc}", empresa.Rfc);
                    _estado.PorEmpresa[empresa.EmpresaId] = new ResultadoSincronizacion
                    {
                        EmpresaId = empresa.EmpresaId,
                        Rfc = empresa.Rfc,
                        Inicio = DateTime.Now,
                        Fin = DateTime.Now,
                        Aviso = $"Error: {ex.Message}"
                    };
                }

                _estado.Notificar();
            }

            // CFDI entre empresas del grupo: registrar en la contraparte lo que falte.
            await Task.Run(() => servicio.RegistrarIntercompaniasPendientes(ct), ct);

            _estado.UltimaPasada = DateTime.Now;
        }
        catch (OperationCanceledException)
        {
            // La app se está cerrando.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falló la sincronización de Storage");
        }
        finally
        {
            _estado.EnCurso = false;
            _estado.Candado.Release();
            _estado.Notificar();
        }
    }
}
