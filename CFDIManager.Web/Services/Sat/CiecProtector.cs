using Microsoft.AspNetCore.DataProtection;

namespace CFDIManager.Web.Services.Sat;

/// <summary>
/// Cifra la CIEC antes de guardarla en empresas_sat.sat_password. Los valores cifrados
/// llevan el prefijo "dp:"; uno sin prefijo es una contraseña guardada antes de cifrar.
/// </summary>
public class CiecProtector
{
    private const string Prefijo = "dp:";

    private readonly IDataProtector _protector;

    public CiecProtector(IDataProtectionProvider proveedor)
    {
        _protector = proveedor.CreateProtector("CFDIManager.Ciec");
    }

    public string? Cifrar(string? ciec) =>
        string.IsNullOrEmpty(ciec) || ciec.StartsWith(Prefijo)
            ? ciec
            : Prefijo + _protector.Protect(ciec);

    /// <summary>La CIEC en claro; lanza si fue cifrada con llaves que ya no existen.</summary>
    public string? Descifrar(string? guardada)
    {
        if (string.IsNullOrEmpty(guardada) || !guardada.StartsWith(Prefijo))
            return guardada;

        try
        {
            return _protector.Unprotect(guardada[Prefijo.Length..]);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new InvalidOperationException(
                "No se pudo leer la CIEC guardada (cambiaron las llaves de cifrado). Captúrala de nuevo en Empresas → Acceso SAT.");
        }
    }
}
