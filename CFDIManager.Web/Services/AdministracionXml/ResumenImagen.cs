using System.Globalization;
using CFDIParser.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Imagen PNG con el resumen de Administración de XML del periodo: emitidos (ingresos) y recibidos (egresos), cada uno
/// con facturas, nómina, notas de crédito, traslados, pagos y archivos XML, más lo vigente y lo cancelado. Mismo
/// estilo que las tarjetas de la pantalla (fondo oscuro, franja de color por tipo).
/// </summary>
public static class ResumenImagen
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    private const string Fondo = "#0B1220";
    private const string Tarjeta = "#141C2B";
    private const string Borde = "#26324A";
    private const string TextoTenue = "#94A3B8";

    public sealed record Seccion(string Titulo, string Subtitulo, AdminXmlTotalesDto Totales);

    public static byte[] Generar(string empresa, string rfc, string periodo, IReadOnlyList<Seccion> secciones) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.ContinuousSize(760);
            page.Margin(0);
            page.PageColor(Fondo);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.White));

            page.Content().Padding(22).Column(col =>
            {
                col.Spacing(14);

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("RESUMEN DE CFDI").FontSize(8).Bold().FontColor("#38BDF8").LetterSpacing(0.12f);
                        c.Item().Text(empresa).FontSize(16).Bold();
                        c.Item().Text($"{rfc} · {periodo}").FontSize(9.5f).FontColor(TextoTenue);
                    });
                    row.AutoItem().AlignBottom().Text($"Generado {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(7.5f).FontColor(TextoTenue);
                });

                foreach (var s in secciones)
                    col.Item().Element(e => PintarSeccion(e, s));

                col.Item().Text("CFDI Manager · Administración de XML").FontSize(7).FontColor("#64748B");
            });
        })).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 150 }).First();

    private static void PintarSeccion(IContainer contenedor, Seccion s)
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

        contenedor.Background(Tarjeta).Border(1).BorderColor(Borde).CornerRadius(10).Padding(14).Column(col =>
        {
            col.Spacing(10);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span(s.Titulo).FontSize(12).Bold();
                    text.Span($"  {s.Subtitulo}").FontSize(9).FontColor(TextoTenue);
                });
                row.AutoItem().AlignMiddle().Text(text =>
                {
                    text.Span("Vigente ").FontSize(8.5f).FontColor(TextoTenue);
                    text.Span(Moneda(t.Vigente)).FontSize(9).Bold().FontColor("#4ADE80");
                    text.Span("    Cancelado ").FontSize(8.5f).FontColor(TextoTenue);
                    text.Span(Moneda(t.Cancelado)).FontSize(9).Bold().FontColor("#F87171");
                });
            });

            // Dos filas de tres tarjetas.
            foreach (var fila in tarjetas.Chunk(3))
            {
                col.Item().Row(row =>
                {
                    row.Spacing(10);
                    foreach (var k in fila)
                        row.RelativeItem().Element(e => PintarTarjeta(e, k.Titulo, k.Cantidad, k.Valor, k.Color));
                });
            }
        });
    }

    private static void PintarTarjeta(IContainer contenedor, string titulo, int? cantidad, string valor, string color)
    {
        bool vacia = cantidad == 0;

        contenedor.Background(Mezcla(color, 0.10f)).Border(1).BorderColor(Mezcla(color, vacia ? 0.25f : 0.55f))
            .CornerRadius(8).Row(row =>
            {
                // Franja de color a la izquierda.
                row.ConstantItem(4).Background(vacia ? Mezcla(color, 0.35f) : color);
                row.RelativeItem().PaddingVertical(9).PaddingHorizontal(11).Column(c =>
                {
                    c.Item().Text(text =>
                    {
                        text.Span(titulo).FontSize(8).Bold().FontColor(TextoTenue);
                        if (cantidad is int n)
                            text.Span($"  {n.ToString("N0", Mx)}").FontSize(8).Bold().FontColor(vacia ? TextoTenue : color);
                    });
                    c.Item().PaddingTop(2).Text(valor).FontSize(14).Bold().FontColor(vacia ? TextoTenue : Colors.White);
                });
            });
    }

    private static string Moneda(decimal v) => v.ToString("C2", Mx);

    /// <summary>El color sobre el fondo de la tarjeta, en la proporción indicada (0 = fondo, 1 = color).</summary>
    private static string Mezcla(string hex, float proporcion)
    {
        static (int R, int G, int B) Rgb(string h) =>
            (Convert.ToInt32(h[1..3], 16), Convert.ToInt32(h[3..5], 16), Convert.ToInt32(h[5..7], 16));

        var (r1, g1, b1) = Rgb(Tarjeta);
        var (r2, g2, b2) = Rgb(hex);
        int M(int a, int b) => (int)Math.Round(a + (b - a) * proporcion);
        return $"#{M(r1, r2):X2}{M(g1, g2):X2}{M(b1, b2):X2}";
    }
}
