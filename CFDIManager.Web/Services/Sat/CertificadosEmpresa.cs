using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Sat;

/// <summary>
/// Ubicación de la e.firma de cada empresa: Storage\Certificados\{RFC}\{RFC}.cer y .key.
/// En la base se guarda la ruta relativa a la carpeta de la app, y se resuelve contra
/// ContentRootPath (como servicio de Windows la carpeta actual es System32).
/// </summary>
public sealed class CertificadosEmpresa
{
    private readonly IWebHostEnvironment _ambiente;

    public CertificadosEmpresa(IWebHostEnvironment ambiente)
    {
        _ambiente = ambiente;
    }

    public static string RutaCer(string rfc) => Path.Combine(Carpeta(rfc), $"{Normalizar(rfc)}.cer");

    public static string RutaKey(string rfc) => Path.Combine(Carpeta(rfc), $"{Normalizar(rfc)}.key");

    private static string Carpeta(string rfc) => Path.Combine("Storage", "Certificados", Normalizar(rfc));

    private static string Normalizar(string rfc) => rfc.Trim().ToUpperInvariant();

    // Ubicación anterior (por id de empresa), para mover lo que aún esté ahí.
    private static string RutaCerAnterior(long empresaId) => Path.Combine("Storage", "Certificados", empresaId.ToString(), "certificado.cer");

    private static string RutaKeyAnterior(long empresaId) => Path.Combine("Storage", "Certificados", empresaId.ToString(), "llave.key");

    /// <summary>Ruta absoluta; las relativas se toman desde la carpeta de la app.</summary>
    public string Resolver(string? ruta) =>
        string.IsNullOrWhiteSpace(ruta) ? ""
        : Path.IsPathRooted(ruta) ? ruta
        : Path.Combine(_ambiente.ContentRootPath, ruta);

    public bool Existe(string? ruta) => Resolver(ruta) is { Length: > 0 } absoluta && File.Exists(absoluta);

    public byte[]? Leer(string? ruta) => Existe(ruta) ? File.ReadAllBytes(Resolver(ruta)) : null;

    public async Task GuardarAsync(string rutaRelativa, byte[] contenido)
    {
        string absoluta = Resolver(rutaRelativa);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluta)!);
        await File.WriteAllBytesAsync(absoluta, contenido);
    }

    /// <summary>
    /// Al arrancar: pasa la e.firma de cada empresa a la carpeta y nombres con su RFC y
    /// actualiza la base. Idempotente; si los archivos no están en este Storage no toca nada.
    /// </summary>
    public int Migrar(PostgreSqlRepository repo, ILogger log)
    {
        int movidas = 0;

        foreach (EmpresaDto empresa in repo.ObtenerEmpresas())
        {
            // Sin rutas en la base la empresa no tiene e.firma, aunque queden archivos viejos.
            if (string.IsNullOrWhiteSpace(empresa.Rfc) ||
                repo.ObtenerEmpresaSat(empresa.EmpresaId) is not { } sat ||
                string.IsNullOrWhiteSpace(sat.RutaCer) || string.IsNullOrWhiteSpace(sat.RutaKey))
                continue;

            try
            {
                bool cer = Mover(RutaCer(empresa.Rfc), sat.RutaCer, RutaCerAnterior(empresa.EmpresaId));
                bool key = Mover(RutaKey(empresa.Rfc), sat.RutaKey, RutaKeyAnterior(empresa.EmpresaId));

                if (cer && key && (sat.RutaCer != RutaCer(empresa.Rfc) || sat.RutaKey != RutaKey(empresa.Rfc)))
                {
                    repo.ActualizarRutasFiel(empresa.EmpresaId, RutaCer(empresa.Rfc), RutaKey(empresa.Rfc));
                    movidas++;
                }

                BorrarSiVacia(Path.GetDirectoryName(Resolver(RutaCerAnterior(empresa.EmpresaId)))!);
            }
            catch (IOException ex)
            {
                log.LogWarning(ex, "No se pudo mover la e.firma de {Rfc} a su carpeta por RFC.", empresa.Rfc);
            }
        }

        return movidas;
    }

    /// <summary>true si el archivo queda en <paramref name="nueva"/>.</summary>
    private bool Mover(string nueva, params string?[] anteriores)
    {
        if (Existe(nueva))
            return true;

        string? origen = anteriores.FirstOrDefault(Existe);

        if (origen is null)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(Resolver(nueva))!);
        File.Move(Resolver(origen), Resolver(nueva));
        return true;
    }

    private static void BorrarSiVacia(string carpeta)
    {
        if (Directory.Exists(carpeta) && !Directory.EnumerateFileSystemEntries(carpeta).Any())
            Directory.Delete(carpeta);
    }
}
