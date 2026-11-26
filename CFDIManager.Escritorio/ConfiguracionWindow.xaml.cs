using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        CarpetaDatos.Text = configuracion.CarpetaDatos;
        CerrarABandeja.IsChecked = configuracion.CerrarABandeja;
        IniciarConWindows.IsChecked = configuracion.IniciarConWindows;

        (configuracion.EsLocal ? ModoLocal : ModoServidor).IsChecked = true;
        ActualizarEstadoDatos();
    }

    private void Modo_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelLocal is null)
            return;

        bool local = ModoLocal.IsChecked == true;
        PanelLocal.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        PanelServidor.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        Error.Visibility = Visibility.Collapsed;
    }

    // ---------- servidor ----------

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

    private void ElegirCarpetaLocal_Click(object sender, RoutedEventArgs e) => ElegirCarpeta(CarpetaLocal);

    // ---------- instalación propia ----------

    private void ElegirCarpetaDatos_Click(object sender, RoutedEventArgs e) => ElegirCarpeta(CarpetaDatos);

    private void CarpetaDatos_TextChanged(object sender, TextChangedEventArgs e) => ActualizarEstadoDatos();

    /// <summary>Si la carpeta ya tiene una instalación con su administrador, no se vuelve a pedir.</summary>
    private void ActualizarEstadoDatos()
    {
        if (PanelAdministrador is null)
            return;

        string carpeta = CarpetaDatos.Text.Trim();
        Instalacion? instalacion = Path.IsPathFullyQualified(carpeta) ? ServidorLocal.LeerInstalacion(carpeta) : null;

        EstadoDatos.Text = instalacion is null
            ? "Instalación nueva."
            : $"Instalación existente del {instalacion.Creada:dd/MM/yyyy}.";
        PanelAdministrador.Visibility = instalacion is { AdministradorCreado: true } ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ElegirCarpeta(TextBox destino)
    {
        var dialogo = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(destino.Text) ? destino.Text : null
        };

        if (dialogo.ShowDialog(this) == true)
            destino.Text = dialogo.FolderName;
    }

    /// <summary>La carpeta de datos del servicio CFDIManager de este equipo es del servidor: no se comparte.</summary>
    private static bool EsCarpetaDelServicio(string carpeta)
    {
        using RegistryKey? servicio = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\CFDIManager");
        if (servicio is null)
            return false;

        string delServicio = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "CFDIManagerDatos");
        return string.Equals(Path.GetFullPath(carpeta).TrimEnd('\\'), delServicio, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Las mismas reglas que Identity en la web: 8 caracteres, mayúscula, minúscula y número.</summary>
    private static string? ErrorPassword(string password) =>
        password.Length < 8 ? "La contraseña debe tener al menos 8 caracteres."
        : !password.Any(char.IsUpper) ? "La contraseña debe tener al menos una mayúscula."
        : !password.Any(char.IsLower) ? "La contraseña debe tener al menos una minúscula."
        : !password.Any(char.IsDigit) ? "La contraseña debe tener al menos un número."
        : null;

    // ---------- guardar ----------

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        bool local = ModoLocal.IsChecked == true;
        string? error = local ? ValidarLocal() : ValidarServidor();

        if (error is not null)
        {
            Error.Text = error;
            Error.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            InicioWindows.Aplicar(IniciarConWindows.IsChecked == true);
        }
        catch (Exception ex)
        {
            Error.Text = "No se pudo cambiar el inicio con Windows: " + ex.Message;
            Error.Visibility = Visibility.Visible;
            return;
        }

        _configuracion.Modo = local ? "Local" : "Servidor";

        if (local)
        {
            _configuracion.CarpetaDatos = Path.GetFullPath(CarpetaDatos.Text.Trim());

            if (PanelAdministrador.Visibility == Visibility.Visible)
            {
                _configuracion.AdministradorUsuario = AdminUsuario.Text.Trim();
                _configuracion.AdministradorPassword = AdminPassword.Password;
            }
        }
        else
        {
            _configuracion.Servidores = RenglonesServidores()
                .Select(r => Configuracion.Normalizar(r)!.ToString().TrimEnd('/'))
                .ToList();
            _configuracion.CarpetaLocal = CarpetaLocal.Text.Trim();
        }

        _configuracion.CerrarABandeja = CerrarABandeja.IsChecked == true;
        _configuracion.IniciarConWindows = IniciarConWindows.IsChecked == true;
        _configuracion.Guardar();

        DialogResult = true;
    }

    private string? ValidarServidor()
    {
        List<string> renglones = RenglonesServidores();
        List<string> invalidos = renglones.Where(r => Configuracion.Normalizar(r) is null).ToList();

        if (renglones.Count == 0)
            return "Escribe al menos un servidor, por ejemplo 192.168.1.20:5000.";
        if (invalidos.Count > 0)
            return "Dirección no válida: " + string.Join(", ", invalidos);
        if (!Path.IsPathFullyQualified(CarpetaLocal.Text.Trim()))
            return "La carpeta local debe ser una ruta completa, por ejemplo C:\\CFDI Manager.";

        return null;
    }

    private string? ValidarLocal()
    {
        string carpeta = CarpetaDatos.Text.Trim();

        if (!Path.IsPathFullyQualified(carpeta))
            return "La carpeta de datos debe ser una ruta completa, por ejemplo C:\\CFDIManagerDatos.";
        if (EsCarpetaDelServicio(carpeta))
            return "Esa carpeta es la del servicio CFDIManager de este equipo. Elige otra.";

        if (PanelAdministrador.Visibility != Visibility.Visible)
            return null;

        if (AdminUsuario.Text.Trim().Length == 0)
            return "Escribe el usuario administrador.";
        if (ErrorPassword(AdminPassword.Password) is string errorPassword)
            return errorPassword;
        if (AdminPassword.Password != AdminConfirmar.Password)
            return "La contraseña y su confirmación no coinciden.";

        return null;
    }
}
