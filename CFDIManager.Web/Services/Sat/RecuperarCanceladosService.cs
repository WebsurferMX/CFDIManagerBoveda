using System.IO.Compression;
using ClosedXML.Excel;
using CFDIParser.Models;
using CFDIParser.Services;
using Microsoft.AspNetCore.Components.Forms;

namespace CFDIManager.Web.Services.Sat;

/// <summary>Resultado de un XML cargado en Recuperar cancelados.</summary>
public sealed record RecuperacionArchivo(string Archivo, string Uuid, string Estatus, string Detalle)
{
    public bool Recuperado => Estatus == RecuperarCanceladosService.Recuperado;
}

/// <summary>
/// Recibidos cancelados que el SAT ya no entrega (5012): la lista sale de los metadatos y los XML se cargan de otra
/// fuente (SINUBE, el proveedor). Solo se aceptan XML cuyo UUID, emisor, receptor y total coinciden con el SAT.
/// </summary>
public class RecuperarCanceladosService(PostgreSqlRepository repo, XmlCfdiParser parser, ImportacionCfdiService importacion)
{
    public const string Recuperado = "Recuperado";
    public const string YaEstaba = "Ya estaba";
    public const string NoEnLista = "No está en la lista";
    public const string NoCoincide = "No coincide con el SAT";
    public const string ConError = "Error";

    private const long MaximoXml = 25L * 1024 * 1024;
    private const long MaximoZip = 200L * 1024 * 1024;

    public List<CanceladoSatDto> Listar(long empresaId, int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        return repo.ObtenerRecibidosCancelados(empresaId, desde, desde.AddMonths(1));
    }

    /// <summary>¿Hay metadatos de recibidos descargados para el mes? Sin ellos no se sabe qué falta.</summary>
    public SatMetadatosPeriodoDto? Metadatos(long empresaId, int anio, int mes) =>
        repo.ObtenerMetadatosPeriodo(empresaId, recibido: true, anio * 100 + mes);

