using System.Globalization;
using CFDIParser.Models;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Imagen PNG con el resumen de Administración de XML del periodo, con las mismas tarjetas (KPI) de la pantalla: por
/// cada lado (emitidos / recibidos) una franja con facturas, nómina, notas de crédito, traslados, pagos y archivos XML,
/// más lo vigente y lo cancelado. En tema claro u oscuro, como la app.
/// </summary>
public static class ResumenImagen
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>Colores de la imagen (fondo, tarjeta, bordes y textos).</summary>
    private sealed record Paleta(string Fondo, string Tarjeta, string Borde, string Texto, string Tenue, string Acento, string Vigente, string Cancelado);

    private static readonly Paleta Oscura = new("#0B1220", "#111A2B", "#26324A", "#E5E7EB", "#94A3B8", "#38BDF8", "#4ADE80", "#F87171");

    private static readonly Paleta Clara = new("#EEF2F7", "#FFFFFF", "#D8E0EA", "#0F172A", "#64748B", "#0284C7", "#16A34A", "#DC2626");

    /// <summary>Tono de un tipo (como .tono-* de la pantalla): color, fondo suave, texto y borde, en cada tema.</summary>
    private sealed record Tono(string Color, string Fondo, string Texto, string Borde);

    private static readonly Dictionary<string, (Tono Claro, Tono Oscuro)> Tonos = new()
    {
        ["azul"] = (new("#0D6EFD", "#CFE2FF", "#052C65", "#9EC5FE"), new("#0D6EFD", "#031633", "#6EA8FE", "#084298")),
        ["verde"] = (new("#198754", "#D1E7DD", "#0A3622", "#A3CFBB"), new("#198754", "#051B11", "#75B798", "#0F5132")),
        ["rojo"] = (new("#DC3545", "#F8D7DA", "#58151C", "#F1AEB5"), new("#DC3545", "#2C0B0E", "#EA868F", "#842029")),
        ["ambar"] = (new("#B45309", "#FFF3CD", "#664D03", "#FFE69C"), new("#C2700F", "#332701", "#FFDA6A", "#997404")),
        ["morado"] = (new("#7E22CE", "#F3E8FF", "#6B21A8", "#E9D5FF"), new("#8B3FD1", "#241640", "#D2B0F2", "#3F2470")),
        ["gris"] = (new("#6C757D", "#E2E3E5", "#2B2F32", "#C4C8CB"), new("#6C757D", "#161719", "#A7ACB1", "#41464B")),
    };

    public sealed record Seccion(string Titulo, string Subtitulo, AdminXmlTotalesDto Totales);

    /// <summary>Una franja de KPI por sección, una debajo de la otra (≈ 960 px de ancho a 100 dpi).</summary>
    /// <param name="claro">Tema claro de la app; si no, oscuro.</param>
    public static byte[] Generar(string empresa, string rfc, string periodo, IReadOnlyList<Seccion> secciones, bool claro = false)
    {
        Paleta p = claro ? Clara : Oscura;

        return Document.Create(doc => doc.Page(page =>
        {
            page.ContinuousSize(690);
            page.Margin(0);
            page.PageColor(p.Fondo);
            page.DefaultTextStyle(t => t.FontSize(7).FontColor(p.Texto));

            page.Content().Padding(12).Column(col =>
            {
                col.Spacing(8);

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("RESUMEN DE CFDI").FontSize(5.5f).Bold().FontColor(p.Acento).LetterSpacing(0.1f);
                        c.Item().Text(empresa).FontSize(11).Bold();
                    });
                    row.AutoItem().AlignBottom().Text($"{rfc} · {periodo}").FontSize(7).FontColor(p.Tenue);
                });

                foreach (var s in secciones)
                    col.Item().Element(e => PintarSeccion(e, s, p, claro));
            });
        })).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 100 }).First();
    }

    private static void PintarSeccion(IContainer contenedor, Seccion s, Paleta p, bool claro)
    {
        var t = s.Totales;
        (string Titulo, int? Cantidad, string Valor, string Tono, string Icono)[] kpis =
        [
            ("FACTURAS", t.Facturas.Cantidad, Moneda(t.Facturas.Total), "azul", IconoFactura),
            ("NÓMINA", t.Nominas.Cantidad, Moneda(t.Nominas.Total), "verde", IconoNomina),
            ("NOTAS DE CRÉDITO", t.NotasCredito.Cantidad, Moneda(t.NotasCredito.Total), "rojo", IconoNota),
            ("TRASLADOS", t.Traslados.Cantidad, Moneda(t.Traslados.Total), "ambar", IconoTraslado),
            ("PAGOS", t.Pagos.Cantidad, Moneda(t.Pagos.Total), "morado", IconoPago),
            ("ARCHIVOS XML", null, t.Archivos.ToString("N0", Mx), "gris", IconoArchivos),
        ];

        contenedor.Background(p.Tarjeta).Border(0.75f).BorderColor(p.Borde).CornerRadius(6).Padding(8).Column(col =>
        {
            col.Spacing(6);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span(s.Titulo).FontSize(8.5f).Bold();
                    text.Span($"  {s.Subtitulo}").FontSize(6.5f).FontColor(p.Tenue);
                });
                row.AutoItem().AlignMiddle().Text(text =>
                {
                    text.Span("Vigente ").FontSize(6).FontColor(p.Tenue);
                    text.Span(Moneda(t.Vigente)).FontSize(6.5f).Bold().FontColor(p.Vigente);
                    text.Span("   Cancelado ").FontSize(6).FontColor(p.Tenue);
                    text.Span(Moneda(t.Cancelado)).FontSize(6.5f).Bold().FontColor(p.Cancelado);
                });
            });

            col.Item().Row(row =>
            {
                row.Spacing(5);
                foreach (var k in kpis)
                {
                    Tono tono = claro ? Tonos[k.Tono].Claro : Tonos[k.Tono].Oscuro;
                    row.RelativeItem().Element(e => PintarKpi(e, k.Titulo, k.Cantidad, k.Valor, k.Icono, tono, p));
                }
            });
        });
    }

    /// <summary>Como .kpi de la pantalla: franja del color a la izquierda, ícono en círculo, nombre, contador y valor.</summary>
    private static void PintarKpi(IContainer contenedor, string titulo, int? cantidad, string valor, string icono, Tono tono, Paleta p)
    {
        contenedor.Background(Mezcla(p.Tarjeta, tono.Color, 0.14f)).Border(0.75f).BorderColor(Mezcla(p.Borde, tono.Color, 0.35f))
            .CornerRadius(5).Row(row =>
            {
                row.ConstantItem(2.2f).Background(tono.Color);
                row.RelativeItem().PaddingVertical(4).PaddingLeft(4).PaddingRight(5).Row(fila =>
                {
                    fila.Spacing(4);
                    // Ícono en un círculo del fondo del tono con borde del color.
                    fila.ConstantItem(15).AlignMiddle().Height(15).Background(tono.Fondo).Border(0.6f)
                        .BorderColor(Mezcla(tono.Fondo, tono.Color, 0.45f)).CornerRadius(7.5f).Padding(3.5f)
                        .Svg(icono.Replace("currentColor", tono.Color));
                    fila.RelativeItem().Column(c =>
                    {
                        c.Item().Text(text =>
                        {
                            text.Span(titulo).FontSize(5.2f).Bold().FontColor(p.Tenue);
                            if (cantidad is int n)
                                text.Span($" {n.ToString("N0", Mx)} ").FontSize(5.2f).Bold().FontColor(tono.Texto).BackgroundColor(tono.Fondo);
                        });
                        c.Item().Text(valor).FontSize(8.6f).Bold().FontColor(tono.Texto);
                    });
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

    // Íconos de Bootstrap Icons (los mismos de la pantalla: bi-receipt, bi-people, bi-arrow-counterclockwise, bi-truck,
    // bi-cash-coin, bi-files). "currentColor" se cambia por el color del tipo.
    private const string IconoFactura = """<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16"><path d="M1.92.506a.5.5 0 0 1 .434.14L3 1.293l.646-.647a.5.5 0 0 1 .708 0L5 1.293l.646-.647a.5.5 0 0 1 .708 0L7 1.293l.646-.647a.5.5 0 0 1 .708 0L9 1.293l.646-.647a.5.5 0 0 1 .708 0l.646.647.646-.647a.5.5 0 0 1 .708 0l.646.647.646-.647a.5.5 0 0 1 .801.13l.5 1A.5.5 0 0 1 15 2v12a.5.5 0 0 1-.053.224l-.5 1a.5.5 0 0 1-.8.13L13 14.707l-.646.647a.5.5 0 0 1-.708 0L11 14.707l-.646.647a.5.5 0 0 1-.708 0L9 14.707l-.646.647a.5.5 0 0 1-.708 0L7 14.707l-.646.647a.5.5 0 0 1-.708 0L5 14.707l-.646.647a.5.5 0 0 1-.708 0L3 14.707l-.646.647a.5.5 0 0 1-.801-.13l-.5-1A.5.5 0 0 1 1 14V2a.5.5 0 0 1 .053-.224l.5-1a.5.5 0 0 1 .367-.27m.217 1.338L2 2.118v11.764l.137.274.51-.51a.5.5 0 0 1 .707 0l.646.647.646-.646a.5.5 0 0 1 .708 0l.646.646.646-.646a.5.5 0 0 1 .708 0l.646.646.646-.646a.5.5 0 0 1 .708 0l.646.646.646-.646a.5.5 0 0 1 .708 0l.646.646.646-.646a.5.5 0 0 1 .708 0l.509.509.137-.274V2.118l-.137-.274-.51.51a.5.5 0 0 1-.707 0L12 1.707l-.646.647a.5.5 0 0 1-.708 0L10 1.707l-.646.647a.5.5 0 0 1-.708 0L8 1.707l-.646.647a.5.5 0 0 1-.708 0L6 1.707l-.646.647a.5.5 0 0 1-.708 0L4 1.707l-.646.647a.5.5 0 0 1-.708 0z"/><path d="M3 4.5a.5.5 0 0 1 .5-.5h6a.5.5 0 1 1 0 1h-6a.5.5 0 0 1-.5-.5m0 2a.5.5 0 0 1 .5-.5h6a.5.5 0 1 1 0 1h-6a.5.5 0 0 1-.5-.5m0 2a.5.5 0 0 1 .5-.5h6a.5.5 0 1 1 0 1h-6a.5.5 0 0 1-.5-.5m0 2a.5.5 0 0 1 .5-.5h6a.5.5 0 0 1 0 1h-6a.5.5 0 0 1-.5-.5m8-6a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 0 1h-1a.5.5 0 0 1-.5-.5m0 2a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 0 1h-1a.5.5 0 0 1-.5-.5m0 2a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 0 1h-1a.5.5 0 0 1-.5-.5m0 2a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 0 1h-1a.5.5 0 0 1-.5-.5"/></svg>""";

    private const string IconoNomina = """<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16"><path d="M15 14s1 0 1-1-1-4-5-4-5 3-5 4 1 1 1 1zm-7.978-1L7 12.996c.001-.264.167-1.03.76-1.72C8.312 10.629 9.282 10 11 10c1.717 0 2.687.63 3.24 1.276.593.69.758 1.457.76 1.72l-.008.002-.014.002zM11 7a2 2 0 1 0 0-4 2 2 0 0 0 0 4m3-2a3 3 0 1 1-6 0 3 3 0 0 1 6 0M6.936 9.28a6 6 0 0 0-1.23-.247A7 7 0 0 0 5 9c-4 0-5 3-5 4q0 1 1 1h4.216A2.24 2.24 0 0 1 5 13c0-1.01.377-2.042 1.09-2.904.243-.294.526-.569.846-.816M4.92 10A5.5 5.5 0 0 0 4 13H1c0-.26.164-1.03.76-1.724.545-.636 1.492-1.256 3.16-1.275ZM1.5 5.5a3 3 0 1 1 6 0 3 3 0 0 1-6 0m3-2a2 2 0 1 0 0 4 2 2 0 0 0 0-4"/></svg>""";

    private const string IconoNota = """<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16"><path fill-rule="evenodd" d="M8 3a5 5 0 1 1-4.546 2.914.5.5 0 0 0-.908-.417A6 6 0 1 0 8 2z"/><path d="M8 4.466V.534a.25.25 0 0 0-.41-.192L5.23 2.308a.25.25 0 0 0 0 .384l2.36 1.966A.25.25 0 0 0 8 4.466"/></svg>""";

    private const string IconoTraslado = """<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16"><path d="M0 3.5A1.5 1.5 0 0 1 1.5 2h9A1.5 1.5 0 0 1 12 3.5V5h1.02a1.5 1.5 0 0 1 1.17.563l1.481 1.85a1.5 1.5 0 0 1 .329.938V10.5a1.5 1.5 0 0 1-1.5 1.5H14a2 2 0 1 1-4 0H5a2 2 0 1 1-3.998-.085A1.5 1.5 0 0 1 0 10.5zm1.294 7.456A2 2 0 0 1 4.732 11h5.536a2 2 0 0 1 .732-.732V3.5a.5.5 0 0 0-.5-.5h-9a.5.5 0 0 0-.5.5v7a.5.5 0 0 0 .294.456M12 10a2 2 0 0 1 1.732 1h.768a.5.5 0 0 0 .5-.5V8.35a.5.5 0 0 0-.11-.312l-1.48-1.85A.5.5 0 0 0 13.02 6H12zm-9 1a1 1 0 1 0 0 2 1 1 0 0 0 0-2m9 0a1 1 0 1 0 0 2 1 1 0 0 0 0-2"/></svg>""";

    private const string IconoPago = """<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16"><path fill-rule="evenodd" d="M11 15a4 4 0 1 0 0-8 4 4 0 0 0 0 8m5-4a5 5 0 1 1-10 0 5 5 0 0 1 10 0"/><path d="M9.438 11.944c.047.596.518 1.06 1.363 1.116v.44h.375v-.443c.875-.061 1.386-.529 1.386-1.207 0-.618-.39-.936-1.09-1.1l-.296-.07v-1.2c.376.043.614.248.671.532h.658c-.047-.575-.54-1.024-1.329-1.073V8.5h-.375v.45c-.747.073-1.255.522-1.255 1.158 0 .562.378.92 1.007 1.066l.248.061v1.272c-.384-.058-.639-.27-.696-.563h-.668zm1.36-1.354c-.369-.085-.569-.26-.569-.522 0-.294.216-.514.572-.578v1.1zm.432.746c.449.104.655.272.655.569 0 .339-.257.571-.709.614v-1.195z"/><path d="M1 0a1 1 0 0 0-1 1v8a1 1 0 0 0 1 1h4.083q.088-.517.258-1H3a2 2 0 0 0-2-2V3a2 2 0 0 0 2-2h10a2 2 0 0 0 2 2v3.528c.38.34.717.728 1 1.154V1a1 1 0 0 0-1-1z"/><path d="M9.998 5.083 10 5a2 2 0 1 0-3.132 1.65 6 6 0 0 1 3.13-1.567"/></svg>""";

    private const string IconoArchivos = """<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16"><path d="M13 0H6a2 2 0 0 0-2 2 2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h7a2 2 0 0 0 2-2 2 2 0 0 0 2-2V2a2 2 0 0 0-2-2m0 13V4a2 2 0 0 0-2-2H5a1 1 0 0 1 1-1h7a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1M3 4a1 1 0 0 1 1-1h7a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z"/></svg>""";
}
