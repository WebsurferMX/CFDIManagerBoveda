using CFDIManager.Web.Models;
using CFDIManager.Web.Services.Interfaces;
using CFDIManager.Web.Services.Sat;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Sinube;

/// <summary>
/// Lado SAT de la conciliación: metadatos de la descarga masiva por mes y dirección. Se piden una sola vez
/// (o se reutiliza una solicitud existente), se guardan en sat_metadatos y conciliar ya no consulta al SAT.
/// </summary>
public class SatMetadatosService(ISatDescargaService descargas, PostgreSqlRepository repo, ILogger<SatMetadatosService> log)
{
    private static readonly Dictionary<string, string> Efectos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ingreso"] = "I", ["Egreso"] = "E", ["Traslado"] = "T", ["Nómina"] = "N", ["Pago"] = "P"
    };

    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(30);

    public static int Periodo(int anio, int mes) => anio * 100 + mes;

    /// <summary>
    /// Metadatos guardados del mes. Si no hay, o hay una solicitud descargada más reciente que los cubre
    /// (p. ej. pedida desde Descargas SAT), se cargan primero.
    /// </summary>
    public async Task<(SatMetadatosPeriodoDto? Periodo, List<SatMetadatoDto> Metadatos)> ObtenerAsync(
        EmpresaDto empresa, bool recibidos, int anio, int mes)
    {
        int periodo = Periodo(anio, mes);
        SatMetadatosPeriodoDto? actual = repo.ObtenerMetadatosPeriodo(empresa.EmpresaId, recibidos, periodo);

        SolicitudSatDto? descargada = (await SolicitudesDelMes(empresa, recibidos, anio, mes))
            .Where(s => s.Estado == "Descargada")
            .MaxBy(s => s.Actualizada);

        if (descargada is not null && (actual is null || descargada.Actualizada > actual.Consultado && descargada.Id != actual.SolicitudId))
            actual = Guardar(empresa, recibidos, anio, mes, descargada);

        if (actual is null)
            return (null, []);

        var desde = new DateTime(anio, mes, 1);
        return (actual, repo.ObtenerMetadatos(empresa.EmpresaId, recibidos, desde, desde.AddMonths(1)));
    }

    /// <summary>Solicitud de metadatos del mes que el SAT todavía está preparando, si la hay.</summary>
    public async Task<SolicitudSatDto?> ObtenerPendienteAsync(EmpresaDto empresa, bool recibidos, int anio, int mes) =>
        (await SolicitudesDelMes(empresa, recibidos, anio, mes))
            .Where(s => s.Estado is "Aceptada" or "En proceso" or "Terminada")
            .MaxBy(s => s.Creada);

    /// <summary>
    /// Pide (o retoma) los metadatos del mes al SAT, espera a que estén listos, los descarga y los guarda.
    /// El SAT suele tardar de 1 a 10 minutos; se verifica cada 15 s.
    /// </summary>
    public async Task<SatMetadatosPeriodoDto> ActualizarAsync(
        EmpresaDto empresa, bool recibidos, int anio, int mes, IProgress<string> progreso, CancellationToken cancelacion,
        string origen = "")
    {
        SolicitudSatDto? solicitud = await ObtenerPendienteAsync(empresa, recibidos, anio, mes);

        if (solicitud is null)
        {
            var desde = new DateTime(anio, mes, 1);
            // El mes en curso se pide hasta hace unos minutos (el SAT rechaza fechas futuras).
            DateTime hasta = Min(desde.AddMonths(1).AddSeconds(-1), DateTime.Now.AddMinutes(-5));

            if (hasta <= desde)
                throw new InvalidOperationException("El periodo todavía no tiene CFDI que consultar en el SAT.");

            progreso.Report("Enviando la solicitud de metadatos al SAT…");

            solicitud = await descargas.CrearSolicitudAsync(empresa, new ParametrosSolicitudSat
            {
                FechaInicial = desde,
                FechaFinal = hasta,
                Recibidos = recibidos,
                Metadatos = true
            }, cancelacion, origen: origen);

            if (solicitud.Estado == "Rechazada")
                throw new InvalidOperationException($"El SAT rechazó la solicitud: {solicitud.Mensaje}");
        }

        solicitud = await EsperarAsync(empresa, solicitud, "los metadatos", progreso, cancelacion);

        progreso.Report("Descargando los metadatos del SAT…");
        solicitud = await descargas.DescargarAsync(empresa, solicitud.Id, cancelacion: cancelacion);

        return Guardar(empresa, recibidos, anio, mes, solicitud);
    }

    // Con pocos faltantes conviene una solicitud por UUID (paquetes chicos); con más, una sola del mes.
    private const int MaximoPorUuid = 10;

    public sealed record ResultadoXmlFaltantes(int Importados, List<string> Rechazados);

    /// <summary>
    /// Descarga del SAT los XML que faltan en la bóveda y los importa. Con pocos UUID se pide cada uno; con
    /// más, una sola solicitud de XML del mes de la que solo se importan los faltantes (el resto no se lee).
    /// La solicitud por periodo de recibidos solo trae vigentes (regla del SAT), así que los recibidos
    /// cancelados se piden por UUID; si el SAT rechaza alguno se informa y se sigue con los demás.
    /// Comprobado el 31/10/2026: el SAT rechaza los recibidos cancelados con "5012 No se permite la descarga
    /// de xml que se encuentren cancelados"; por eso la pantalla ya no los ofrece en proveedores.
    /// </summary>
    public async Task<ResultadoXmlFaltantes> DescargarXmlFaltantesAsync(
        EmpresaDto empresa, bool recibidos, int anio, int mes,
        IReadOnlyCollection<string> vigentes, IReadOnlyCollection<string> cancelados,
        IProgress<string> progreso, CancellationToken cancelacion, string origen = "")
    {
        static HashSet<string> Normalizar(IEnumerable<string> u) => u.Select(x => x.Trim().ToUpperInvariant()).ToHashSet();

        HashSet<string> faltantes = Normalizar(vigentes.Concat(cancelados));
        HashSet<string> porUuid = recibidos ? Normalizar(cancelados) : [];
        HashSet<string> delMes = faltantes.Except(porUuid).ToHashSet();

        if (delMes.Count <= MaximoPorUuid)
        {
            porUuid.UnionWith(delMes);
            delMes.Clear();
        }

        var rechazados = new List<string>();
        var solicitudes = new List<SolicitudSatDto>();
        int n = 0;

        foreach (string uuid in porUuid)
        {
            progreso.Report($"Solicitando al SAT el XML {++n} de {porUuid.Count}…");
            SolicitudSatDto s = await descargas.CrearSolicitudPorUuidAsync(empresa, uuid, cancelacion);

            if (s.Estado == "Rechazada")
                rechazados.Add($"{uuid}: {s.Mensaje}");
            else
                solicitudes.Add(s);
        }

        if (delMes.Count > 0)
        {
            var desde = new DateTime(anio, mes, 1);
            DateTime hasta = Min(desde.AddMonths(1).AddSeconds(-1), DateTime.Now.AddMinutes(-5));

            // Se retoma una solicitud de XML del mes que siga en proceso, si la hay.
            SolicitudSatDto? pendiente = (await descargas.ListarSolicitudesAsync(empresa.EmpresaId))
                .Where(s => !s.Metadatos && s.Recibidos == recibidos && s.Uuid == "" && s.TipoComprobante == "" &&
                            s.RfcContraparte == "" && s.FechaInicial <= desde && s.FechaFinal >= Min(hasta, s.Creada.AddMinutes(-10)) &&
                            s.Estado is "Aceptada" or "En proceso" or "Terminada")
                .MaxBy(s => s.Creada);

            if (pendiente is null)
            {
                progreso.Report("Enviando al SAT la solicitud de XML del mes…");
                pendiente = await descargas.CrearSolicitudAsync(empresa, new ParametrosSolicitudSat
                {
                    FechaInicial = desde,
                    FechaFinal = hasta,
                    Recibidos = recibidos,
                    Metadatos = false
                }, cancelacion, origen: origen);

                if (pendiente.Estado == "Rechazada")
                    throw new InvalidOperationException($"El SAT rechazó la solicitud: {pendiente.Mensaje}");
            }

            solicitudes.Add(pendiente);
        }

        int importados = 0;

        foreach (SolicitudSatDto s in solicitudes)
        {
            SolicitudSatDto lista;

            try
            {
                lista = await EsperarAsync(empresa, s, "los XML", progreso, cancelacion);
            }
            catch (InvalidOperationException ex) when (s.Uuid != "")
            {
                // Una solicitud por UUID que el SAT no atiende no detiene a las demás.
                rechazados.Add($"{s.Uuid}: {ex.Message}");
                continue;
            }

            var avance = new Progress<string>(texto => progreso.Report(texto));
            SolicitudSatDto descargada = await descargas.DescargarAsync(empresa, lista.Id, avance, cancelacion, faltantes);
            importados += descargada.Importados;
        }

        log.LogInformation("XML faltantes {Rfc} {Periodo}: {Importados} de {Faltantes} importados, {Rechazados} rechazados.",
            empresa.Rfc, Periodo(anio, mes), importados, faltantes.Count, rechazados.Count);

        return new ResultadoXmlFaltantes(importados, rechazados);
    }

    /// <summary>Verifica cada 15 s hasta que el SAT termina la solicitud (máximo 30 min).</summary>
    private async Task<SolicitudSatDto> EsperarAsync(
        EmpresaDto empresa, SolicitudSatDto solicitud, string que, IProgress<string> progreso, CancellationToken cancelacion)
    {
        DateTime inicio = DateTime.Now;

        while (solicitud.Estado != "Terminada")
        {
            if (DateTime.Now - inicio > EsperaMaxima)
                throw new InvalidOperationException(
                    "El SAT no ha terminado la solicitud. Sigue en Descargas SAT; vuelve a intentarlo más tarde y se retomará.");

            progreso.Report($"El SAT está preparando {que} ({(DateTime.Now - inicio):mm\\:ss}). Puedes seguir trabajando; no cierres esta pantalla.");
            await Task.Delay(TimeSpan.FromSeconds(15), cancelacion);

            solicitud = await descargas.VerificarAsync(empresa, solicitud.Id, cancelacion);

            if (solicitud.Estado is "Error" or "Rechazada" or "Vencida" or "Cancelada")
                throw new InvalidOperationException($"La solicitud quedó {solicitud.Estado.ToLowerInvariant()}: {solicitud.Mensaje}");
        }

        return solicitud;
    }

    private SatMetadatosPeriodoDto Guardar(EmpresaDto empresa, bool recibidos, int anio, int mes, SolicitudSatDto solicitud)
    {
        List<SatMetadatoDto> metadatos = descargas
            .LeerMetadatos(empresa, solicitud.Id, marcarImportados: false)
            .Select(m => new SatMetadatoDto
            {
                Uuid = m.Uuid,
                RfcEmisor = m.RfcEmisor,
                NombreEmisor = m.NombreEmisor,
                RfcReceptor = m.RfcReceptor,
                NombreReceptor = m.NombreReceptor,
                FechaEmision = m.Fecha,
                Monto = m.Monto,
                Efecto = Efectos.GetValueOrDefault(m.Tipo, m.Tipo),
                Estatus = m.Estatus,
                FechaCancelacion = m.FechaCancelacion
            })
            .ToList();

        int periodo = Periodo(anio, mes);
        repo.GuardarMetadatosPeriodo(empresa.EmpresaId, recibidos, periodo, solicitud.Id, solicitud.FechaFinal, metadatos);
        log.LogInformation("Metadatos SAT {Rfc} {Periodo} {Direccion}: {Registros} CFDI.",
            empresa.Rfc, periodo, recibidos ? "recibidos" : "emitidos", metadatos.Count);

        return repo.ObtenerMetadatosPeriodo(empresa.EmpresaId, recibidos, periodo)!;
    }

    // Solicitudes de metadatos (sin filtros de tipo ni contraparte) que cubren el mes completo
    // o, en el mes en curso, hasta el momento en que se pidieron.
    private async Task<List<SolicitudSatDto>> SolicitudesDelMes(EmpresaDto empresa, bool recibidos, int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        DateTime finMes = desde.AddMonths(1).AddSeconds(-1);

        return (await descargas.ListarSolicitudesAsync(empresa.EmpresaId))
            .Where(s => s.Metadatos && s.Recibidos == recibidos && s.Uuid == "" &&
                        s.TipoComprobante == "" && s.RfcContraparte == "" &&
                        s.FechaInicial <= desde && s.FechaFinal >= Min(finMes, s.Creada.AddMinutes(-10)))
            .ToList();
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
