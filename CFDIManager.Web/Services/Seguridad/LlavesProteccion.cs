using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Npgsql;

namespace CFDIManager.Web.Services.Seguridad;

/// <summary>
/// Clave maestra (CFDI_CLAVE_MAESTRA: variable de entorno o appsettings) que cifra las llaves
/// con que se protegen la CIEC, el SMTP, SINUBE y las sesiones. La misma clave en cada equipo
/// permite mover la base entre equipos sin volver a capturar contraseñas.
/// </summary>
public static class ClaveMaestra
{
    public const string Nombre = "CFDI_CLAVE_MAESTRA";

    /// <summary>32 bytes para AES-256, o null si no está configurada.</summary>
    public static byte[]? Leer(IConfiguration configuracion)
    {
        string? valor = configuracion[Nombre];

        if (string.IsNullOrWhiteSpace(valor))
            return null;

        valor = valor.Trim();

        // La generada por la app es base64 de 32 bytes; cualquier otro texto se deriva con SHA-256.
        try
        {
            byte[] bytes = Convert.FromBase64String(valor);

            if (bytes.Length == 32)
                return bytes;
        }
        catch (FormatException)
        {
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(valor));
    }

    public static string Generar() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}

/// <summary>Llaves de Data Protection en la tabla proteccion_llaves: viajan con el respaldo de la base.</summary>
public sealed class LlavesProteccionPostgres(string cadenaConexion) : IXmlRepository
{
    private const string CrearTabla = """
        CREATE TABLE IF NOT EXISTS proteccion_llaves (
            nombre TEXT PRIMARY KEY,
            xml TEXT NOT NULL,
            creada TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var cn = new NpgsqlConnection(cadenaConexion);
        cn.Open();

        using var cmd = new NpgsqlCommand(CrearTabla + "SELECT xml FROM proteccion_llaves ORDER BY creada;", cn);
        using NpgsqlDataReader dr = cmd.ExecuteReader();

        var llaves = new List<XElement>();

        while (dr.Read())
            llaves.Add(XElement.Parse(dr.GetString(0)));

        return llaves;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var cn = new NpgsqlConnection(cadenaConexion);
        cn.Open();

        using var cmd = new NpgsqlCommand(CrearTabla + """
            INSERT INTO proteccion_llaves (nombre, xml) VALUES (@nombre, @xml)
            ON CONFLICT (nombre) DO UPDATE SET xml = EXCLUDED.xml;
            """, cn);

        cmd.Parameters.AddWithValue("nombre", friendlyName);
        cmd.Parameters.AddWithValue("xml", element.ToString(SaveOptions.DisableFormatting));
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Cifra cada llave con AES-GCM y la clave maestra.</summary>
public sealed class CifradoLlaves(byte[] clave) : IXmlEncryptor
{
    private const int TamanoNonce = 12;
    private const int TamanoTag = 16;

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        byte[] texto = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        byte[] nonce = RandomNumberGenerator.GetBytes(TamanoNonce);
        byte[] tag = new byte[TamanoTag];
        byte[] cifrado = new byte[texto.Length];

        using (var aes = new AesGcm(clave, TamanoTag))
            aes.Encrypt(nonce, texto, cifrado, tag);

        var elemento = new XElement("llaveCifrada",
            new XAttribute("version", "1"),
            Convert.ToBase64String([.. nonce, .. tag, .. cifrado]));

        return new EncryptedXmlInfo(elemento, typeof(DescifradoLlaves));
    }

    internal static XElement Descifrar(byte[] clave, XElement elemento)
    {
        byte[] datos = Convert.FromBase64String(elemento.Value);
        byte[] nonce = datos[..TamanoNonce];
        byte[] tag = datos[TamanoNonce..(TamanoNonce + TamanoTag)];
        byte[] cifrado = datos[(TamanoNonce + TamanoTag)..];
        byte[] texto = new byte[cifrado.Length];

        using (var aes = new AesGcm(clave, TamanoTag))
            aes.Decrypt(nonce, cifrado, tag, texto);

        return XElement.Parse(Encoding.UTF8.GetString(texto));
    }
}

/// <summary>Data Protection lo crea por nombre de tipo, con el IServiceProvider de la app.</summary>
public sealed class DescifradoLlaves(IServiceProvider servicios) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        byte[] clave = ClaveMaestra.Leer(servicios.GetRequiredService<IConfiguration>())
            ?? throw new InvalidOperationException($"Falta {ClaveMaestra.Nombre}: no se pueden leer las llaves de cifrado.");

        try
        {
            return CifradoLlaves.Descifrar(clave, encryptedElement);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new CryptographicException($"{ClaveMaestra.Nombre} no es la misma con la que se cifraron las llaves.");
        }
    }
}
