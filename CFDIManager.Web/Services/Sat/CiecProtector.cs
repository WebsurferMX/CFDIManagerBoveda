using Microsoft.AspNetCore.DataProtection;

namespace CFDIManager.Web.Services.Sat;

/// <summary>
/// Cifra una contraseña antes de guardarla en la base. Los valores cifrados llevan el
/// prefijo "dp:"; uno sin prefijo es una contraseña guardada antes de cifrar.
/// </summary>
public abstract class ProtectorConPrefijo
{
    private const string Prefijo = "dp:";

    private readonly IDataProtector _protector;
    private readonly string _mensajeIlegible;

    protected ProtectorConPrefijo(IDataProtectionProvider proveedor, string proposito, string mensajeIlegible)
    {
        _protector = proveedor.CreateProtector(proposito);
        _mensajeIlegible = mensajeIlegible;
    }

    public static bool EstaCifrada(string? guardada) => guardada?.StartsWith(Prefijo) == true;

    public string? Cifrar(string? valor) =>
        string.IsNullOrEmpty(valor) || EstaCifrada(valor)
            ? valor
            : Prefijo + _protector.Protect(valor);

    /// <summary>El valor en claro; lanza si fue cifrado con llaves que ya no existen.</summary>
    public string? Descifrar(string? guardada)
    {
        if (!EstaCifrada(guardada))
            return guardada;

        try
        {
            return _protector.Unprotect(guardada![Prefijo.Length..]);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new InvalidOperationException(_mensajeIlegible);
        }
    }
}

/// <summary>CIEC de empresas_sat.sat_password.</summary>
public class CiecProtector(IDataProtectionProvider proveedor) : ProtectorConPrefijo(
    proveedor,
    "CFDIManager.Ciec",
    "No se pudo leer la CIEC guardada (cambiaron las llaves de cifrado). Captúrala de nuevo en Empresas → Acceso SAT.");

/// <summary>Contraseña de la llave privada de la e.firma (empresas_sat.password_key).</summary>
public class FielPasswordProtector(IDataProtectionProvider proveedor) : ProtectorConPrefijo(
    proveedor,
    "CFDIManager.FielPassword",
    "No se pudo leer la contraseña de la e.firma (cambiaron las llaves de cifrado). Captúrala de nuevo en Empresas → Acceso SAT.");
