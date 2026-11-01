using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Sat;

public sealed class ResultadoDescargaCiec
{
    public int Importados { get; set; }
    public int Actualizados { get; set; }
    public int Omitidos { get; set; }
    public int Errores { get; set; }
    public int SinXml { get; set; }

    public string Resumen =>
        $"{Importados + Actualizados + Omitidos + Errores} descargados: {Importados} nuevos, " +
        $"{Actualizados} ya existían (actualizados), {Omitidos} omitidos, {Errores} con error" +
        (SinXml > 0 ? $"; {SinXml} sin XML disponible en el portal" : "");
}

/// <summary>
/// Descarga con CIEC: la sesión del portal del SAT vive mientras la pestaña esté abierta
/// (servicio scoped del circuito). Los XML entran por la misma importación que la descarga
/// con e.firma, así que quedan en Storage\XML\RFC\Emitidos|Recibidos\AAAA\MM.
/// </summary>
public sealed class DescargaCiecService : IDisposable
{
    private readonly PortalSatCiec _portal;
    private readonly PostgreSqlRepository _repo;
    private readonly ImportacionCfdiService _importacion;
    private readonly CiecProtector _protector;
    private readonly IWebHostEnvironment _ambiente;

    public DescargaCiecService(
        PostgreSqlRepository repo,
        ImportacionCfdiService importacion,
        CiecProtector protector,
        IWebHostEnvironment ambiente)
    {
        _repo = repo;
        _importacion = importacion;
        _protector = protector;
        _ambiente = ambiente;
        _portal = new PortalSatCiec(CarpetaStorage.Ruta("Diagnostico", "SatCiec"));
    }

    /// <summary>Empresa con sesión abierta en el portal, o null.</summary>
    public long? EmpresaSesion { get; private set; }

    public bool SesionActiva(long empresaId) =>
        EmpresaSesion == empresaId && _portal.RfcSesion is not null;

    public Task<string> ObtenerCaptchaAsync(CancellationToken ct = default)
    {
        EmpresaSesion = null;
        return _portal.ObtenerCaptchaAsync(ct);
    }

    /// <summary>
    /// Inicia sesión con la CIEC guardada de la empresa, o con <paramref name="ciecCapturada"/>
    /// si se está probando una nueva antes de guardarla. Registra el resultado en la empresa.
    /// </summary>
    public async Task IniciarSesionAsync(EmpresaDto empresa, string? ciecCapturada, string captcha, CancellationToken ct = default)
    {
        string? ciec = string.IsNullOrEmpty(ciecCapturada)
            ? _protector.Descifrar(_repo.ObtenerEmpresaSat(empresa.EmpresaId)?.SatPassword)
            : ciecCapturada;

        if (string.IsNullOrEmpty(ciec))
            throw new InvalidOperationException("La empresa no tiene CIEC guardada (Empresas → Acceso SAT).");

        if (string.IsNullOrWhiteSpace(captcha))
            throw new InvalidOperationException("Escribe el texto del captcha.");

        try
        {
            await _portal.IniciarSesionAsync(empresa.Rfc, ciec, captcha, ct);
            EmpresaSesion = empresa.EmpresaId;
            _repo.RegistrarPruebaCiec(empresa.EmpresaId, "Correcta");
        }
        catch (InvalidOperationException ex)
        {
            EmpresaSesion = null;

            // Un captcha mal escrito no dice nada de la contraseña: solo se registra lo demás.
            if (!ex.Message.Contains("captcha", StringComparison.OrdinalIgnoreCase))
                _repo.RegistrarPruebaCiec(empresa.EmpresaId, ex.Message);

            throw;
        }
    }

    /// <summary>CFDI del periodo, día por día, marcando los que ya están en CFDI Manager.</summary>
    public async Task<List<CfdiPortalSat>> BuscarAsync(
        EmpresaDto empresa, bool recibidos, DateTime desde, DateTime hasta, string estado, string rfcContraparte,
        IProgress<string>? progreso, Action<string>? aviso, CancellationToken ct = default)
    {
        ValidarSesion(empresa);

        if (hasta < desde)
            throw new InvalidOperationException("La fecha final debe ser igual o posterior a la inicial.");

        var resultado = new List<CfdiPortalSat>();
        int dias = (hasta.Date - desde.Date).Days + 1;

        for (int i = 0; i < dias; i++)
        {
            DateTime dia = desde.Date.AddDays(i);
            progreso?.Report($"Consultando {dia:dd/MM/yyyy} ({i + 1} de {dias})… {resultado.Count} CFDI encontrados");

            resultado.AddRange(await _portal.ConsultarDiaAsync(recibidos, dia, estado, rfcContraparte.Trim().ToUpperInvariant(), aviso, ct));
        }

        foreach (CfdiPortalSat cfdi in resultado)
            cfdi.YaImportado = _repo.ExisteCfdi(empresa.EmpresaId, cfdi.Uuid);

        return resultado;
    }

    /// <summary>Descarga e importa los XML elegidos.</summary>
    public async Task<ResultadoDescargaCiec> DescargarAsync(
        EmpresaDto empresa, IReadOnlyList<CfdiPortalSat> cfdis, IProgress<string>? progreso, CancellationToken ct = default)
    {
        ValidarSesion(empresa);

        var resultado = new ResultadoDescargaCiec();
        string carpetaTemporal = CarpetaStorage.Ruta("Temporal", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(carpetaTemporal);

        try
        {
            for (int i = 0; i < cfdis.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                CfdiPortalSat cfdi = cfdis[i];
                progreso?.Report($"Descargando XML {i + 1} de {cfdis.Count}…");

                if (cfdi.UrlXml == "")
                {
                    resultado.SinXml++;
                    continue;
                }

                string xml;

                try
                {
                    xml = await _portal.DescargarXmlAsync(cfdi, ct);
                }
                catch (SesionSatExpiradaException)
                {
                    EmpresaSesion = null;
                    throw;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    resultado.Errores++;
                    continue;
                }

                string ruta = Path.Combine(carpetaTemporal, $"{cfdi.Uuid}.xml");
                await File.WriteAllTextAsync(ruta, xml, ct);

                ResultadoImportacionCfdi r = _importacion.Importar(empresa, ruta, $"{cfdi.Uuid}.xml");

                switch (r.Estatus)
                {
                    case "CORRECTO": resultado.Importados++; break;
                    case "ACTUALIZADO": resultado.Actualizados++; break;
                    case "OMITIDO": resultado.Omitidos++; break;
                    default: resultado.Errores++; break;
                }

                cfdi.YaImportado = r.Estatus is "CORRECTO" or "ACTUALIZADO";
                File.Delete(ruta);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(carpetaTemporal, recursive: true);
            }
            catch
            {
                // La limpieza temporal no debe ocultar el resultado.
            }
        }

        return resultado;
    }

    private void ValidarSesion(EmpresaDto empresa)
    {
        if (!SesionActiva(empresa.EmpresaId))
            throw new InvalidOperationException("Inicia sesión en el SAT con la CIEC de la empresa activa.");
    }

    public void Dispose() => _portal.Dispose();
}
