using System.IO;
using Velopack;
using Velopack.Sources;

namespace CFDIManager.Escritorio;

/// <summary>
/// Actualizaciones desde GitHub Releases del repositorio público (las publica herramientas\Publicar-Escritorio.ps1
/// -Subir). Se revisa al iniciar, cada 6 horas y desde la bandeja; la nueva versión se baja sola y se instala al salir
/// o desde la bandeja. Cada revisión queda en %LocalAppData%\CFDIManagerEscritorio\actualizaciones.log.
/// </summary>
public sealed class Actualizaciones
{
    private const string Repositorio = "https://github.com/WebsurferMX/CFDIManagerBoveda";

    /// <summary>Solo pruebas: CFDI_ESCRITORIO_ACTUALIZACIONES = carpeta con los paquetes (publish\Releases) en vez de GitHub.</summary>
    private static readonly string? CarpetaPrueba = Environment.GetEnvironmentVariable("CFDI_ESCRITORIO_ACTUALIZACIONES");

    private static readonly string Bitacora = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CFDIManagerEscritorio", "actualizaciones.log");

    private readonly UpdateManager _manager = new(CarpetaPrueba is { Length: > 0 }
        ? new SimpleFileSource(new DirectoryInfo(CarpetaPrueba))
        : new GithubSource(Repositorio, accessToken: null, prerelease: false));
    private readonly System.Windows.Threading.DispatcherTimer _reloj = new() { Interval = TimeSpan.FromHours(6) };
    private readonly SemaphoreSlim _revisando = new(1, 1);

    /// <summary>Versión ya descargada, lista para instalarse.</summary>
    public UpdateInfo? Lista { get; private set; }

    /// <summary>Se avisa cuando hay una versión descargada (con su número).</summary>
    public event Action<string>? Descargada;

    /// <summary>Instalada con el instalador (en desarrollo no hay actualizaciones).</summary>
    public bool Disponible => _manager.IsInstalled;

    public static string VersionActual =>
        typeof(Actualizaciones).Assembly.GetName().Version is Version v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    public void Iniciar()
    {
        if (!Disponible)
            return;

        _reloj.Tick += async (_, _) => await RevisarAsync();
        _reloj.Start();
        _ = RevisarConRetrasoAsync();
    }

    private async Task RevisarConRetrasoAsync()
    {
        // Que primero arranque la app (y en modo Local, PostgreSQL y la web).
        await Task.Delay(CarpetaPrueba is { Length: > 0 } ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(1));
        await RevisarAsync();
    }

    /// <summary>Revisa ya y regresa el resultado en palabras (para la bandeja).</summary>
    public async Task<string> RevisarAsync()
    {
        if (!Disponible)
            return "Esta copia no se instaló con el instalador; no se actualiza sola.";
        if (Lista is not null)
            return $"La versión {Lista.TargetFullRelease.Version} ya está descargada: se instala al salir.";

        await _revisando.WaitAsync();
        try
        {
            if (await _manager.CheckForUpdatesAsync() is not UpdateInfo nueva)
            {
                Anotar($"Sin versiones nuevas (instalada {VersionActual}).");
                return $"Ya tienes la versión más reciente ({VersionActual}).";
            }

            var version = nueva.TargetFullRelease.Version.ToString();
            Anotar($"Versión {version} encontrada; descargando.");
            await _manager.DownloadUpdatesAsync(nueva);
            Lista = nueva;
            Anotar($"Versión {version} descargada.");
            Descargada?.Invoke(version);
            return $"La versión {version} está lista.";
        }
        catch (Exception ex)
        {
            // Sin internet o GitHub no respondió: se intenta en la siguiente revisión.
            Anotar($"Error al revisar: {ex.GetType().Name}: {ex.Message}");
            return $"No se pudo revisar: {ex.Message}";
        }
        finally
        {
            _revisando.Release();
        }
    }

    private static void Anotar(string texto)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Bitacora)!);
            File.AppendAllText(Bitacora, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {texto}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Instala ya la versión descargada y vuelve a abrir la app.</summary>
    public void InstalarYReiniciar()
    {
        if (Lista is not null)
            _manager.ApplyUpdatesAndRestart(Lista);
    }

    /// <summary>Al salir: la versión descargada se instala cuando la app termina, sin volver a abrirla.</summary>
    public void InstalarAlSalir()
    {
        if (Lista is not null)
            _manager.WaitExitThenApplyUpdates(Lista, silent: true, restart: false);
    }
}
