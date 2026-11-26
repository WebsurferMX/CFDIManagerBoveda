using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace CFDIManager.Escritorio;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly Configuracion _configuracion;
    private readonly Bandeja _bandeja;
    private Uri? _servidor;
    private bool _iniciada;
    private bool _saliendo;
    private bool _avisoBandejaMostrado;

    public MainWindow(Configuracion configuracion)
    {
        _configuracion = configuracion;
        InitializeComponent();
        RestaurarPosicion();

        _bandeja = new Bandeja(Mostrar, Recargar, AbrirConfiguracion, Salir);
    }

    /// <summary>Muestra la ventana (desde la bandeja, otra instancia o el arranque) y conecta la primera vez.</summary>
    public async void Mostrar()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = _configuracion.Maximizada ? WindowState.Maximized : WindowState.Normal;
        Activate();

        if (_iniciada)
            return;

        _iniciada = true;
        await IniciarAsync();
    }

    private async Task IniciarAsync()
    {
        try
        {
            CoreWebView2Environment entorno =
                await CoreWebView2Environment.CreateAsync(null, Path.Combine(Configuracion.Carpeta, "WebView2"));
            await Navegador.EnsureCoreWebView2Async(entorno);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MostrarAviso("Falta Microsoft Edge WebView2",
                "Instala el WebView2 Runtime de Microsoft y vuelve a abrir CFDI Manager.", botones: false);
            return;
        }

        CoreWebView2 core = Navegador.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.DocumentTitleChanged += (_, _) =>
            Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "CFDI Manager" : core.DocumentTitle;
        core.DownloadStarting += DescargaIniciando;
        core.NewWindowRequested += NuevaVentanaSolicitada;
        core.ProcessFailed += (_, _) => MostrarAviso("Se cerró la vista", "", botones: true);

        Navegador.NavigationStarting += NavegacionIniciando;
        Navegador.NavigationCompleted += NavegacionTerminada;

        _ = new Puente(core, _configuracion, _bandeja, () => _servidor);

        await ConectarAsync();
    }

    // ---------- conexión ----------

    /// <summary>Prueba los servidores en orden (red, Tailscale...) y abre el primero que responde.</summary>
    private async Task ConectarAsync()
    {
        MostrarAviso("Conectando…", "", botones: false);

        List<Uri> servidores = _configuracion.Servidores
            .Select(Configuracion.Normalizar)
            .OfType<Uri>()
            .ToList();

        foreach (Uri servidor in servidores)
        {
            AvisoTexto.Text = servidor.Authority;

            if (await RespondeAsync(servidor))
            {
                _servidor = servidor;
                Navegador.Source = servidor;
                return;
            }
        }

        _servidor = null;
        MostrarAviso("No se pudo conectar al servidor",
            servidores.Count == 0
                ? "No hay servidores configurados."
                : "Sin respuesta de " + string.Join(", ", servidores.Select(s => s.Authority)) + ".",
            botones: true);
    }

    public static async Task<bool> RespondeAsync(Uri servidor)
    {
        try
        {
            using HttpResponseMessage _ = await Http.GetAsync(new Uri(servidor, "cuenta/login"), HttpCompletionOption.ResponseHeadersRead);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void NavegacionIniciando(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Ligas a otros sitios (SAT, SINUBE, ayuda...) se abren en el navegador de Windows.
        if (_servidor is not null && Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? destino) &&
            (destino.Scheme == Uri.UriSchemeHttp || destino.Scheme == Uri.UriSchemeHttps) &&
            !MismoServidor(destino))
        {
            e.Cancel = true;
            AbrirEnNavegador(destino);
        }
    }

    private void NavegacionTerminada(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess || e.HttpStatusCode != 0 || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            Aviso.Visibility = Visibility.Collapsed;
            Navegador.Visibility = Visibility.Visible;
            return;
        }

        MostrarAviso("Se perdió la conexión con el servidor", _servidor?.Authority ?? "", botones: true);
    }

    private void NuevaVentanaSolicitada(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // El PDF y otras pestañas del mismo servidor se abren en una ventana de la app (con la misma sesión);
        // lo de otros sitios, en el navegador de Windows.
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? destino) &&
            (destino.Scheme == Uri.UriSchemeHttp || destino.Scheme == Uri.UriSchemeHttps) &&
            !MismoServidor(destino))
        {
            e.Handled = true;
            AbrirEnNavegador(destino);
        }
    }

    private bool MismoServidor(Uri destino) =>
        _servidor is not null &&
        Uri.Compare(destino, _servidor, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private static void AbrirEnNavegador(Uri destino) =>
        Process.Start(new ProcessStartInfo(destino.AbsoluteUri) { UseShellExecute = true });

    private void MostrarAviso(string titulo, string texto, bool botones)
    {
        AvisoTitulo.Text = titulo;
        AvisoTexto.Text = texto;
        AvisoBotones.Visibility = botones ? Visibility.Visible : Visibility.Collapsed;
        Aviso.Visibility = Visibility.Visible;
        Navegador.Visibility = Visibility.Hidden;
    }

    private async void Reintentar_Click(object sender, RoutedEventArgs e) => await ConectarAsync();

    private void Configuracion_Click(object sender, RoutedEventArgs e) => AbrirConfiguracion();

    // ---------- descargas: "Guardar como" de Windows ----------

    private void DescargaIniciando(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        CoreWebView2Deferral diferido = e.GetDeferral();

        // El diálogo se abre fuera del evento de WebView2 para no bloquearlo.
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                string sugerido = Path.GetFileName(e.ResultFilePath);
                string extension = Path.GetExtension(sugerido);

                var dialogo = new SaveFileDialog
                {
                    FileName = sugerido,
                    InitialDirectory = CarpetaInicialGuardado(),
                    Filter = extension.Length > 1
                        ? $"{extension.TrimStart('.').ToUpperInvariant()} (*{extension})|*{extension}|Todos los archivos (*.*)|*.*"
                        : "Todos los archivos (*.*)|*.*",
                    OverwritePrompt = true
                };

                if (dialogo.ShowDialog(this) != true)
                {
                    e.Cancel = true;
                    return;
                }

                e.ResultFilePath = dialogo.FileName;
                e.Handled = true;

                _configuracion.UltimaCarpetaGuardado = Path.GetDirectoryName(dialogo.FileName);
                _configuracion.Guardar();

                CoreWebView2DownloadOperation operacion = e.DownloadOperation;
                operacion.StateChanged += (_, _) =>
                {
                    string archivo = operacion.ResultFilePath;

                    if (operacion.State == CoreWebView2DownloadState.Completed)
                        _bandeja.Avisar("Archivo guardado", Path.GetFileName(archivo), () => Puente.MostrarEnExplorador(archivo));
                    else if (operacion.State == CoreWebView2DownloadState.Interrupted)
                        _bandeja.Avisar("No se pudo guardar", $"{Path.GetFileName(archivo)} ({operacion.InterruptReason})");
                };
            }
            finally
            {
                diferido.Complete();
            }
        });
    }

    private string CarpetaInicialGuardado() =>
        _configuracion.UltimaCarpetaGuardado is { Length: > 0 } carpeta && Directory.Exists(carpeta)
            ? carpeta
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    // ---------- bandeja, configuración y cierre ----------

    private async void Recargar()
    {
        Mostrar();

        if (_servidor is null)
            await ConectarAsync();
        else
            Navegador.Reload();
    }

    private async void AbrirConfiguracion()
    {
        var ventana = new ConfiguracionWindow(_configuracion);
        if (IsVisible)
            ventana.Owner = this;

        if (ventana.ShowDialog() == true && Navegador.CoreWebView2 is not null)
            await ConectarAsync();
    }

    private void Salir()
    {
        _saliendo = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        GuardarPosicion();

        if (!_saliendo && _configuracion.CerrarABandeja)
        {
            e.Cancel = true;
            Hide();

            if (!_avisoBandejaMostrado)
            {
                _avisoBandejaMostrado = true;
                _bandeja.Avisar("CFDI Manager sigue abierto", "Ábrelo o ciérralo desde el ícono de la bandeja.", Mostrar);
            }
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _bandeja.Dispose();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    private void RestaurarPosicion()
    {
        if (_configuracion is { Izquierda: double x, Arriba: double y, Ancho: double ancho, Alto: double alto } &&
            x + ancho > SystemParameters.VirtualScreenLeft && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            y >= SystemParameters.VirtualScreenTop && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = x;
            Top = y;
            Width = ancho;
            Height = alto;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (_configuracion.Maximizada)
            WindowState = WindowState.Maximized;
    }

    private void GuardarPosicion()
    {
        if (!IsVisible)
            return;

        Rect normal = RestoreBounds;
        _configuracion.Maximizada = WindowState == WindowState.Maximized;
        _configuracion.Izquierda = normal.Left;
        _configuracion.Arriba = normal.Top;
        _configuracion.Ancho = normal.Width;
        _configuracion.Alto = normal.Height;
        _configuracion.Guardar();
    }
}
