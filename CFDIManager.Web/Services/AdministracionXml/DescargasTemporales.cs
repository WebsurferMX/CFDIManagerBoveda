using System.Collections.Concurrent;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Archivos grandes (los ZIP de XML y PDF) que la pantalla arma en el servidor y el navegador baja por
/// /api/descargas/{token}: así no viajan en base64 por la conexión de Blazor. Cada token sirve una sola vez
/// y caduca a los 30 minutos; con Borrar, el archivo se elimina al terminar de bajarlo.
/// </summary>
public sealed class DescargasTemporales
{
    public sealed record Descarga(string Ruta, string Nombre, bool Borrar, string Usuario, DateTime Caduca);

    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, Descarga> _descargas = new();

    /// <summary>Carpeta para los archivos que solo existen mientras se descargan.</summary>
    public static string CarpetaTemporal { get; } = Path.Combine(Path.GetTempPath(), "CFDIManager", "descargas");

    public string Registrar(string ruta, string nombre, bool borrar, string usuario)
    {
        Limpiar();

        string token = Guid.NewGuid().ToString("N");
        _descargas[token] = new Descarga(ruta, nombre, borrar, usuario, DateTime.UtcNow + Vigencia);
        return token;
    }

    /// <summary>La descarga del token si es del mismo usuario y no ha caducado; el token deja de servir.</summary>
    public Descarga? Tomar(string token, string usuario)
    {
        if (!_descargas.TryRemove(token, out Descarga? d))
        {
            return null;
        }

        if (d.Usuario != usuario || d.Caduca < DateTime.UtcNow || !File.Exists(d.Ruta))
        {
            Borrar(d);
            return null;
        }

        return d;
    }

    /// <summary>Quita los tokens caducados y los temporales que nadie bajó.</summary>
    private void Limpiar()
    {
        foreach (var (token, d) in _descargas)
        {
            if (d.Caduca < DateTime.UtcNow && _descargas.TryRemove(token, out _))
            {
                Borrar(d);
            }
        }

        try
        {
            if (!Directory.Exists(CarpetaTemporal))
            {
                return;
            }

            foreach (string archivo in Directory.GetFiles(CarpetaTemporal))
            {
                if (File.GetLastWriteTimeUtc(archivo) < DateTime.UtcNow - TimeSpan.FromDays(1))
                {
                    File.Delete(archivo);
                }
            }
        }
        catch (IOException)
        {
            // Un temporal en uso se borra en la siguiente limpieza.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Borrar(Descarga d)
    {
        if (!d.Borrar)
        {
            return;
        }

        try
        {
            File.Delete(d.Ruta);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
