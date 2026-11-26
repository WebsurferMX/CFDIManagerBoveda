using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace CFDIManager.Escritorio;

public partial class App : Application
{
    private const string NombreUnico = @"Local\CFDIManagerEscritorio";

    private Mutex? _unica;
    private EventWaitHandle? _mostrar;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Una sola instancia por usuario: si ya está abierta (aunque sea en la bandeja), se le pide que se muestre.
        _unica = new Mutex(true, NombreUnico, out bool primera);
        _mostrar = new EventWaitHandle(false, EventResetMode.AutoReset, NombreUnico + ".Mostrar");

        if (!primera)
        {
            _mostrar.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += ErrorNoControlado;

        Configuracion? configuracion = Configuracion.Cargar();

        if (configuracion is null)
        {
            configuracion = new Configuracion();
            if (new ConfiguracionWindow(configuracion).ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        var ventana = new MainWindow(configuracion);
        MainWindow = ventana;

        // --bandeja: al iniciar con Windows se queda en la bandeja hasta que el usuario la abre.
        if (!e.Args.Contains("--bandeja", StringComparer.OrdinalIgnoreCase))
            ventana.Mostrar();

        ThreadPool.RegisterWaitForSingleObject(_mostrar, (_, _) => Dispatcher.BeginInvoke(ventana.Mostrar), null, -1, false);
    }

    private static void ErrorNoControlado(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "CFDI Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _unica?.Dispose();
        _mostrar?.Dispose();
        base.OnExit(e);
    }
}
