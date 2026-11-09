using CFDIParser.Models;

namespace CFDIManager.Web.Services;

/// <summary>
/// Ruta XML general (AppSettings:CarpetaXml). Cada empresa guarda e indexa sus XML en
/// &lt;ruta general&gt;\RFC; ya no se define una ruta por empresa.
/// </summary>
public class RutaXmlService
{
    private readonly IConfiguration _configuracion;
    private readonly IWebHostEnvironment _ambiente;

    public RutaXmlService(
        IConfiguration configuracion,
        IWebHostEnvironment ambiente)
    {
        _configuracion = configuracion;
        _ambiente = ambiente;
    }

    /// <summary>Carpeta raíz; "Storage\XML" (el valor de omisión) queda dentro de la carpeta de datos (CarpetaStorage).</summary>
    public string CarpetaGeneral =>
        CarpetaStorage.Resolver(
            _configuracion["AppSettings:CarpetaXml"] is { Length: > 0 } ruta
                ? ruta
                : Path.Combine("Storage", "XML"));

    public string CarpetaEmpresa(
        string rfc)
    {
        string limpio = (rfc ?? "").Trim().ToUpperInvariant();

        foreach (char caracter in Path.GetInvalidFileNameChars())
        {
            limpio = limpio.Replace(caracter, '_');
        }

        return Path.Combine(CarpetaGeneral, limpio);
    }

    public string CarpetaEmpresa(
        EmpresaDto empresa) =>
        CarpetaEmpresa(empresa.Rfc);

    /// <summary>Copias con adenda de SINUBE: Emitidos\Adendas\AAAA\MM\UUID.xml (aparte de la copia del SAT).</summary>
    public const string CarpetaAdendas = "Adendas";

    public string CarpetaAdendasPeriodo(EmpresaDto empresa, int anio, int mes) =>
        Path.Combine(CarpetaEmpresa(empresa), "Emitidos", CarpetaAdendas, anio.ToString(), mes.ToString("00"));

    /// <summary>
    /// Carpetas dentro de la de la empresa que no son parte de la bóveda (no se importan ni se registran al sincronizar):
    /// NoIdentificados y las copias con adenda, que repiten el UUID de la copia del SAT.
    /// </summary>
    public static bool FueraDeBoveda(string ruta)
    {
        char s = Path.DirectorySeparatorChar;
        return ruta.Contains($"{s}NoIdentificados{s}", StringComparison.OrdinalIgnoreCase) ||
               ruta.Contains($"{s}Emitidos{s}{CarpetaAdendas}{s}", StringComparison.OrdinalIgnoreCase);
    }
}
