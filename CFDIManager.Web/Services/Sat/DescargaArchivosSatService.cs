using CFDIManager.Web.Services.Cif;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Sat;

public sealed class ResultadoDescargaDeclaraciones
{
    public int Encontradas { get; set; }
    public int Nuevas { get; set; }
    public int YaGuardadas { get; set; }
    public int SinPdf { get; set; }
    public List<string> Errores { get; } = [];

    public string Resumen =>
        Encontradas == 0
            ? "El SAT no tiene declaraciones presentadas para ese ejercicio."
            : $"{Encontradas} en el SAT: {Nuevas} nueva(s), {YaGuardadas} ya guardada(s)" +
              (SinPdf > 0 ? $", {SinPdf} sin acuse disponible" : "") +
              (Errores.Count > 0 ? $", {Errores.Count} con error" : "") + ".";
}

/// <summary>
/// Descarga del SAT con la CIEC guardada de la empresa: opinión de cumplimiento y constancia
/// (SAT Móvil, sin captcha) y acuses de declaraciones (portal del SAT, con captcha por sesión).
/// </summary>
public sealed class DescargaArchivosSatService : IDisposable
{
    private readonly PostgreSqlRepository _repo;
    private readonly CiecProtector _protector;
    private readonly SatMovilCliente _satMovil;
    private readonly ArchivosSatService _archivos;
    private readonly ConstanciaFiscalService _constancias;
    private readonly PortalSatDeclaraciones _portal;

    public DescargaArchivosSatService(
        PostgreSqlRepository repo,
        CiecProtector protector,
        SatMovilCliente satMovil,
        ArchivosSatService archivos,
        ConstanciaFiscalService constancias,
        IWebHostEnvironment ambiente)
    {
        _repo = repo;
        _protector = protector;
        _satMovil = satMovil;
        _archivos = archivos;
        _constancias = constancias;
        _portal = new PortalSatDeclaraciones(CarpetaStorage.Ruta("Diagnostico", "SatDeclaraciones"));
    }

    private long? _empresaSesion;

    private string Ciec(long empresaId) =>
        _protector.Descifrar(_repo.ObtenerEmpresaSat(empresaId)?.SatPassword) is { Length: > 0 } ciec
            ? ciec
            : throw new InvalidOperationException("La empresa no tiene CIEC guardada (pestaña Accesos SAT).");

    // ---------- SAT Móvil ----------

    public async Task<OpinionCumplimientoDto> DescargarOpinionAsync(long empresaId, string rfc, CancellationToken ct = default)
    {
        byte[] pdf = await _satMovil.DescargarAsync(rfc, Ciec(empresaId), SatMovilCliente.Opinion, ct);
        return _archivos.CargarOpinion(empresaId, rfc, "SAT", pdf, $"{rfc}-opinion-cumplimiento-{DateTime.Today:yyyyMMdd}.pdf");
    }

    public async Task<ConstanciaFiscalDto> DescargarConstanciaAsync(long empresaId, string rfc, CancellationToken ct = default)
    {
        byte[] pdf = await _satMovil.DescargarAsync(rfc, Ciec(empresaId), SatMovilCliente.Constancia, ct);
        return _constancias.Cargar(empresaId, rfc, pdf, $"{rfc}-constancia-{DateTime.Today:yyyyMMdd}.pdf");
    }

    // ---------- Portal de declaraciones ----------

    /// <summary>MENSUAL o ANUAL si la empresa tiene sesión abierta en ese portal.</summary>
    public bool SesionActiva(long empresaId, string portal) =>
        _empresaSesion == empresaId && _portal.PortalSesion == portal;

    public Task<string> ObtenerCaptchaAsync(string portal, CancellationToken ct = default)
    {
        _empresaSesion = null;
        return _portal.ObtenerCaptchaAsync(portal, ct);
    }

    public async Task IniciarSesionAsync(long empresaId, string rfc, string captcha, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(captcha))
            throw new InvalidOperationException("Escribe el texto del captcha.");

        await _portal.IniciarSesionAsync(rfc, Ciec(empresaId), captcha, ct);
        _empresaSesion = empresaId;
    }

    /// <summary>Baja los acuses del ejercicio que todavía no estén guardados.</summary>
    public async Task<ResultadoDescargaDeclaraciones> DescargarDeclaracionesAsync(
        long empresaId, string rfc, int ejercicio, IProgress<string>? progreso, CancellationToken ct = default)
    {
        if (_empresaSesion != empresaId)
            throw new SesionSatExpiradaException();

        var resultado = new ResultadoDescargaDeclaraciones();

        progreso?.Report($"Consultando declaraciones {ejercicio} en el SAT…");
        List<DeclaracionPortalSat> lista = await _portal.ConsultarAsync(ejercicio, ct);

        // Una misma declaración puede venir repetida (reimpresión y pagada).
        lista = lista.GroupBy(d => d.NumeroOperacion).Select(g => g.First()).ToList();
        resultado.Encontradas = lista.Count;

        int n = 0;

        foreach (DeclaracionPortalSat d in lista)
        {
            ct.ThrowIfCancellationRequested();
            n++;

            if (_archivos.TieneOperacion(empresaId, d.NumeroOperacion))
            {
                resultado.YaGuardadas++;
                continue;
            }

            progreso?.Report($"Bajando acuse {n} de {lista.Count} (operación {d.NumeroOperacion})…");

            try
            {
                byte[]? pdf = await _portal.DescargarAcuseAsync(d, ct);

                if (pdf is null)
                {
                    resultado.SinPdf++;
                    continue;
                }

                _archivos.GuardarDescargada(empresaId, rfc, d.Tipo, d.Ejercicio, d.Periodo, pdf, d.NumeroOperacion, d.TipoDeclaracion, d.FechaPresentacion);
                resultado.Nuevas++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException)
            {
                resultado.Errores.Add($"Operación {d.NumeroOperacion}: {ex.Message}");
            }
        }

        return resultado;
    }

    public void Dispose() => _portal.Dispose();
}
