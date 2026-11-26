using System.IO;
using System.Text.Json;

namespace CFDIManager.Escritorio;

/// <summary>
/// Configuración de la app en esta PC y para este usuario de Windows:
/// %LocalAppData%\CFDIManagerEscritorio\configuracion.json.
/// </summary>
public sealed class Configuracion
{
    public static string Carpeta { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CFDIManagerEscritorio");

    private static string Archivo => Path.Combine(Carpeta, "configuracion.json");

    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    /// <summary>"Servidor" (abre un CFDIManager.Web de otro equipo) o "Local" (instalación propia, F2).</summary>
    public string Modo { get; set; } = "Servidor";

    /// <summary>Direcciones del servidor en orden (p. ej. la de la red y la de Tailscale): se usa la primera que responde.</summary>
    public List<string> Servidores { get; set; } = [];

    /// <summary>Modo Local: carpeta con Storage, la base de PostgreSQL y los logs de esta PC.</summary>
    public string CarpetaDatos { get; set; } =
        Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "CFDIManagerDatos");

    /// <summary>Administrador capturado en el asistente; solo en memoria hasta que la web arranca con él.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AdministradorUsuario { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? AdministradorPassword { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool EsLocal => Modo == "Local";

    /// <summary>Carpeta de esta PC para las copias de XML que se localizan en el Explorador.</summary>
    public string CarpetaLocal { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CFDI Manager");

    /// <summary>Última carpeta usada en "Guardar como".</summary>
    public string? UltimaCarpetaGuardado { get; set; }

    public bool CerrarABandeja { get; set; } = true;

    public bool IniciarConWindows { get; set; }

    public double? Izquierda { get; set; }
    public double? Arriba { get; set; }
    public double? Ancho { get; set; }
    public double? Alto { get; set; }
    public bool Maximizada { get; set; } = true;

    /// <summary>La configuración guardada, o null en el primer arranque.</summary>
    public static Configuracion? Cargar()
    {
        try
        {
            return File.Exists(Archivo)
                ? JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(Archivo), Opciones)
                : null;
        }
        catch (Exception)
        {
            // Archivo dañado: se vuelve a configurar.
            return null;
        }
    }

    public void Guardar()
    {
        Directory.CreateDirectory(Carpeta);
        File.WriteAllText(Archivo, JsonSerializer.Serialize(this, Opciones));
    }

    /// <summary>"100.1.2.3:5000" → http://100.1.2.3:5000/. Null si no es una dirección http(s) válida.</summary>
    public static Uri? Normalizar(string? texto)
    {
        string limpio = (texto ?? "").Trim();
        if (limpio.Length == 0)
            return null;

        if (!limpio.Contains("://", StringComparison.Ordinal))
            limpio = "http://" + limpio;

        return Uri.TryCreate(limpio, UriKind.Absolute, out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? new Uri(uri.GetLeftPart(UriPartial.Authority) + "/")
            : null;
    }
}
