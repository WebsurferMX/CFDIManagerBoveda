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

    /// <summary>Carpeta raíz; una ruta relativa se toma desde la carpeta de la aplicación.</summary>
    public string CarpetaGeneral =>
        Path.GetFullPath(
            _configuracion["AppSettings:CarpetaXml"] is { Length: > 0 } ruta
                ? ruta
                : Path.Combine("Storage", "XML"),
            _ambiente.ContentRootPath);

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
}
