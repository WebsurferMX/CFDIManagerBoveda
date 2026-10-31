using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CFDIManager.Web.Services.Sat;

/// <summary>
/// e.firma (FIEL) abierta en memoria: certificado .cer (DER) y llave .key
/// (PKCS#8 cifrada, como la entrega el SAT).
/// </summary>
public sealed class FielSat : IDisposable
{
    private const string OidRfc =
        "2.5.4.45";

    private const string OidUnidadOrganizacional =
        "2.5.4.11";

    private readonly RSA _llave;

    private FielSat(
        X509Certificate2 certificado,
        RSA llave)
    {
        Certificado =
            certificado;

        _llave =
            llave;
    }

    public X509Certificate2 Certificado { get; }

    /// <summary>RFC del titular, tomado del atributo x500UniqueIdentifier del certificado.</summary>
    public string Rfc =>
        LeerRfc(Certificado);

    public static string LeerRfc(
        X509Certificate2 certificado) =>
        LeerAtributo(certificado, OidRfc)
            .Split('/')[0]
            .Trim()
            .ToUpperInvariant();

    /// <summary>
    /// Los certificados de sello digital (CSD) traen unidad organizacional (sucursal);
    /// la e.firma no. El SAT solo acepta e.firma en la descarga masiva.
    /// </summary>
    public bool EsFiel =>
        EsCertificadoFiel(Certificado);

    public static bool EsCertificadoFiel(
        X509Certificate2 certificado) =>
        string.IsNullOrEmpty(
            LeerAtributo(
                certificado,
                OidUnidadOrganizacional));

    public DateTime VigenteDesde =>
        Certificado.NotBefore;

    public DateTime VigenteHasta =>
        Certificado.NotAfter;

    public bool EstaVigente =>
        DateTime.Now >= VigenteDesde &&
        DateTime.Now <= VigenteHasta;

    /// <summary>Número de serie en decimal, como lo pide la firma XML del SAT.</summary>
    public string SerieDecimal =>
        new BigInteger(
                Certificado.GetSerialNumber(),
                isUnsigned: true,
                isBigEndian: false)
            .ToString();

    /// <summary>Nombre del emisor en formato RFC 4514, igual que la librería de referencia de NodeCfdi.</summary>
    public string EmisorCertificado =>
        string.Join(
            ",",
            Certificado.IssuerName
                .EnumerateRelativeDistinguishedNames(reversed: false)
                .Where(rdn => !rdn.HasMultipleElements)
                .Select(rdn =>
                {
                    string oid = rdn.GetSingleElementType().Value ?? "";
                    string tipo = NombresRfc4514.GetValueOrDefault(oid, oid);
                    return $"{tipo}={EscaparRfc4514(rdn.GetSingleElementValue() ?? "")}";
                }));

    private static readonly Dictionary<string, string> NombresRfc4514 =
        new()
        {
            ["2.5.4.3"] = "CN",
            ["2.5.4.7"] = "L",
            ["2.5.4.8"] = "ST",
            ["2.5.4.10"] = "O",
            ["2.5.4.11"] = "OU",
            ["2.5.4.6"] = "C",
            ["2.5.4.9"] = "STREET",
            ["0.9.2342.19200300.100.1.25"] = "DC",
            ["0.9.2342.19200300.100.1.1"] = "UID"
        };

    private static string EscaparRfc4514(
        string valor)
    {
        var texto = new StringBuilder();

        for (int i = 0; i < valor.Length; i++)
        {
            char c = valor[i];

            bool escapar =
                c is ',' or '+' or '"' or '\\' or '<' or '>' or ';' ||
                (i == 0 && c is '#' or ' ') ||
                (i == valor.Length - 1 && c == ' ');

            if (escapar)
            {
                texto.Append('\\');
            }

            texto.Append(c);
        }

        return texto.ToString();
    }

    public string CertificadoBase64 =>
        Convert.ToBase64String(
            Certificado.RawData);

    public static FielSat Abrir(
        string rutaCer,
        string rutaKey,
        string password) =>
        Abrir(
            File.ReadAllBytes(rutaCer),
            File.ReadAllBytes(rutaKey),
            password);

    public static FielSat Abrir(
        byte[] cer,
        byte[] key,
        string password)
    {
        X509Certificate2 certificado;

        try
        {
            certificado =
                X509CertificateLoader.LoadCertificate(
                    cer);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "El archivo .cer no es un certificado válido.",
                ex);
        }

        RSA llave =
            RSA.Create();

        try
        {
            llave.ImportEncryptedPkcs8PrivateKey(
                password.AsSpan(),
                key,
                out _);
        }
        catch (CryptographicException ex)
        {
            llave.Dispose();
            certificado.Dispose();

            throw new InvalidOperationException(
                "No se pudo abrir la llave privada (.key): la contraseña es incorrecta " +
                "o el archivo no es una llave de e.firma.",
                ex);
        }

        using RSA? publica =
            certificado.GetRSAPublicKey();

        byte[] moduloCertificado =
            publica?.ExportParameters(false).Modulus ?? [];

        byte[] moduloLlave =
            llave.ExportParameters(false).Modulus ?? [];

        if (!moduloCertificado.AsSpan().SequenceEqual(moduloLlave))
        {
            llave.Dispose();
            certificado.Dispose();

            throw new InvalidOperationException(
                "La llave privada (.key) no corresponde al certificado (.cer).");
        }

        return new FielSat(
            certificado,
            llave);
    }

    /// <summary>Firma RSA-SHA1 (PKCS#1 v1.5) en Base64, el algoritmo que exige el servicio del SAT.</summary>
    public string Firmar(
        string contenido)
    {
        byte[] firma =
            _llave.SignData(
                Encoding.UTF8.GetBytes(
                    contenido),
                HashAlgorithmName.SHA1,
                RSASignaturePadding.Pkcs1);

        return Convert.ToBase64String(
            firma);
    }

    private static string LeerAtributo(
        X509Certificate2 certificado,
        string oid)
    {
        foreach (X500RelativeDistinguishedName rdn
                 in certificado.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            if (rdn.HasMultipleElements)
            {
                continue;
            }

            if (rdn.GetSingleElementType().Value == oid)
            {
                return rdn.GetSingleElementValue() ?? "";
            }
        }

        return "";
    }

    public void Dispose()
    {
        _llave.Dispose();
        Certificado.Dispose();
    }
}
