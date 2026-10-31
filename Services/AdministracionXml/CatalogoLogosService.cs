using System.Text.RegularExpressions;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>Logo por RFC emisor para la representación impresa: Storage/Logos/{RFC}.png|.jpg.</summary>
public partial class CatalogoLogosService
{
    private readonly string _carpeta;

    public CatalogoLogosService(IWebHostEnvironment ambiente)
    {
        _carpeta = Path.Combine(ambiente.ContentRootPath, "Storage", "Logos");
    }

    [GeneratedRegex("^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$")]
    private static partial Regex RfcValido();

    public List<string> Listar()
    {
        return Directory.Exists(_carpeta)
            ? Directory.EnumerateFiles(_carpeta)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Order()
                .ToList()
            : new();
    }

    public byte[]? Leer(string rfc)
    {
        string? ruta = Ruta(rfc);
        return ruta is null ? null : File.ReadAllBytes(ruta);
    }

    public string? TipoContenido(string rfc) =>
        Ruta(rfc) is string r ? (r.EndsWith(".png") ? "image/png" : "image/jpeg") : null;

    public void Guardar(string rfc, byte[] imagen)
    {
        rfc = rfc.Trim().ToUpperInvariant();

        if (!RfcValido().IsMatch(rfc))
        {
            throw new InvalidOperationException("RFC inválido.");
        }

        string? extension =
            imagen.Length > 4 && imagen[0] == 0x89 && imagen[1] == 0x50 && imagen[2] == 0x4E && imagen[3] == 0x47 ? ".png"
            : imagen.Length > 2 && imagen[0] == 0xFF && imagen[1] == 0xD8 ? ".jpg"
            : null;

        if (extension is null)
        {
            throw new InvalidOperationException("El logo debe ser PNG o JPG.");
        }

        Directory.CreateDirectory(_carpeta);
        Quitar(rfc);
        File.WriteAllBytes(Path.Combine(_carpeta, rfc + extension), imagen);
    }

    public void Quitar(string rfc)
    {
        if (Ruta(rfc) is string ruta)
        {
            File.Delete(ruta);
        }
    }

    private string? Ruta(string rfc)
    {
        rfc = (rfc ?? "").Trim().ToUpperInvariant();

        if (!RfcValido().IsMatch(rfc))
        {
            return null;
        }

        return new[] { ".png", ".jpg" }
            .Select(ext => Path.Combine(_carpeta, rfc + ext))
            .FirstOrDefault(File.Exists);
    }
}
