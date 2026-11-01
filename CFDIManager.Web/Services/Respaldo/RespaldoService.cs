using System.Diagnostics;
using System.IO.Compression;
using CFDIParser.Models;
using CFDIParser.Services;
using Npgsql;

namespace CFDIManager.Web.Services.Respaldo;

/// <summary>Ajustes del respaldo de este equipo (Herramientas).</summary>
public sealed record ConfiguracionRespaldo(string Carpeta, TimeSpan Hora, int Diarios);

/// <summary>
/// Respaldo de la base de datos (pg_dump) y de Storage en un ZIP, en la carpeta elegida (p. ej. dentro de OneDrive,
/// que lo sube a la nube). Diarios\ conserva los últimos N; el último día hábil del mes se guarda además en
/// Mensuales\ (no se borran). La configuración es por equipo: la base viaja entre equipos.
/// </summary>
public sealed class RespaldoService(IServiceScopeFactory scopes, IConfiguration configuracion, ILogger<RespaldoService> log)
{
    private readonly SemaphoreSlim _candado = new(1, 1);

    public static string Equipo => Environment.MachineName;

    private static string Clave(string nombre) => $"respaldo:{Equipo}:{nombre}";

    public bool EnCurso { get; private set; }

    public string Paso { get; private set; } = "";

    public event Action? Cambio;

    public ConfiguracionRespaldo Leer()
    {
        using IServiceScope scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();

        return new ConfiguracionRespaldo(
            repo.ObtenerConfiguracionApp(Clave("carpeta")) ?? "",
            TimeSpan.TryParse(repo.ObtenerConfiguracionApp(Clave("hora")), out TimeSpan hora) ? hora : new TimeSpan(2, 0, 0),
            int.TryParse(repo.ObtenerConfiguracionApp(Clave("diarios")), out int diarios) ? diarios : 5);
    }

    public void Guardar(ConfiguracionRespaldo c)
    {
        using IServiceScope scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();

        repo.GuardarConfiguracionApp(Clave("carpeta"), c.Carpeta);
        repo.GuardarConfiguracionApp(Clave("hora"), c.Hora.ToString(@"hh\:mm"));
        repo.GuardarConfiguracionApp(Clave("diarios"), Math.Clamp(c.Diarios, 1, 60).ToString());
    }

    /// <summary>El último lunes a viernes del mes (no considera días festivos).</summary>
    public static bool EsUltimoDiaHabil(DateTime dia)
    {
        if (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        for (DateTime d = dia.AddDays(1); d.Month == dia.Month; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                return false;
        }

        return true;
    }

    /// <summary>Hace un respaldo ("Diario" o "Manual"). false si ya hay otro en curso.</summary>
    public async Task<bool> RespaldarAsync(string tipo, CancellationToken ct)
    {
        if (!await _candado.WaitAsync(0, ct))
            return false;

        using IServiceScope scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();

        EnCurso = true;
        long id = repo.IniciarRespaldo(Equipo, tipo);
        string temporal = CarpetaStorage.Ruta("Temporal", $"respaldo-{Guid.NewGuid():N}");

        try
        {
            ConfiguracionRespaldo c = Leer();

            if (c.Carpeta == "")
                throw new InvalidOperationException("Elige la carpeta de los respaldos en Herramientas.");

            string diarios = Path.Combine(c.Carpeta, "Diarios");
            Directory.CreateDirectory(diarios);
            Directory.CreateDirectory(temporal);

            Avance("Respaldando la base de datos…");
            string dump = Path.Combine(temporal, "cfdi_manager.backup");
            await VolcarBaseAsync(dump, ct);

            Avance("Comprimiendo la base y Storage…");
            string nombre = $"CFDIManager_{Equipo}_{DateTime.Now:yyyy-MM-dd_HHmm}.zip";
            string zip = Path.Combine(temporal, nombre);
            int archivos = await Task.Run(() => ArmarZip(zip, dump, ct), ct);

            // Se arma en Temporal y se mueve completo: OneDrive no sube un ZIP a medias.
            Avance("Copiando a la carpeta de respaldos…");
            string destino = Path.Combine(diarios, nombre);
            File.Move(zip, destino, overwrite: true);
            long tamano = new FileInfo(destino).Length;
            string detalle = $"{archivos:N0} archivos de Storage + base de datos";

            if (tipo == "Diario" && EsUltimoDiaHabil(DateTime.Today))
            {
                string mensuales = Path.Combine(c.Carpeta, "Mensuales");
                Directory.CreateDirectory(mensuales);
                File.Copy(destino, Path.Combine(mensuales, $"CFDIManager_{Equipo}_{DateTime.Today:yyyy-MM}.zip"), overwrite: true);
                detalle += "; también guardado como mensual";
            }

            int borrados = AplicarRetencion(diarios, c.Diarios);

            if (borrados > 0)
                detalle += $"; {borrados} diario(s) anterior(es) borrado(s)";

            repo.TerminarRespaldo(id, "Correcto", destino, tamano, detalle);
            log.LogInformation("Respaldo {Tipo} correcto: {Archivo} ({Tamano:N0} bytes). {Detalle}", tipo, destino, tamano, detalle);
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falló el respaldo {Tipo}", tipo);
            repo.TerminarRespaldo(id, "Error", "", 0, ex is OperationCanceledException ? "Cancelado." : ex.Message);
            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporal))
                    Directory.Delete(temporal, recursive: true);
            }
            catch (IOException)
            {
                // Se limpia en otra ocasión.
            }

