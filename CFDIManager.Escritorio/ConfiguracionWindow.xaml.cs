using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace CFDIManager.Escritorio;

public partial class ConfiguracionWindow : Window
{
    private readonly Configuracion _configuracion;

    public ConfiguracionWindow(Configuracion configuracion)
    {
        _configuracion = configuracion;
        InitializeComponent();

        Servidores.Text = string.Join(Environment.NewLine, configuracion.Servidores);
        CarpetaLocal.Text = configuracion.CarpetaLocal;
        CerrarABandeja.IsChecked = configuracion.CerrarABandeja;
        IniciarConWindows.IsChecked = configuracion.IniciarConWindows;
    }

    private List<string> RenglonesServidores() =>
        Servidores.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private async void Probar_Click(object sender, RoutedEventArgs e)
    {
        ResultadoPrueba.Text = "Probando…";
        var resultados = new List<string>();

        foreach (string renglon in RenglonesServidores())
        {
            resultados.Add(Configuracion.Normalizar(renglon) is Uri uri
                ? $"{uri.Authority}: {(await MainWindow.RespondeAsync(uri) ? "responde" : "sin respuesta")}"
                : $"{renglon}: dirección no válida");
        }

        ResultadoPrueba.Text = resultados.Count == 0 ? "Escribe al menos un servidor." : string.Join("   ", resultados);
    }

    private void ElegirCarpeta_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(CarpetaLocal.Text) ? CarpetaLocal.Text : null
        };

        if (dialogo.ShowDialog(this) == true)
            CarpetaLocal.Text = dialogo.FolderName;
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        List<string> renglones = RenglonesServidores();
        List<string> invalidos = renglones.Where(r => Configuracion.Normalizar(r) is null).ToList();

        if (renglones.Count == 0 || invalidos.Count > 0)
        {
            MostrarError(renglones.Count == 0
                ? "Escribe al menos un servidor, por ejemplo 192.168.1.20:5000."
                : "Dirección no válida: " + string.Join(", ", invalidos));
            return;
        }

        string carpeta = CarpetaLocal.Text.Trim();
        if (!Path.IsPathFullyQualified(carpeta))
        {
            MostrarError("La carpeta local debe ser una ruta completa, por ejemplo C:\\CFDI Manager.");
            return;
        }

        try
        {
            InicioWindows.Aplicar(IniciarConWindows.IsChecked == true);
        }
        catch (Exception ex)
        {
            MostrarError("No se pudo cambiar el inicio con Windows: " + ex.Message);
            return;
        }

        _configuracion.Modo = "Servidor";
        _configuracion.Servidores = renglones.Select(r => Configuracion.Normalizar(r)!.ToString().TrimEnd('/')).ToList();
        _configuracion.CarpetaLocal = carpeta;
        _configuracion.CerrarABandeja = CerrarABandeja.IsChecked == true;
        _configuracion.IniciarConWindows = IniciarConWindows.IsChecked == true;
        _configuracion.Guardar();

        DialogResult = true;
    }

    private void MostrarError(string texto)
    {
        Error.Text = texto;
        Error.Visibility = Visibility.Visible;
    }
}
