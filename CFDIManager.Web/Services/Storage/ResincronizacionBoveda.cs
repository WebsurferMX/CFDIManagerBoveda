using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Storage;

/// <summary>Resultado de re-sincronizar una empresa.</summary>
public sealed class ResultadoResincronizacion
{
    public long EmpresaId { get; init; }
    public string Rfc { get; init; } = "";
    public string Nombre { get; init; } = "";
    public int Archivos { get; set; }
    public int Rehechos { get; set; }
    public int Nuevos { get; set; }
    public int Omitidos { get; set; }
    public int Errores { get; set; }
    public int Retirados { get; set; }
    public string Aviso { get; set; } = "";
    public List<string> MuestraErrores { get; } = [];

    /// <summary>CFDI cuyo XML no está en ninguna parte (ni en su ruta ni en la bóveda local).</summary>
    public List<(long CfdiId, string Uuid, string Ruta)> SinXml { get; set; } = [];
}

/// <summary>
/// Re-sincronizar con la bóveda (Herramientas, solo Administrador): vuelve a leer todos los XML de la
/// carpeta de cada empresa y rehace sus datos en la base sin perder validaciones; importa los que falten
/// y, con la sincronización normal, retira los que desaparecieron de su carpeta. Corre en segundo plano.
/// </summary>
public sealed class ResincronizacionBoveda(
    IServiceScopeFactory scopes,
    EstadoSincronizacionStorage sincronizacion,
    ILogger<ResincronizacionBoveda> log)
{
    private CancellationTokenSource? _cancelacion;

    public bool EnCurso { get; private set; }
    public string EmpresaActual { get; private set; } = "";
    public int Procesados { get; private set; }
    public int Total { get; private set; }
    public DateTime? Inicio { get; private set; }
    public DateTime? Fin { get; private set; }
    public bool Cancelada { get; private set; }
    public List<ResultadoResincronizacion> Resultados { get; private set; } = [];

    public event Action? Cambio;

    /// <summary>
    /// Una empresa por corrida (por el volumen). false si ya hay una re-sincronización o una
    /// sincronización automática en curso.
    /// </summary>
    public bool Iniciar(EmpresaDto empresa)
    {
        if (EnCurso || !sincronizacion.Candado.Wait(0))
            return false;

        IReadOnlyList<EmpresaDto> empresas = [empresa];

        EnCurso = true;
        Cancelada = false;
        Inicio = DateTime.Now;
        Fin = null;
        Procesados = 0;
        Total = 0;
        Resultados = [];
        _cancelacion = new CancellationTokenSource();

        CancellationToken ct = _cancelacion.Token;
        _ = Task.Run(() => Ejecutar(empresas, ct));

        Notificar();
        return true;
    }

    public void Cancelar() => _cancelacion?.Cancel();

    private void Ejecutar(IReadOnlyList<EmpresaDto> empresas, CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            var importacion = scope.ServiceProvider.GetRequiredService<ImportacionCfdiService>();
            var storage = scope.ServiceProvider.GetRequiredService<SincronizacionStorageService>();
            var repo = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();

            // Primero se cuentan los archivos, para que la barra de avance sea del total.
            var trabajo = empresas
                .Select(e => (Empresa: e, Carpeta: importacion.ObtenerCarpetaBaseEmpresa(e)))
                .Select(t => (t.Empresa, t.Carpeta, Archivos: Archivos(t.Carpeta)))
                .ToList();

            Total = trabajo.Sum(t => t.Archivos.Count);
            Notificar();

            foreach ((EmpresaDto empresa, string carpeta, List<string> archivos) in trabajo)
            {
                ct.ThrowIfCancellationRequested();

                var r = new ResultadoResincronizacion
                {
                    EmpresaId = empresa.EmpresaId,
                    Rfc = empresa.Rfc,
                    Nombre = empresa.NombreParaMostrar,
                    Archivos = archivos.Count
                };

                Resultados.Add(r);
                EmpresaActual = empresa.NombreParaMostrar;

                if (!Directory.Exists(carpeta))
                    r.Aviso = $"No existe la carpeta {carpeta}.";

                foreach (string archivo in archivos)
                {
                    ct.ThrowIfCancellationRequested();

                    ResultadoImportacionCfdi i = importacion.Rehacer(empresa, archivo);

                    switch (i.Estatus)
                    {
                        case "REHECHO": r.Rehechos++; break;
                        case "CORRECTO": r.Nuevos++; break;
                        case "OMITIDO": r.Omitidos++; break;
                        default:
                            r.Errores++;
                            if (r.MuestraErrores.Count < 20)
                                r.MuestraErrores.Add($"{Path.GetFileName(archivo)}: {i.Detalle}");
                            break;
                    }

                    if (++Procesados % 50 == 0)
                        Notificar();
                }

                // La sincronización normal registra los archivos y retira los CFDI cuyo XML desapareció de su carpeta.
                ResultadoSincronizacion s = storage.SincronizarEmpresa(empresa, null, ct);
                r.Retirados = s.Eliminados;

                if (s.Aviso != "")
                    r.Aviso = s.Aviso;

                r.SinXml = BuscarSinXml(repo, empresa.EmpresaId);
                Notificar();
            }
        }
        catch (OperationCanceledException)
        {
            Cancelada = true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falló la re-sincronización con la bóveda");
            Resultados.Add(new ResultadoResincronizacion { Aviso = $"Error: {ex.Message}" });
        }
        finally
        {
            EnCurso = false;
            EmpresaActual = "";
            Fin = DateTime.Now;
            sincronizacion.Candado.Release();
            Notificar();
        }
    }

    /// <summary>Quita los CFDI de la empresa que siguen sin XML; devuelve cuántos quitó.</summary>
    public int QuitarSinXml(long empresaId)
    {
        using IServiceScope scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();

        // Se vuelve a revisar al momento: pudo aparecer el XML desde la re-sincronización.
        List<(long CfdiId, string Uuid, string Ruta)> sinXml = BuscarSinXml(repo, empresaId);

        foreach ((long cfdiId, _, _) in sinXml)
            repo.EliminarCfdi(empresaId, cfdiId);

        if (Resultados.FirstOrDefault(r => r.EmpresaId == empresaId) is { } resultado)
            resultado.SinXml = [];

        log.LogInformation("Re-sincronizar: {Cantidad} CFDI sin XML quitados de la empresa {EmpresaId}.", sinXml.Count, empresaId);
        Notificar();
        return sinXml.Count;
    }

    private static List<(long CfdiId, string Uuid, string Ruta)> BuscarSinXml(PostgreSqlRepository repo, long empresaId) =>
        repo.ObtenerRutasCfdi(empresaId)
            .Where(c => c.Ruta == "" || !File.Exists(RutaXmlLocal.Resolver(c.Ruta)))
            .ToList();

    private static List<string> Archivos(string carpeta) =>
        Directory.Exists(carpeta)
            ? Directory.EnumerateFiles(carpeta, "*.xml", SearchOption.AllDirectories)
                .Where(a => !a.Contains($"{Path.DirectorySeparatorChar}NoIdentificados{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];

    private void Notificar() => Cambio?.Invoke();
}
