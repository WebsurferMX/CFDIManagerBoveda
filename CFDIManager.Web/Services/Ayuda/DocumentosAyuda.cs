using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CFDIManager.Web.Services.Ayuda;

/// <summary>Un documento de ayuda ya convertido: HTML y sus secciones (## del Markdown) para el índice.</summary>
public sealed record DocumentoAyuda(string Titulo, string Html, IReadOnlyList<(string Id, string Titulo)> Secciones);

/// <summary>
/// Documentos en Markdown que se muestran dentro de la app, convertidos a HTML con Markdig (una vez por documento):
/// <list type="bullet">
/// <item>CFDIManager.Web\Ayuda: Manual de usuario, Registro de cambios y Manual técnico.</item>
/// <item>Documentacion\*.md del repositorio (bitácora y guías técnicas), con el prefijo <see cref="Documentacion"/>;
/// se incrustan tal cual, así que se editan en un solo lugar.</item>
/// </list>
/// Las ligas se ajustan a la página donde se muestran: "#seccion" lleva la ruta de la página (la app usa base href "/")
/// y "Guia.md#seccion" abre ese documento en el Manual técnico. Las anclas van sin acentos, como los id de Markdig.
/// </summary>
public static class DocumentosAyuda
{
    public const string Manual = "Manual.md";
    public const string Novedades = "Novedades.md";
    public const string ManualTecnico = "ManualTecnico.md";
    public const string Documentacion = "Documentacion.";

    /// <summary>Ruta del Manual técnico; un documento de Documentacion se abre en RutaTecnica/&lt;nombre sin .md&gt;.</summary>
    public const string RutaTecnica = "manual-tecnico";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
        .Build();

    private static readonly Regex LigaMd = new(@"^(?<nombre>[\w\-+.]+)\.md(?<ancla>#.*)?$", RegexOptions.Compiled);
    private static readonly Regex NumeroInicial = new(@"^#[\d\-]+(?=\p{L})", RegexOptions.Compiled);

    private static readonly ConcurrentDictionary<(string Archivo, string Ruta), DocumentoAyuda> Cache = new();

    /// <summary>Documentos de Documentacion incrustados (nombre sin .md), para el menú del Manual técnico.</summary>
    public static IReadOnlyList<string> DocumentosTecnicos { get; } = typeof(DocumentosAyuda).Assembly
        .GetManifestResourceNames()
        .Where(n => n.Contains(".Ayuda." + Documentacion, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
        .Select(n => n[(n.IndexOf(".Ayuda." + Documentacion, StringComparison.Ordinal) + 7 + Documentacion.Length)..^3])
        .Order()
        .ToList();

    /// <summary>Existe el documento (archivo de Ayuda, o "Documentacion.&lt;nombre&gt;.md").</summary>
    public static bool Existe(string archivo) => NombreRecurso(archivo) is not null;

    /// <param name="archivo">Archivo de Ayuda, o "Documentacion.&lt;nombre&gt;.md".</param>
    /// <param name="ruta">Ruta de la página donde se muestra (sin "/" inicial), para las ligas a secciones.</param>
    public static DocumentoAyuda Obtener(string archivo, string ruta) => Cache.GetOrAdd((archivo, ruta), Convertir);

    private static string? NombreRecurso(string archivo) => typeof(DocumentosAyuda).Assembly
        .GetManifestResourceNames()
        .FirstOrDefault(n => n.EndsWith(".Ayuda." + archivo, StringComparison.Ordinal));

    private static DocumentoAyuda Convertir((string Archivo, string Ruta) clave)
    {
        Assembly ensamblado = typeof(DocumentosAyuda).Assembly;
        string recurso = NombreRecurso(clave.Archivo) ?? throw new FileNotFoundException("No existe el documento.", clave.Archivo);
        using var lector = new StreamReader(ensamblado.GetManifestResourceStream(recurso)!);
        string markdown = lector.ReadToEnd();

        MarkdownDocument documento = Markdown.Parse(markdown, Pipeline);
        AjustarLigas(documento, clave.Ruta);

        string titulo = documento.Descendants<HeadingBlock>().FirstOrDefault(h => h.Level == 1) is { } h1
            ? Texto(h1)
            : Path.GetFileNameWithoutExtension(clave.Archivo);
        var secciones = documento.Descendants<HeadingBlock>()
            .Where(h => h.Level == 2)
            .Select(h => (h.GetAttributes().Id ?? "", Texto(h)))
            .ToList();

        return new DocumentoAyuda(titulo, documento.ToHtml(Pipeline), secciones);
    }

    private static void AjustarLigas(MarkdownDocument documento, string ruta)
    {
        foreach (LinkInline liga in documento.Descendants<LinkInline>().Where(l => !l.IsImage && l.Url is not null))
        {
            string url = liga.Url!;

            if (url.StartsWith('#'))
            {
                liga.Url = ruta + Ancla(url);
            }
            else if (LigaMd.Match(url) is { Success: true } m)
            {
                liga.Url = $"{RutaTecnica}/{m.Groups["nombre"].Value}{Ancla(m.Groups["ancla"].Value)}";
            }
        }
    }

    /// <summary>"#8-bitácora-de-cambios" → "#8-bitacora-de-cambios" (Markdig quita los acentos de los id).</summary>
    private static string Ancla(string ancla)
    {
        if (ancla.Length == 0)
        {
            return "";
        }

        string sinAcentos = string.Concat(Uri.UnescapeDataString(ancla).Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

        // Markdig también quita los números del inicio ("8. Bitácora" → "bitacora"); GitHub los deja ("8-bitácora").
        return NumeroInicial.Replace(sinAcentos.Normalize(NormalizationForm.FormC).ToLowerInvariant(), "#");
    }

    private static string Texto(HeadingBlock encabezado) =>
        encabezado.Inline is null ? "" : string.Concat(encabezado.Inline.Descendants<LiteralInline>().Select(l => l.Content.ToString()));
}
