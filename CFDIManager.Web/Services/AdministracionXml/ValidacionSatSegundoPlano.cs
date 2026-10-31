using System.Collections.Concurrent;
using CFDIParser.Models;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// "Valida Estado SAT" de Administración de XML en segundo plano: una validación por empresa a la vez.
/// Sigue aunque el usuario cambie de pantalla (antes se cancelaba al salir); al volver, la pantalla
/// se reconecta con el avance por medio de <see cref="Cambio"/>.
/// </summary>
public sealed class ValidacionSatSegundoPlano(IServiceScopeFactory scopes, ILogger<ValidacionSatSegundoPlano> log)
{
    public sealed class Trabajo
    {
        public required long EmpresaId { get; init; }
        public required DateTime Inicio { get; init; }
        public ResultadoProcesoXml Resultado { get; internal set; } = new();
        public bool Terminado { get; internal set; }
        public bool Cancelado { get; internal set; }
        public string? Error { get; internal set; }

        /// <summary>Ya se mostró el resumen final en pantalla (para no repetirlo cada vez que se abre).</summary>
        public bool Avisado { get; set; }

        public bool EnCurso => !Terminado;

        internal CancellationTokenSource Cancelacion { get; } = new();
    }

    private readonly ConcurrentDictionary<long, Trabajo> _trabajos = new();
    private readonly object _candado = new();

    /// <summary>Avance o fin de la validación de una empresa (el parámetro es su EmpresaId).</summary>
    public event Action<long>? Cambio;

    /// <summary>La validación en curso o la última que terminó para la empresa.</summary>
    public Trabajo? Obtener(long empresaId) => _trabajos.TryGetValue(empresaId, out Trabajo? t) ? t : null;

    /// <summary>Regresa false si la empresa ya tiene una validación en curso.</summary>
    public bool Iniciar(EmpresaDto empresa, IReadOnlyList<AdminXmlFilaDto> filas)
    {
        var trabajo = new Trabajo { EmpresaId = empresa.EmpresaId, Inicio = DateTime.Now };
        trabajo.Resultado.Total = filas.Count;

        lock (_candado)
        {
            if (Obtener(empresa.EmpresaId) is { EnCurso: true })
                return false;

            _trabajos[empresa.EmpresaId] = trabajo;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                AdministracionXmlService admin = scope.ServiceProvider.GetRequiredService<AdministracionXmlService>();

                var avance = new Avance(r =>
                {
                    trabajo.Resultado = r;
                    Notificar(empresa.EmpresaId);
                });

                trabajo.Resultado = await admin.ValidarEstadoSatAsync(empresa, filas, avance, trabajo.Cancelacion.Token);
            }
            catch (OperationCanceledException)
            {
                trabajo.Cancelado = true;
            }
            catch (Exception ex)
            {
                trabajo.Error = ex.Message;
                log.LogWarning(ex, "Valida Estado SAT de {Rfc}", empresa.Rfc);
            }
            finally
            {
                trabajo.Terminado = true;
                Notificar(empresa.EmpresaId);
            }
        });

        Notificar(empresa.EmpresaId);
        return true;
    }

    public void Cancelar(long empresaId)
    {
        if (Obtener(empresaId) is { EnCurso: true } trabajo)
            trabajo.Cancelacion.Cancel();
    }

    private void Notificar(long empresaId)
    {
        try
        {
            Cambio?.Invoke(empresaId);
        }
        catch (Exception ex)
        {
            // Una pantalla que falla al pintar no detiene la validación.
            log.LogDebug(ex, "Aviso de avance de Valida Estado SAT");
        }
    }

    /// <summary>Reporta en el mismo hilo (Progress&lt;T&gt; lo mandaría al pool y podría llegar desordenado).</summary>
    private sealed class Avance(Action<ResultadoProcesoXml> alReportar) : IProgress<ResultadoProcesoXml>
    {
        public void Report(ResultadoProcesoXml value) => alReportar(value);
    }
}
