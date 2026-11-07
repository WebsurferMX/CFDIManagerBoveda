using System.Net;
using System.Text;

namespace CFDIManager.Web.Services.Correo;

// Plantilla HTML de los correos de la aplicación: tablas y estilos en línea para que se vea
// igual en Outlook, Gmail y celulares. Todo el texto recibido se codifica (sin HTML del usuario).
public static class PlantillaCorreo
{
    private const string ColorMarca = "#052767";
    private const string ColorAcento = "#0891b2";

    public sealed class Contenido
    {
        public string Titulo { get; init; } = "";

        public string Saludo { get; init; } = "";

        public IReadOnlyList<string> Parrafos { get; init; } = [];

        /// <summary>Tabla de datos (etiqueta, valor), p. ej. usuario y contraseña temporal.</summary>
        public IReadOnlyList<(string Etiqueta, string Valor)> Datos { get; init; } = [];

        /// <summary>Valores de Datos que se muestran en letra monoespaciada (contraseñas, RFC).</summary>
        public IReadOnlyCollection<string> DatosResaltados { get; init; } = [];

        public string? TextoBoton { get; init; }

        public string? UrlBoton { get; init; }

        public string? Nota { get; init; }

        /// <summary>Content-ID de una imagen en línea (p. ej. el resumen de XML) que va después de los párrafos.</summary>
        public string? ImagenCid { get; init; }

        /// <summary>Ancho natural de la imagen en px: no se agranda más allá (se vería borrosa).</summary>
        public int ImagenAnchoMaximo { get; init; } = 696;

        /// <summary>Ancho máximo de la tarjeta del correo en px; en pantallas angostas se ajusta.</summary>
        public int AnchoMaximo { get; init; } = 760;
    }

    /// <summary>Pie de todos los correos si no se ha configurado otro (Configuración → Correo).</summary>
    public const string PieOmision =
        "Este correo lo envió automáticamente CFDI Manager. Si no esperabas este mensaje, avisa al administrador de tu empresa.\n" +
        "Ernesto Hernandez enha@cga.bar";

    /// <summary>Lee el pie configurado; lo asigna Program al arrancar.</summary>
    public static Func<string?>? LeerPie { get; set; }

    /// <summary>El pie configurado o, si está vacío o no se puede leer, el de omisión.</summary>
    public static string Pie
    {
        get
        {
            try
            {
                return LeerPie?.Invoke() is { Length: > 0 } pie && !string.IsNullOrWhiteSpace(pie) ? pie.Trim() : PieOmision;
            }
            catch
            {
                return PieOmision;
            }
        }
    }

    public static (string Html, string Texto) Construir(Contenido c)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        var html = new StringBuilder();
        // Fluida: ocupa el ancho de la ventana hasta el máximo (más ancha si lleva imagen).
        int ancho = c.AnchoMaximo;

        html.Append($"""
            <!DOCTYPE html>
            <html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(c.Titulo)}</title></head>
            <body style="margin:0;padding:0;background:#eef1f5;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:#1e293b;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#eef1f5;padding:32px 12px;">
            <tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:{ancho}px;width:100%;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #dde3ea;">
            <tr><td style="background:{ColorMarca};padding:22px 32px;">
                <span style="display:inline-block;width:10px;height:10px;border-radius:2px;background:{ColorAcento};margin-right:8px;"></span>
                <span style="color:#ffffff;font-size:17px;font-weight:700;letter-spacing:.3px;">CFDI Manager</span>
                <span style="color:#9fb3d9;font-size:12px;margin-left:8px;">Bóveda de comprobantes fiscales</span>
            </td></tr>
            <tr><td style="padding:32px 32px 8px;">
                <h1 style="margin:0 0 16px;font-size:21px;line-height:1.3;color:#0f172a;">{E(c.Titulo)}</h1>
            """);

