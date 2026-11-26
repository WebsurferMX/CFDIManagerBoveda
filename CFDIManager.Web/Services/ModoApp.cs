namespace CFDIManager.Web.Services;

/// <summary>
/// Cómo corre la app. Por omisión es el servidor (servicio de Windows o desarrollo). Con <c>--Modo=Escritorio</c> la
/// arranca CFDI Manager Escritorio en la PC de un cliente con instalación propia: base local que puede venir vacía,
/// Storage propio de la PC, solo en 127.0.0.1 (sin HTTPS) y llaves DPAPI del equipo.
/// </summary>
public static class ModoApp
{
    public static bool Escritorio { get; private set; }

    /// <summary>Storage de omisión en modo Escritorio: fuera de la carpeta del programa, que se reemplaza al actualizar.</summary>
    public static string StorageEscritorio =>
        Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "CFDIManagerDatos", "Storage");

    public static void Configurar(IConfiguration configuracion) =>
        Escritorio = string.Equals(configuracion["Modo"], "Escritorio", StringComparison.OrdinalIgnoreCase);
}
