using System.Text;
using System.Xml.Linq;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Sinube;

/// <summary>Resultado de bajar las adendas de un mes.</summary>
public sealed record ResultadoAdendasSinube(int ConAdenda, int Nuevas, int Errores);

/// <summary>
/// Copias con adenda de las facturas emitidas en SINUBE (consultas getpost de solo lectura):
/// 1. DbFactura del mes con tieneAddenda = true;
/// 2. por cada una, DbFacturaXml (misma serie y folio) → xmlConAddenda.
/// Se guardan en RFC\Emitidos\Adendas\AAAA\MM\UUID.xml; la copia del SAT no se toca y esa carpeta no se importa
/// a la bóveda (RutaXmlService.FueraDeBoveda). Un UUID que ya tiene su archivo no se vuelve a pedir.
/// </summary>
public class AdendasSinubeService(
    SinubeService sinube,
    SinubeDatosService datos,
    RutaXmlService rutas,
    ILogger<AdendasSinubeService> log)
{
    private static readonly XNamespace Tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";

    /// <summary>Si la empresa tiene capturado el acceso a SINUBE que piden las consultas.</summary>
    public bool TieneSinube(EmpresaDto empresa)
    {
        SinubeConfiguracionDto config = sinube.ObtenerConfiguracion(empresa);
        return SinubeService.FaltanDatos(config, requierePassword: true) is null && !string.IsNullOrWhiteSpace(config.Usuario);
    }

    public async Task<ResultadoAdendasSinube> DescargarMesAsync(EmpresaDto empresa, int anio, int mes, CancellationToken ct = default)
    {
        SinubeConfiguracionDto config = sinube.ObtenerConfiguracion(empresa);
        string e = Limpiar(config.Empresa);

        List<FilaSinube> facturas = await datos.ConsultarAsync(config,
            $"SELECT uuid, serieFactura, folioFactura, sucursal FROM DbFactura " +
            $"WHERE empresa = '{e}' AND mes = {anio * 100 + mes} AND tieneAddenda = true", ct);

        string carpeta = rutas.CarpetaAdendasPeriodo(empresa, anio, mes);
        int conAdenda = 0, nuevas = 0, errores = 0;

        foreach (FilaSinube f in facturas)
        {
            ct.ThrowIfCancellationRequested();

            // Sin UUID todavía no está timbrada (o es una prefactura).
            if (!Guid.TryParse(f.Texto("uuid")?.Trim(), out Guid guid))
                continue;

            conAdenda++;
            string uuid = guid.ToString().ToUpperInvariant();
            string archivo = Path.Combine(carpeta, uuid + ".xml");

            if (File.Exists(archivo))
                continue;

            string serie = Limpiar(f.Texto("serieFactura") ?? "");
            long? folio = f.Entero("folioFactura");
            string sucursal = Limpiar(f.Texto("sucursal") ?? "");

            if (folio is null)
            {
                errores++;
                log.LogWarning("Adenda SINUBE {Rfc} {Uuid}: la factura no tiene folio.", empresa.Rfc, uuid);
                continue;
            }

            try
            {
                List<FilaSinube> xmls = await datos.ConsultarAsync(config,
                    $"SELECT xmlConAddenda FROM DbFacturaXml WHERE empresa = '{e}'" +
                    (serie == "" ? "" : $" AND serieFactura = '{serie}'") +
                    $" AND folioFactura = {folio}" +
                    (sucursal == "" ? "" : $" AND sucursal = '{sucursal}'"), ct);

                // La serie y folio pueden repetirse (otra sucursal, folios repetidos): vale la del mismo UUID timbrado.
                string? xml = xmls
                    .Select(x => x.Texto("xmlConAddenda"))
                    .FirstOrDefault(x => x is { Length: > 0 } && UuidTimbre(x) == uuid);

                if (xml is null)
                {
                    errores++;
                    log.LogWarning("Adenda SINUBE {Rfc} {Serie}-{Folio} {Uuid}: SINUBE no tiene el XML con adenda.", empresa.Rfc, serie, folio, uuid);
                    continue;
                }

                Directory.CreateDirectory(carpeta);
                string temporal = archivo + ".tmp";
                await File.WriteAllTextAsync(temporal, xml, new UTF8Encoding(false), ct);
                File.Move(temporal, archivo, overwrite: true);
                nuevas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errores++;
                log.LogWarning(ex, "Adenda SINUBE {Rfc} {Serie}-{Folio} {Uuid}", empresa.Rfc, serie, folio, uuid);
            }
        }

        return new ResultadoAdendasSinube(conAdenda, nuevas, errores);
    }

    /// <summary>UUID del timbre (mayúsculas) o null si el texto no es un CFDI timbrado.</summary>
    private static string? UuidTimbre(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants(Tfd + "TimbreFiscalDigital").FirstOrDefault()?.Attribute("UUID")?.Value
                .Trim().ToUpperInvariant();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string Limpiar(string valor) => valor.Trim().Replace("'", "");
}

/// <summary>
/// Cada AdendasSinube:Horas (3 por omisión) baja las adendas de SINUBE de las empresas con acceso a SINUBE:
/// el mes actual y el anterior siempre; los meses más viejos con emitidos en la bóveda, una sola vez cada uno
/// (configuracion_app adendas_sinube:{empresa}:{AAAAMM}), hasta AdendasSinube:MesesPorPasada (12) por empresa.
/// </summary>
public class AdendasSinubeHostedService(
    IServiceScopeFactory scopes,
    IConfiguration configuracion,
    ILogger<AdendasSinubeHostedService> log) : BackgroundService
{
    private static string ClaveMes(long empresaId, DateTime mes) => $"adendas_sinube:{empresaId}:{mes:yyyyMM}";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Igual que la descarga del SAT: una segunda instancia que comparte la base la apaga.
        if (!configuracion.GetValue("AdendasSinube:Habilitada", true))
            return;

        TimeSpan intervalo = TimeSpan.FromHours(Math.Clamp(configuracion.GetValue("AdendasSinube:Horas", 3.0), 0.5, 24));
        int maximoMeses = Math.Max(1, configuracion.GetValue("AdendasSinube:MesesPorPasada", 12));

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), ct);

            while (!ct.IsCancellationRequested)
            {
                await PasadaAsync(maximoMeses, ct);
                await Task.Delay(intervalo, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // La app se está cerrando.
        }
    }

    private async Task PasadaAsync(int maximoMeses, CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();
            var sinube = scope.ServiceProvider.GetRequiredService<SinubeService>();
            var adendas = scope.ServiceProvider.GetRequiredService<AdendasSinubeService>();

            DateTime actual = new(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTime anterior = actual.AddMonths(-1);

            foreach (EmpresaDto empresa in repo.ObtenerEmpresasActivas())
            {
                ct.ThrowIfCancellationRequested();

                if (!adendas.TieneSinube(empresa))
                    continue;

                // Los dos meses recientes siempre (SINUBE puede timbrar después); los viejos, una vez.
                List<DateTime> meses = [actual, anterior];
                meses.AddRange(sinube.Periodos(empresa, clientes: true)
                    .Where(m => m < anterior && repo.ObtenerConfiguracionApp(ClaveMes(empresa.EmpresaId, m)) is null)
                    .Take(maximoMeses));

                foreach (DateTime mes in meses)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        ResultadoAdendasSinube r = await adendas.DescargarMesAsync(empresa, mes.Year, mes.Month, ct);

                        if (r.Nuevas > 0 || r.Errores > 0)
                            log.LogInformation("Adendas SINUBE {Rfc} {Mes:yyyy-MM}: {Nuevas} nuevas de {ConAdenda}, {Errores} con error.",
                                empresa.Rfc, mes, r.Nuevas, r.ConAdenda, r.Errores);

                        if (mes < anterior && r.Errores == 0)
                            repo.GuardarConfiguracionApp(ClaveMes(empresa.EmpresaId, mes), DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Acceso rechazado o SINUBE sin respuesta: se intenta de nuevo en la siguiente pasada.
                        log.LogWarning(ex, "No se pudieron bajar las adendas de SINUBE de {Rfc} {Mes:yyyy-MM}", empresa.Rfc, mes);
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Falló la descarga de adendas de SINUBE");
        }
    }
}
