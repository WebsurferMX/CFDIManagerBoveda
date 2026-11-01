namespace CFDIManager.Web.Services;

/// <summary>
/// Carpeta de datos de la app (XML, e.firmas, logos, reportes, descargas del SAT...). Se define en Herramientas
/// (configuracion_app, por equipo) o en AppSettings:CarpetaStorage; sin definir es "Storage" junto a la app.
/// Así los datos pueden vivir fuera de la carpeta que se publica. El cambio aplica al reiniciar la app.
/// </summary>
public static class CarpetaStorage
{
    private const string Prefijo = "Storage";

    private static string _carpetaApp = AppContext.BaseDirectory;

    /// <summary>Por equipo: la base viaja entre equipos y una carpeta de uno puede no existir en otro.</summary>
    public static string ClaveConfiguracion => $"carpeta_storage:{Environment.MachineName}";

    /// <summary>Carpeta en uso (la que se resolvió al arrancar).</summary>
    public static string Raiz { get; private set; } = Path.Combine(AppContext.BaseDirectory, Prefijo);

    /// <summary>La carpeta de omisión: Storage junto a la app.</summary>
    public static string Predeterminada => Path.GetFullPath(Prefijo, _carpetaApp);

    /// <summary>Se llama al arrancar, antes de registrar servicios. Gana lo definido en Herramientas.</summary>
    public static void Configurar(IConfiguration configuracion, string carpetaApp, string? definidaEnHerramientas)
    {
        _carpetaApp = carpetaApp;

        string ruta =
            definidaEnHerramientas is { Length: > 0 } enBase ? enBase
            : configuracion["AppSettings:CarpetaStorage"] is { Length: > 0 } enConfiguracion ? enConfiguracion
            : Prefijo;

        Raiz = Path.GetFullPath(ruta, carpetaApp);
    }

    /// <summary>
    /// Copia el contenido de la carpeta en uso a <paramref name="destino"/> (sin Temporal ni Diagnostico).
    /// No sobrescribe: un archivo que ya existe allá se omite. Devuelve copiados y omitidos.
    /// </summary>
    public static (int Copiados, int Omitidos) CopiarA(string destino, Action<int, int>? avance, CancellationToken ct)
    {
        string[] excluir = ["Temporal", "Diagnostico"];
        var archivos = Directory.EnumerateFiles(Raiz, "*", SearchOption.AllDirectories)
            .Where(a => !excluir.Contains(Path.GetRelativePath(Raiz, a).Split(Path.DirectorySeparatorChar)[0], StringComparer.OrdinalIgnoreCase))
            .ToList();

        int copiados = 0, omitidos = 0;

        foreach (string archivo in archivos)
        {
            ct.ThrowIfCancellationRequested();

            string nuevo = Path.Combine(destino, Path.GetRelativePath(Raiz, archivo));

            if (File.Exists(nuevo))
            {
                omitidos++;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(nuevo)!);
                File.Copy(archivo, nuevo);
                copiados++;
            }

            if ((copiados + omitidos) % 200 == 0)
                avance?.Invoke(copiados + omitidos, archivos.Count);
        }

        avance?.Invoke(archivos.Count, archivos.Count);
        return (copiados, omitidos);
    }

    /// <summary>Ruta dentro de la carpeta de datos.</summary>
    public static string Ruta(params string[] partes) => Path.Combine([Raiz, .. partes]);

    /// <summary>
    /// Ruta absoluta de una ruta guardada o configurada. Las que empiezan con "Storage\" (como las de la
    /// e.firma en la base) se toman de la carpeta de datos; otras relativas, de la carpeta de la app.
    /// </summary>
    public static string Resolver(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta))
            return "";

        if (Path.IsPathRooted(ruta))
            return ruta;

        string[] partes = ruta.Split(['\\', '/'], 2);

        return partes[0].Equals(Prefijo, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(partes.Length > 1 ? Path.Combine(Raiz, partes[1]) : Raiz)
            : Path.GetFullPath(ruta, _carpetaApp);
    }
}
