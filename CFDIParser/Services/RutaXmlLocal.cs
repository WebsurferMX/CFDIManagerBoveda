namespace CFDIParser.Services;

/// <summary>
/// La base guarda la ruta completa de cada XML, que puede venir de otro equipo
/// (p. ej. C:\Users\Otro\Desktop\...\Storage\XML\RFC\Emitidos\2026\10\uuid.xml).
/// Si esa ruta no existe aquí, se busca el mismo archivo bajo la carpeta local de XML
/// a partir de la carpeta del RFC. La base no se modifica.
/// </summary>
public static class RutaXmlLocal
{
    private static readonly TimeSpan VigenciaCarpetas = TimeSpan.FromMinutes(1);

    private static string? _carpetaGeneral;
    private static HashSet<string> _carpetasRfc = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _cargadas = DateTime.MinValue;
    private static readonly object _candado = new();

    /// <summary>Carpeta local de XML (AppSettings:CarpetaXml). La fija la aplicación web al arrancar.</summary>
    public static string? CarpetaGeneral
    {
        get => _carpetaGeneral;
        set
        {
            lock (_candado)
            {
                _carpetaGeneral = value;
                _cargadas = DateTime.MinValue;
            }
        }
    }

    /// <summary>La ruta guardada si existe; si no, la equivalente en la carpeta local cuando ahí está el archivo.</summary>
    public static string Resolver(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || CarpetaGeneral is not { } raiz || File.Exists(ruta))
            return ruta ?? "";

        string[] partes = ruta.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        HashSet<string> carpetasRfc = CarpetasRfc(raiz);

        // La primera carpeta que coincide con una carpeta de RFC local marca el inicio de la ruta relativa.
        for (int i = 0; i < partes.Length - 1; i++)
        {
            if (!carpetasRfc.Contains(partes[i]))
                continue;

            string local = Path.Combine([raiz, .. partes[i..]]);

            if (File.Exists(local))
                return local;
        }

        return ruta;
    }

    private static HashSet<string> CarpetasRfc(string raiz)
    {
        lock (_candado)
        {
            if (DateTime.UtcNow - _cargadas < VigenciaCarpetas)
                return _carpetasRfc;

            _carpetasRfc = Directory.Exists(raiz)
                ? Directory.EnumerateDirectories(raiz)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _cargadas = DateTime.UtcNow;
            return _carpetasRfc;
        }
    }
}
