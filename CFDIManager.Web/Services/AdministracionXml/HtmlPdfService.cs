using System.Diagnostics;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Convierte HTML a PDF con Edge o Chrome instalado en el servidor, sin ventana (--headless --print-to-pdf).
/// Como servicio (LocalSystem) el navegador puede necesitar parámetros distintos que en una sesión de usuario, así que se
/// prueban varias combinaciones y se recuerda la primera que funciona. Cada conversión usa su propia carpeta temporal y
/// perfil, y se borran al terminar.
/// </summary>
public class HtmlPdfService(ILogger<HtmlPdfService> log)
{
    private static readonly SemaphoreSlim Turnos = new(2);

    private static readonly string[] Navegadores =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe")
    ];

    // Parámetros extra por intento: sin sandbox (lo que suele pedir LocalSystem) y, al final, el modo headless anterior.
    private static readonly string[][] Variantes =
    [
        ["--headless=new", "--no-sandbox"],
        ["--headless=new", "--no-sandbox", "--disable-crash-reporter", "--disable-breakpad", "--no-zygote", "--disable-features=RendererCodeIntegrity"],
        ["--headless=new"],
        ["--headless", "--no-sandbox"]
    ];

    /// <summary>Intentos (navegador, variante): primero el que ya funcionó.</summary>
    private static (string Navegador, string[] Variante)? _funciona;

    public static string? Navegador => Navegadores.FirstOrDefault(File.Exists);

    private static IEnumerable<(string Navegador, string[] Variante)> Intentos()
    {
        if (_funciona is { } f)
            yield return f;

        foreach (string n in Navegadores.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (string[] v in Variantes)
        {
            if (_funciona is not { } ya || ya.Navegador != n || ya.Variante != v)
                yield return (n, v);
        }
    }

    public async Task<byte[]> ConvertirAsync(string html, CancellationToken ct = default)
    {
        if (Navegador is null)
            throw new InvalidOperationException("No se encontró Edge ni Chrome en el servidor para generar el PDF.");

        string carpeta = Path.Combine(Path.GetTempPath(), "cfdi-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        string entrada = Path.Combine(carpeta, "cfdi.html");

        await Turnos.WaitAsync(ct);
        try
        {
            await File.WriteAllTextAsync(entrada, html, ct);
            var fallas = new List<string>();
            int n = 0;

            foreach ((string navegador, string[] variante) in Intentos())
            {
                n++;
                string salida = Path.Combine(carpeta, $"cfdi{n}.pdf");
                (int codigo, string errores) = await EjecutarAsync(navegador, variante, Path.Combine(carpeta, $"perfil{n}"), entrada, salida, ct);

                if (File.Exists(salida) && new FileInfo(salida).Length > 0)
                {
                    if (_funciona is not { } ya || ya.Navegador != navegador || ya.Variante != variante)
                    {
                        _funciona = (navegador, variante);
                        log.LogInformation("PDF generado con {Navegador} {Parametros}", Path.GetFileName(navegador), string.Join(" ", variante));
                    }

                    return await File.ReadAllBytesAsync(salida, ct);
                }

                fallas.Add($"{Path.GetFileName(navegador)} {string.Join(" ", variante)} → salida {codigo}: {Recortar(errores)}");
            }

            _funciona = null;
            log.LogWarning("El navegador no generó el PDF. Intentos:\n{Intentos}", string.Join("\n", fallas));
            throw new InvalidOperationException("El navegador no generó el PDF.");
        }
        finally
        {
            Turnos.Release();
            try { Directory.Delete(carpeta, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string Recortar(string texto)
    {
        texto = texto.Trim();
        return texto.Length > 600 ? texto[..600] + "…" : texto;
    }

    private static async Task<(int Codigo, string Errores)> EjecutarAsync(
        string navegador, string[] variante, string perfil, string entrada, string salida, CancellationToken ct)
    {
        var inicio = new ProcessStartInfo(navegador)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (string a in variante.Concat(
                 [
                     "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-sync",
                     "--no-pdf-header-footer", "--print-to-pdf-no-header", "--enable-logging=stderr", "--v=0",
                     // Da tiempo a que carguen las imágenes (logo de SINUBE).
                     "--run-all-compositor-stages-before-draw", "--virtual-time-budget=8000",
                     $"--user-data-dir={perfil}",
                     $"--print-to-pdf={salida}",
                     new Uri(entrada).AbsoluteUri
                 ]))
            inicio.ArgumentList.Add(a);

        using Process proceso = Process.Start(inicio) ?? throw new InvalidOperationException("No se pudo iniciar el navegador para generar el PDF.");
        Task<string> errores = proceso.StandardError.ReadToEndAsync(ct);
        _ = proceso.StandardOutput.ReadToEndAsync(ct);

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            await proceso.WaitForExitAsync(limite.Token);
        }
        catch (OperationCanceledException)
        {
            try { proceso.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            ct.ThrowIfCancellationRequested();
            return (-1, "tardó más de 45 segundos");
        }

        string texto;
        try
        {
            texto = await errores.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        catch (TimeoutException)
        {
            texto = "";
        }

        return (proceso.ExitCode, texto);
    }
}