            EnCurso = false;
            Paso = "";
            _candado.Release();
            Cambio?.Invoke();
        }
    }

    private void Avance(string paso)
    {
        Paso = paso;
        Cambio?.Invoke();
    }

    /// <summary>pg_dump en formato custom (comprimido; se restaura con pg_restore).</summary>
    private async Task VolcarBaseAsync(string archivo, CancellationToken ct)
    {
        var cadena = new NpgsqlConnectionStringBuilder(ConexionPostgres.Cadena(configuracion));

        var inicio = new ProcessStartInfo(RutaPgDump())
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string arg in new[] { "-h", cadena.Host ?? "localhost", "-p", cadena.Port.ToString(), "-U", cadena.Username ?? "postgres",
                                       "-w", "-Fc", "-f", archivo, cadena.Database ?? "cfdi_manager" })
        {
            inicio.ArgumentList.Add(arg);
        }

        inicio.Environment["PGPASSWORD"] = cadena.Password ?? "";

        using Process proceso = Process.Start(inicio) ?? throw new InvalidOperationException("No se pudo ejecutar pg_dump.");
        string errores = await proceso.StandardError.ReadToEndAsync(ct);
        await proceso.WaitForExitAsync(ct);

        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"pg_dump terminó con error: {errores.Trim()}");
    }

    /// <summary>Respaldo:PgDump o el pg_dump de la versión más nueva de PostgreSQL instalada.</summary>
    private string RutaPgDump()
    {
        if (configuracion["Respaldo:PgDump"] is { Length: > 0 } configurado)
            return configurado;

        string raiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL");

        return (Directory.Exists(raiz) ? Directory.GetDirectories(raiz) : [])
            .Select(d => Path.Combine(d, "bin", "pg_dump.exe"))
            .Where(File.Exists)
            .OrderByDescending(p => int.TryParse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(p))), out int v) ? v : 0)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No se encontró pg_dump.exe; defínelo en Respaldo:PgDump.");
    }

    /// <summary>cfdi_manager.backup y Storage\ (sin Temporal ni Diagnostico). Devuelve los archivos de Storage incluidos.</summary>
    private static int ArmarZip(string zip, string dump, CancellationToken ct)
    {
        string[] excluir = ["Temporal", "Diagnostico"];
        int archivos = 0;

        using ZipArchive archivo = ZipFile.Open(zip, ZipArchiveMode.Create);
        archivo.CreateEntryFromFile(dump, "cfdi_manager.backup", CompressionLevel.NoCompression);

        foreach (string ruta in Directory.EnumerateFiles(CarpetaStorage.Raiz, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            string relativa = Path.GetRelativePath(CarpetaStorage.Raiz, ruta);

            if (excluir.Contains(relativa.Split(Path.DirectorySeparatorChar)[0], StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                archivo.CreateEntryFromFile(ruta, Path.Combine("Storage", relativa).Replace('\\', '/'), CompressionLevel.Fastest);
                archivos++;
            }
            catch (IOException)
            {
                // Un archivo en uso en ese momento no detiene el respaldo.
            }
        }

        return archivos;
    }

    /// <summary>Deja los <paramref name="conservar"/> ZIP más recientes de Diarios; devuelve cuántos borró.</summary>
    private static int AplicarRetencion(string carpeta, int conservar)
    {
        List<FileInfo> viejos = new DirectoryInfo(carpeta).GetFiles("CFDIManager_*.zip")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(Math.Max(1, conservar))
            .ToList();

        foreach (FileInfo f in viejos)
            f.Delete();

        return viejos.Count;
    }

    public List<RespaldoDto> Historial(int limite = 10)
    {
        using IServiceScope scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>().ObtenerRespaldos(Equipo, limite);
    }
}

/// <summary>
/// Respaldo diario de lunes a viernes a la hora de Herramientas. Solo en el servicio de Windows: la app de
/// desarrollo del mismo equipo no repite el respaldo. Si el equipo estaba apagado a esa hora, corre al encender.
/// </summary>
public sealed class RespaldoHostedService(RespaldoService respaldos, IServiceScopeFactory scopes, ILogger<RespaldoHostedService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
            return;

        await Task.Delay(TimeSpan.FromMinutes(1), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConfiguracionRespaldo c = respaldos.Leer();
                DateTime hoy = DateTime.Today;

                if (c.Carpeta != "" &&
                    hoy.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
                    DateTime.Now >= hoy + c.Hora &&
                    !YaSeHizoHoy())
                {
                    await respaldos.RespaldarAsync("Diario", ct);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Falló la revisión del respaldo diario");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }

    private bool YaSeHizoHoy()
    {
        using IServiceScope scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>().HayRespaldoHoy(RespaldoService.Equipo, "Diario");
    }
}
