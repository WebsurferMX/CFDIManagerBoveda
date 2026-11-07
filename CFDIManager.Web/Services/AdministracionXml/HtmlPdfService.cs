using System.Diagnostics;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Convierte HTML a PDF con Edge (o Chrome) instalado en el servidor, sin ventana (--headless --print-to-pdf).
/// Cada conversión usa su propia carpeta temporal y perfil, y se borran al terminar.
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

    public static string? Navegador => Navegadores.FirstOrDefault(File.Exists);

    public async Task<byte[]> ConvertirAsync(string html, CancellationToken ct = default)
    {
        string navegador = Navegador ?? throw new InvalidOperationException("No se encontró Edge ni Chrome en el servidor para generar el PDF.");
        string carpeta = Path.Combine(Path.GetTempPath(), "cfdi-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        string entrada = Path.Combine(carpeta, "cfdi.html");
        string salida = Path.Combine(carpeta, "cfdi.pdf");

        await Turnos.WaitAsync(ct);
        try
        {
            await File.WriteAllTextAsync(entrada, html, ct);

            var inicio = new ProcessStartInfo(navegador)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (string a in new[]
                     {
                         "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                         "--disable-sync", "--no-pdf-header-footer", "--print-to-pdf-no-header",
                         // Da tiempo a que carguen las imágenes (logo de SINUBE).
                         "--run-all-compositor-stages-before-draw", "--virtual-time-budget=8000",
                         $"--user-data-dir={Path.Combine(carpeta, "perfil")}",
                         $"--print-to-pdf={salida}",
                         new Uri(entrada).AbsoluteUri
                     })
                inicio.ArgumentList.Add(a);

            using Process proceso = Process.Start(inicio) ?? throw new InvalidOperationException("No se pudo iniciar el navegador para generar el PDF.");
            Task<string> errores = proceso.StandardError.ReadToEndAsync(ct);
            _ = proceso.StandardOutput.ReadToEndAsync(ct);

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await proceso.WaitForExitAsync(limite.Token);
            }
            catch (OperationCanceledException)
            {
                try { proceso.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException("El navegador tardó más de un minuto en generar el PDF.");
            }

            if (!File.Exists(salida) || new FileInfo(salida).Length == 0)
            {
                string detalle = await errores;
                log.LogWarning("El navegador no generó el PDF (salida {Codigo}): {Detalle}", proceso.ExitCode, detalle);
                throw new InvalidOperationException("El navegador no generó el PDF.");
            }

            return await File.ReadAllBytesAsync(salida, ct);
        }
        finally
        {
            Turnos.Release();
            try { Directory.Delete(carpeta, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