    /// <summary>Carga XML sueltos o en ZIP; importa a la bóveda los que el SAT tiene como recibidos cancelados.</summary>
    public async Task<List<RecuperacionArchivo>> CargarAsync(
        EmpresaDto empresa, IReadOnlyList<IBrowserFile> archivos, CancellationToken ct = default)
    {
        Dictionary<string, CanceladoSatDto> cancelados = repo.ObtenerRecibidosCancelados(empresa.EmpresaId)
            .ToDictionary(c => c.Uuid, StringComparer.OrdinalIgnoreCase);

        string temporal = CarpetaStorage.Ruta("Temporal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporal);
        var resultados = new List<RecuperacionArchivo>();

        try
        {
            foreach (IBrowserFile archivo in archivos)
            {
                ct.ThrowIfCancellationRequested();
                bool esZip = archivo.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                string ruta = Path.Combine(temporal, Guid.NewGuid().ToString("N") + (esZip ? ".zip" : ".xml"));

                try
                {
                    await using (FileStream destino = File.Create(ruta))
                        await archivo.OpenReadStream(esZip ? MaximoZip : MaximoXml, ct).CopyToAsync(destino, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    resultados.Add(new RecuperacionArchivo(archivo.Name, "", ConError, ex.Message));
                    continue;
                }

                if (!esZip)
                {
                    resultados.Add(Procesar(empresa, ruta, archivo.Name, cancelados));
                    continue;
                }

                using ZipArchive zip = ZipFile.OpenRead(ruta);

                foreach (ZipArchiveEntry entrada in zip.Entries.Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                {
                    ct.ThrowIfCancellationRequested();
                    string rutaXml = Path.Combine(temporal, Guid.NewGuid().ToString("N") + ".xml");
                    entrada.ExtractToFile(rutaXml);
                    resultados.Add(Procesar(empresa, rutaXml, $"{archivo.Name} › {entrada.FullName}", cancelados));
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(temporal, recursive: true);
            }
            catch
            {
                // La limpieza temporal no debe ocultar el resultado.
            }
        }

        if (resultados.Any(r => r.Recuperado))
            repo.AplicarEstadoSatDeMetadatos(empresa.EmpresaId);

        return resultados;
    }

    private RecuperacionArchivo Procesar(
        EmpresaDto empresa, string ruta, string nombre, Dictionary<string, CanceladoSatDto> cancelados)
    {
        CfdiDto cfdi;

        try
        {
            cfdi = parser.Parse(ruta);
        }
        catch (Exception ex)
        {
            return new RecuperacionArchivo(nombre, "", ConError, "No es un CFDI válido: " + ex.Message);
        }

        string uuid = (cfdi.UUID ?? "").Trim().ToUpperInvariant();

        if (!cancelados.TryGetValue(uuid, out CanceladoSatDto? sat))
            return new RecuperacionArchivo(nombre, uuid, NoEnLista, "El SAT no lo tiene como recibido cancelado de esta empresa.");

        if (sat.EnBoveda)
            return new RecuperacionArchivo(nombre, uuid, YaEstaba, "El XML ya estaba en la bóveda.");

        // El XML viene de fuera del SAT: debe coincidir con lo que el SAT reporta de ese UUID.
        var diferencias = new List<string>();

        if (!Igual(cfdi.RFCEmisor, sat.RfcEmisor))
            diferencias.Add($"emisor {cfdi.RFCEmisor} (SAT {sat.RfcEmisor})");

        if (!Igual(cfdi.RFCReceptor, sat.RfcReceptor))
            diferencias.Add($"receptor {cfdi.RFCReceptor} (SAT {sat.RfcReceptor})");

        if (Math.Abs(cfdi.Total - sat.Monto) > 0.01m)
            diferencias.Add($"total {cfdi.Total:N2} (SAT {sat.Monto:N2})");

        if (diferencias.Count > 0)
            return new RecuperacionArchivo(nombre, uuid, NoCoincide, string.Join(", ", diferencias));

        ResultadoImportacionCfdi r = importacion.Importar(empresa, ruta, Path.GetFileName(nombre));

        if (r.Estatus is not ("CORRECTO" or "ACTUALIZADO"))
            return new RecuperacionArchivo(nombre, uuid, ConError, r.Detalle);

        sat.EnBoveda = true;
        return new RecuperacionArchivo(nombre, uuid, Recuperado, $"{sat.RfcEmisor} · {sat.Monto:N2}");
    }

    private static bool Igual(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Lista del mes para pedir los XML a SINUBE o a los proveedores.</summary>
    public byte[] GenerarExcel(EmpresaDto empresa, int anio, int mes, IEnumerable<CanceladoSatDto> filas)
    {
        using var libro = new XLWorkbook();
        IXLWorksheet hoja = libro.AddWorksheet("Cancelados");

        hoja.Cell(1, 1).Value = $"Recibidos cancelados · {empresa.Rfc} · {empresa.NombreParaMostrar} · Periodo {anio}{mes:00}";
        hoja.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        hoja.Cell(2, 1).Value = $"Generado {DateTime.Now:dd/MM/yyyy HH:mm}";

        string[] encabezados =
            ["RFC emisor", "Emisor", "Fecha emisión", "Monto", "Tipo", "Fecha cancelación", "XML en bóveda", "UUID"];

        for (int i = 0; i < encabezados.Length; i++)
            hoja.Cell(4, i + 1).Value = encabezados[i];

        int fila = 5;
        foreach (CanceladoSatDto c in filas)
        {
            object?[] valores =
                [c.RfcEmisor, c.NombreEmisor, c.FechaEmision, c.Monto, c.Efecto, c.FechaCancelacion, c.EnBoveda ? "Sí" : "No", c.Uuid];

            for (int i = 0; i < valores.Length; i++)
                hoja.Cell(fila, i + 1).Value = XLCellValue.FromObject(valores[i]);

            fila++;
        }

        IXLRange tabla = hoja.Range(4, 1, Math.Max(fila - 1, 5), encabezados.Length);
        tabla.CreateTable().Theme = XLTableTheme.TableStyleMedium2;
        hoja.Column(3).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        hoja.Column(4).Style.NumberFormat.Format = "#,##0.00";
        hoja.Column(6).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        hoja.Columns().AdjustToContents(4, fila, 8, 60);

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return memoria.ToArray();
    }
}
