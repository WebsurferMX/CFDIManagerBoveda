using System.Collections.Concurrent;
using System.Reflection;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace CFDIManager.Web.Services.Ayuda;

/// <summary>Un documento de ayuda ya convertido: HTML y sus secciones (## del Markdown) para el índice.</summary>
public sealed record DocumentoAyuda(string Titulo, string Html, IReadOnlyList<(string Id, string Titulo)> Secciones);

/// <summary>
/// Manual de usuario y Registro de cambios: Markdown en CFDIManager.Web\Ayuda (recursos incrustados, viajan dentro de
/// la DLL) convertido a HTML con Markdig. Se convierte una vez por documento.
/// </summary>
public static class DocumentosAyuda
{
    public const string Manual = "Manual.md";
    public const string Novedades = "Novedades.md";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
        .Build();

    private static readonly ConcurrentDictionary<string, DocumentoAyuda> Cache = new();

    public static DocumentoAyuda Obtener(string archivo) => Cache.GetOrAdd(archivo, Convertir);

    private static DocumentoAyuda Convertir(string archivo)
    {
        Assembly ensamblado = typeof(DocumentosAyuda).Assembly;
        string recurso = ensamblado.GetManifestResourceNames().Single(n => n.EndsWith(".Ayuda." + archivo, StringComparison.Ordinal));
        using var lector = new StreamReader(ensamblado.GetManifestResourceStream(recurso)!);
        string markdown = lector.ReadToEnd();

        MarkdownDocument documento = Markdown.Parse(markdown, Pipeline);
        string titulo = documento.Descendants<HeadingBlock>().FirstOrDefault(h => h.Level == 1) is { } h1
            ? Texto(h1)
            : Path.GetFileNameWithoutExtension(archivo);
        var secciones = documento.Descendants<HeadingBlock>()
            .Where(h => h.Level == 2)
            .Select(h => (h.GetAttributes().Id ?? "", Texto(h)))
            .ToList();

        return new DocumentoAyuda(titulo, documento.ToHtml(Pipeline), secciones);
    }

    private static string Texto(HeadingBlock encabezado) =>
        encabezado.Inline is null ? "" : string.Concat(encabezado.Inline.Descendants<Markdig.Syntax.Inlines.LiteralInline>().Select(l => l.Content.ToString()));
}
