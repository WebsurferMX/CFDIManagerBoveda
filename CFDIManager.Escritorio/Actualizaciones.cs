using Velopack;
using Velopack.Sources;

namespace CFDIManager.Escritorio;

/// <summary>
/// Actualizaciones desde GitHub Releases del repositorio público (las publica herramientas\Publicar-Escritorio.ps1
/// -Subir). Se revisa al iniciar y cada 6 horas; la nueva versión se baja sola y se instala al salir o desde la bandeja.
/// </summary>
public sealed class Actualizaciones
{
    private const string Repositorio = "https://github.com/WebsurferMX/CFDIManagerBoveda";

    private readonly UpdateManager _manager = new(new GithubSource(Repositorio, accessToken: null, prerelease: false));
    private readonly System.Windows.Threading.DispatcherTimer _reloj = new() { Interval = TimeSpan.FromHours(6) };

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
        await Task.Delay(TimeSpan.FromMinutes(1));
        await RevisarAsync();
    }

    private async Task RevisarAsync()
    {
        if (Lista is not null)
            return;

        try
        {
            if (await _manager.CheckForUpdatesAsync() is not UpdateInfo nueva)
                return;

            await _manager.DownloadUpdatesAsync(nueva);
            Lista = nueva;
            Descargada?.Invoke(nueva.TargetFullRelease.Version.ToString());
        }
        catch (Exception)
        {
            // Sin internet o GitHub no respondió: se intenta en la siguiente revisión.
        }
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
