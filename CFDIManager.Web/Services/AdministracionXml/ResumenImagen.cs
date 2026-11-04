using System.Globalization;
using CFDIParser.Models;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Imagen PNG con el resumen de Administración de XML del periodo: emitidos (ingresos) y recibidos (egresos), cada uno
/// con facturas, nómina, notas de crédito, traslados, pagos y archivos XML, más lo vigente y lo cancelado. Mismo
/// estilo que las tarjetas de la pantalla (franja de color por tipo), en tema oscuro o claro según la app.
/// </summary>
public static class ResumenImagen
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>Colores de la imagen. Los tonos de cada tipo son los mismos en los dos temas.</summary>
    private sealed record Paleta(
        string Fondo, string Tarjeta, string Borde, string Texto, string Tenue, string Acento, string Vigente,
        string Cancelado, string Pie, float Tinte);

    private static readonly Paleta Oscura = new(
        "#0B1220", "#141C2B", "#26324A", "#FFFFFF", "#94A3B8", "#38BDF8", "#4ADE80", "#F87171", "#64748B", 0.10f);

    private static readonly Paleta Clara = new(
        "#EEF2F7", "#FFFFFF", "#D8E0EA", "#0F172A", "#64748B", "#0284C7", "#16A34A", "#DC2626", "#94A3B8", 0.07f);

    public sealed record Seccion(string Titulo, string Subtitulo, AdminXmlTotalesDto Totales);

    /// <param name="juntas">Con dos secciones: una junto a la otra (imagen ancha) en lugar de una debajo de la otra.</param>
    /// <param name="claro">Tema claro de la app; si no, oscuro.</param>
    public static byte[] Generar(
        string empresa, string rfc, string periodo, IReadOnlyList<Seccion> secciones, bool juntas = false, bool claro = false)
    {
        Paleta p = claro ? Clara : Oscura;
        juntas &= secciones.Count > 1;

        return Document.Create(doc => doc.Page(page =>
        {
            page.ContinuousSize(juntas ? 1480 : 760);
            page.Margin(0);
            page.PageColor(p.Fondo);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(p.Texto));

            page.Content().Padding(22).Column(col =>
            {
                col.Spacing(14);

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("RESUMEN DE CFDI").FontSize(8).Bold().FontColor(p.Acento).LetterSpacing(0.12f);
                        c.Item().Text(empresa).FontSize(16).Bold();
                        c.Item().Text($"{rfc} · {periodo}").FontSize(9.5f).FontColor(p.Tenue);
                    });
                    row.AutoItem().AlignBottom().Text($"Generado {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(7.5f).FontColor(p.Tenue);
                });

                if (juntas)
                {
                    col.Item().Row(row =>
                    {
                        row.Spacing(14);
                        foreach (var s in secciones)
                            row.RelativeItem().Element(e => PintarSeccion(e, s, p));
                    });
                }
                else
                {
                    foreach (var s in secciones)
                        col.Item().Element(e => PintarSeccion(e, s, p));
                }

                col.Item().Text("CFDI Manager · Administración de XML").FontSize(7).FontColor(p.Pie);
            });
        })).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 75 }).First();
    }

    private static void PintarSeccion(IContainer contenedor, Seccion s, Paleta p)
    {
        var t = s.Totales;
        (string Titulo, int? Cantidad, string Valor, string Color)[] tarjetas =
        [
            ("FACTURAS", t.Facturas.Cantidad, Moneda(t.Facturas.Total), "#3B82F6"),
            ("NÓMINA", t.Nominas.Cantidad, Moneda(t.Nominas.Total), "#22C55E"),
            ("NOTAS DE CRÉDITO", t.NotasCredito.Cantidad, Moneda(t.NotasCredito.Total), "#EF4444"),
            ("TRASLADOS", t.Traslados.Cantidad, Moneda(t.Traslados.Total), "#F59E0B"),
            ("PAGOS", t.Pagos.Cantidad, Moneda(t.Pagos.Total), "#A855F7"),
            ("ARCHIVOS XML", null, t.Archivos.ToString("N0", Mx), "#94A3B8"),
        ];

        contenedor.Background(p.Tarjeta).Border(1).BorderColor(p.Borde).CornerRadius(10).Padding(14).Column(col =>
        {
            col.Spacing(10);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span(s.Titulo).FontSize(12).Bold();
                    text.Span($"  {s.Subtitulo}").FontSize(9).FontColor(p.Tenue);
                });
                row.AutoItem().AlignMiddle().Text(text =>
                {
                    text.Span("Vigente ").FontSize(8.5f).FontColor(p.Tenue);
                    text.Span(Moneda(t.Vigente)).FontSize(9).Bold().FontColor(p.Vigente);
                    text.Span("    Cancelado ").FontSize(8.5f).FontColor(p.Tenue);
                    text.Span(Moneda(t.Cancelado)).FontSize(9).Bold().FontColor(p.Cancelado);
                });
            });

            // Dos filas de tres tarjetas.
            foreach (var fila in tarjetas.Chunk(3))
            {
                col.Item().Row(row =>
                {
                    row.Spacing(10);
                    foreach (var k in fila)
                        row.RelativeItem().Element(e => PintarTarjeta(e, k.Titulo, k.Cantidad, k.Valor, k.Color, p));
                });
            }
        });
    }

    private static void PintarTarjeta(IContainer contenedor, string titulo, int? cantidad, string valor, string color, Paleta p)
    {
        bool vacia = cantidad == 0;

        contenedor.Background(Mezcla(p.Tarjeta, color, p.Tinte)).Border(1).BorderColor(Mezcla(p.Tarjeta, color, vacia ? 0.25f : 0.55f))
            .CornerRadius(8).Row(row =>
            {
                // Franja de color a la izquierda.
                row.ConstantItem(4).Background(vacia ? Mezcla(p.Tarjeta, color, 0.35f) : color);
                row.RelativeItem().PaddingVertical(9).PaddingHorizontal(11).Column(c =>
                {
                    c.Item().Text(text =>
                    {
                        text.Span(titulo).FontSize(8).Bold().FontColor(p.Tenue);
                        if (cantidad is int n)
                            text.Span($"  {n.ToString("N0", Mx)}").FontSize(8).Bold().FontColor(vacia ? p.Tenue : color);
                    });
                    c.Item().PaddingTop(2).Text(valor).FontSize(14).Bold().FontColor(vacia ? p.Tenue : p.Texto);
                });
            });
    }

    private static string Moneda(decimal v) => v.ToString("C2", Mx);

    /// <summary>El color sobre el fondo indicado, en la proporción indicada (0 = fondo, 1 = color).</summary>
    private static string Mezcla(string fondo, string hex, float proporcion)
    {
        static (int R, int G, int B) Rgb(string h) =>
            (Convert.ToInt32(h[1..3], 16), Convert.ToInt32(h[3..5], 16), Convert.ToInt32(h[5..7], 16));

        var (r1, g1, b1) = Rgb(fondo);
        var (r2, g2, b2) = Rgb(hex);
        int M(int a, int b) => (int)Math.Round(a + (b - a) * proporcion);
        return $"#{M(r1, r2):X2}{M(g1, g2):X2}{M(b1, b2):X2}";
    }
}