        if (!string.IsNullOrWhiteSpace(c.Saludo))
            html.Append($"""<p style="margin:0 0 14px;font-size:15px;line-height:1.6;">{E(c.Saludo)}</p>""");

        foreach (string parrafo in c.Parrafos)
            html.Append($"""<p style="margin:0 0 14px;font-size:15px;line-height:1.6;color:#334155;">{E(parrafo)}</p>""");

        if (!string.IsNullOrWhiteSpace(c.ImagenCid))
            html.Append($"""<img src="cid:{E(c.ImagenCid)}" alt="Resumen" style="display:block;width:100%;max-width:{c.ImagenAnchoMaximo}px;height:auto;margin:6px 0 18px;border:1px solid #e2e8f0;border-radius:8px;">""");

        if (c.Datos.Count > 0)
        {
            html.Append("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:10px 0 18px;border:1px solid #e2e8f0;border-radius:8px;border-collapse:separate;background:#f8fafc;">""");

            foreach ((string etiqueta, string valor) in c.Datos)
            {
                string estiloValor = c.DatosResaltados.Contains(valor)
                    ? "font-family:Consolas,Menlo,monospace;font-size:16px;font-weight:700;color:#0f172a;letter-spacing:.5px;"
                    : "font-size:14px;font-weight:600;color:#0f172a;";

                html.Append($"""
                    <tr>
                    <td style="padding:10px 16px;font-size:13px;color:#64748b;width:38%;border-bottom:1px solid #e2e8f0;">{E(etiqueta)}</td>
                    <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;{estiloValor}">{E(valor)}</td>
                    </tr>
                    """);
            }

            html.Append("</table>");
        }

        if (!string.IsNullOrWhiteSpace(c.TextoBoton) && !string.IsNullOrWhiteSpace(c.UrlBoton))
        {
            html.Append($"""
                <table role="presentation" cellpadding="0" cellspacing="0" style="margin:8px 0 22px;"><tr>
                <td style="background:{ColorAcento};border-radius:8px;">
                <a href="{E(c.UrlBoton)}" style="display:inline-block;padding:12px 24px;color:#ffffff;font-size:15px;font-weight:600;text-decoration:none;">{E(c.TextoBoton)}</a>
                </td></tr></table>
                """);
        }

        if (!string.IsNullOrWhiteSpace(c.Nota))
            html.Append($"""<p style="margin:0 0 14px;padding:12px 14px;border-left:3px solid {ColorAcento};background:#f0f9fb;font-size:13px;line-height:1.5;color:#334155;">{E(c.Nota)}</p>""");

        string pie = string.Join("<br>", Pie.Split('\n').Select(l => E(l.TrimEnd('\r'))));

        html.Append($"""
            </td></tr>
            <tr><td style="padding:18px 32px 26px;border-top:1px solid #eef1f5;font-size:12px;line-height:1.5;color:#94a3b8;">
                {pie}
            </td></tr>
            </table>
            </td></tr></table>
            </body></html>
            """);

        // Versión en texto plano para clientes que no muestran HTML.
        var texto = new StringBuilder();
        texto.AppendLine(c.Titulo).AppendLine();

        if (!string.IsNullOrWhiteSpace(c.Saludo))
            texto.AppendLine(c.Saludo).AppendLine();

        foreach (string parrafo in c.Parrafos)
            texto.AppendLine(parrafo).AppendLine();

        foreach ((string etiqueta, string valor) in c.Datos)
            texto.AppendLine($"{etiqueta}: {valor}");

        if (!string.IsNullOrWhiteSpace(c.UrlBoton))
            texto.AppendLine().AppendLine($"{c.TextoBoton}: {c.UrlBoton}");

        if (!string.IsNullOrWhiteSpace(c.Nota))
            texto.AppendLine().AppendLine(c.Nota);

        texto.AppendLine().AppendLine("--").AppendLine(Pie);

        return (html.ToString(), texto.ToString());
    }
}
