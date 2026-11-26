using Microsoft.Win32;

namespace CFDIManager.Escritorio;

/// <summary>"Iniciar con Windows": entrada en HKCU\...\Run que abre la app directo en la bandeja.</summary>
public static class InicioWindows
{
    private const string Clave = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Nombre = "CFDIManagerEscritorio";

    public static void Aplicar(bool activo)
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(Clave, writable: true);
        if (run is null)
            return;

        if (activo)
            run.SetValue(Nombre, $"\"{Environment.ProcessPath}\" --bandeja");
        else
            run.DeleteValue(Nombre, throwOnMissingValue: false);
    }
}
