using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace CFDIManager.Escritorio;

/// <summary>
/// Atiende los pedidos de wwwroot\js\escritorio.js (portapapeles, Localizar en carpeta, avisos). Solo acepta
/// mensajes de la página del servidor al que está conectada la app.
/// </summary>
public sealed class Puente
{
    private readonly CoreWebView2 _core;
    private readonly Configuracion _configuracion;
    private readonly Bandeja _bandeja;
    private readonly Func<Uri?> _servidor;

    public Puente(CoreWebView2 core, Configuracion configuracion, Bandeja bandeja, Func<Uri?> servidor)
    {
        _core = core;
        _configuracion = configuracion;
        _bandeja = bandeja;
        _servidor = servidor;
        _core.WebMessageReceived += Recibido;
    }

    private void Recibido(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_servidor() is not Uri servidor ||
            !Uri.TryCreate(e.Source, UriKind.Absolute, out Uri? origen) ||
            Uri.Compare(origen, servidor, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return;
        }

        int id = 0;

        try
        {
            using JsonDocument documento = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement m = documento.RootElement;
            id = m.GetProperty("id").GetInt32();

            object? resultado = m.GetProperty("tipo").GetString() switch
            {
                "copiarTexto" => CopiarTexto(Texto(m, "texto")),
                "copiarImagen" => CopiarImagen(Texto(m, "base64"), TextoOpcional(m, "html")),
                "guardarYMostrar" => GuardarYMostrar(Texto(m, "ruta"), Texto(m, "base64")),
                "notificar" => Notificar(Texto(m, "titulo"), Texto(m, "mensaje")),
                var tipo => throw new InvalidOperationException($"Pedido desconocido: {tipo}.")
            };

            Responder(id, true, resultado, null);
        }
        catch (Exception ex)
        {
            Responder(id, false, null, ex.Message);
        }
    }

    private void Responder(int id, bool ok, object? resultado, string? error) =>
        _core.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok, resultado, error }));

    private static string Texto(JsonElement m, string nombre) =>
        TextoOpcional(m, nombre) ?? throw new InvalidOperationException($"Falta {nombre}.");

    private static string? TextoOpcional(JsonElement m, string nombre) =>
        m.TryGetProperty(nombre, out JsonElement valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static bool CopiarTexto(string texto)
    {
        var datos = new DataObject();
        datos.SetText(texto, TextDataFormat.UnicodeText);
        return PonerEnPortapapeles(datos);
    }

    /// <summary>
    /// Con html (correo): solo el fragmento HTML con el &lt;img&gt;, igual que en el navegador, para que Outlook lo pegue
    /// al tamaño de la vista. Sin html (WhatsApp): el PNG y su mapa de bits.
    /// </summary>
    private static bool CopiarImagen(string base64, string? html)
    {
        var datos = new DataObject();

        if (html is not null)
        {
            datos.SetData(DataFormats.Html, FormatoHtml(html));
        }
        else
        {
            byte[] png = Convert.FromBase64String(base64);

            var imagen = new BitmapImage();
            imagen.BeginInit();
            imagen.CacheOption = BitmapCacheOption.OnLoad;
            imagen.StreamSource = new MemoryStream(png);
            imagen.EndInit();
            imagen.Freeze();

            datos.SetImage(imagen);
            datos.SetData("PNG", new MemoryStream(png));
        }

        return PonerEnPortapapeles(datos);
    }

    /// <summary>Formato CF_HTML de Windows: encabezado con las posiciones (en bytes UTF-8) del documento y del fragmento.</summary>
    private static string FormatoHtml(string fragmento)
    {
        const string Encabezado = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string Inicio = "<html><body><!--StartFragment-->";
        const string Fin = "<!--EndFragment--></body></html>";

        int inicioHtml = string.Format(Encabezado, 0, 0, 0, 0).Length;
        int inicioFragmento = inicioHtml + Encoding.UTF8.GetByteCount(Inicio);
        int finFragmento = inicioFragmento + Encoding.UTF8.GetByteCount(fragmento);
        int finHtml = finFragmento + Encoding.UTF8.GetByteCount(Fin);

        return string.Format(Encabezado, inicioHtml, finHtml, inicioFragmento, finFragmento) + Inicio + fragmento + Fin;
    }

    /// <summary>Otro programa puede tener abierto el portapapeles un instante: se reintenta.</summary>
    private static bool PonerEnPortapapeles(DataObject datos)
    {
        for (int intento = 1; ; intento++)
        {
            try
            {
                Clipboard.SetDataObject(datos, copy: true);
                return true;
            }
            catch (COMException) when (intento < 6)
            {
                Thread.Sleep(60);
            }
        }
    }

    /// <summary>Guarda el archivo en CarpetaLocal\XML\{ruta} (la misma estructura que en Storage) y lo selecciona.</summary>
    private string GuardarYMostrar(string ruta, string base64)
    {
        string raiz = Path.GetFullPath(Path.Combine(_configuracion.CarpetaLocal, "XML")) + Path.DirectorySeparatorChar;

        string[] partes = ruta.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => string.Concat(p.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)))
            .ToArray();

        if (partes.Length == 0 || partes.Any(p => p is "." or ".."))
            throw new InvalidOperationException("Ruta no válida.");

        string destino = Path.GetFullPath(Path.Combine([raiz, .. partes]));
        if (!destino.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ruta no válida.");

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllBytes(destino, Convert.FromBase64String(base64));

        MostrarEnExplorador(destino);
        return destino;
    }

    private bool Notificar(string titulo, string mensaje)
    {
        _bandeja.Avisar(titulo, mensaje);
        return true;
    }

    public static void MostrarEnExplorador(string archivo) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{archivo}\"") { UseShellExecute = false });
}
