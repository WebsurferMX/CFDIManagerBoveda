using System.IO;
using Velopack;

namespace CFDIManager.Escritorio;

public static class Program
{
    /// <summary>
    /// Velopack va primero: al instalar, actualizar o desinstalar ejecuta sus ganchos y termina sin abrir la ventana.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            // pgsql vive en la carpeta del programa: con PostgreSQL corriendo, Windows no deja reemplazarla.
            .OnBeforeUpdateFastCallback(_ => DetenerServidorLocal())
            .OnBeforeUninstallFastCallback(_ =>
            {
                DetenerServidorLocal();
                QuitarInicioConWindows();
                // La carpeta de datos (XML, e.firmas, base) se conserva: son datos del cliente.
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static void DetenerServidorLocal()
    {
        try
        {
            if (Configuracion.Cargar() is { EsLocal: true } configuracion && Directory.Exists(configuracion.CarpetaDatos))
                new ServidorLocal(configuracion.CarpetaDatos).Detener();
        }
        catch (Exception)
        {
            // Sin configuración o PostgreSQL ya detenido.
        }
    }

    private static void QuitarInicioConWindows()
    {
        try
        {
            InicioWindows.Aplicar(false);
        }
        catch (Exception)
        {
            // No estaba.
        }
    }
}
