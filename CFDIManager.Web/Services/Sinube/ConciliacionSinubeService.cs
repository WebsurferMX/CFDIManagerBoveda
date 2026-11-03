using ClosedXML.Excel;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Sinube;

public enum TipoConciliacion
{
    /// <summary>CFDI recibidos vs DbFacturaProveedorDeXML (XMLs de proveedores).</summary>
    Proveedores,

    /// <summary>CFDI emitidos vs DbFactura (facturas y notas de crédito de clientes; sin nómina).</summary>
    Clientes
}

/// <summary>Un comprobante en la conciliación SAT ↔ bóveda ↔ SINUBE.</summary>
public sealed class ConciliacionFilaDto
{
    public string Uuid { get; init; } = "";
    public string RazonSocial { get; init; } = "";
    public string Rfc { get; init; } = "";
    public DateTime? Fecha { get; init; }
    public decimal Total { get; init; }
    public string Tipo { get; init; } = "";
    public string Serie { get; init; } = "";
    public string Folio { get; init; } = "";
    public string Pac { get; init; } = "";

    /// <summary>Está en los metadatos del SAT (o, sin metadatos del mes, en la bóveda).</summary>
    public bool ExisteSat { get; init; }

    /// <summary>El XML está en la bóveda local.</summary>
    public bool ExisteBoveda { get; init; }

    public bool ExisteSinube { get; init; }

    /// <summary>Estatus oficial: metadatos del SAT; sin ellos, el validado en la bóveda.</summary>
    public string EstatusSat { get; init; } = "";

    /// <summary>Estatus que tiene SINUBE (statusValidadoSAT o, en clientes, canceladaPAC).</summary>
    public string EstatusSinube { get; init; } = "";

    public bool Poliza { get; init; }
    public string PolizaTexto { get; init; } = "";
    public long? MesPoliza { get; init; }
    public bool MesDistinto { get; init; }
    public decimal? TotalSinube { get; init; }

    public List<string> Diferencias { get; init; } = [];

    public bool Conciliada => Diferencias.Count == 0;
}

public sealed record ResultadoConciliacion(
    TipoConciliacion Tipo, string Empresa, int Anio, int Mes, DateTime Fecha,
    List<ConciliacionFilaDto> Filas, int NominasOmitidas,
    SatMetadatosPeriodoDto? MetadatosSat, TimeSpan Duracion);

/// <summary>
/// Conciliación de 3 vías por UUID: metadatos del SAT (existencia y estatus oficiales), bóveda local (XML) y
/// SINUBE (registro y póliza), como "XMLs sinube VS SAT" del portal en modo descarga masiva.
/// Proveedores usa DbFacturaProveedorDeXML; clientes, DbFactura. Ver Documentacion\Conciliacion_SINUBE_vs_SAT.md.
/// </summary>
public class ConciliacionSinubeService(
    PostgreSqlRepository repo, SinubeService sinube, SinubeDatosService datos, SatMetadatosService metadatosSat)
{
    // Nombres de campo de cada entidad de SINUBE (difieren en mayúsculas y prefijos).
    private sealed record Entidad(
        string Nombre, string Clasificacion, string Uuid, string Rfc, string RazonSocial, string Fecha,
        string Serie, string Folio, string? PolizaCancelada, string? CanceladaPac,
        string Total = "importeTotal", bool ConPoliza = true);

    private static readonly Entidad Proveedores = new(
        "DbFacturaProveedorDeXML", "RECIBIDO", "UUID", "rfcProveedor", "razonSocialProveedor", "fechaFacturaXml",
        "serieFacturaXml", "folioFacturaXml", PolizaCancelada: "polizaCancelada", CanceladaPac: null);

    private static readonly Entidad Clientes = new(
        "DbFactura", "EMITIDO", "uuid", Rfc: "", "razonSocial", "fechaFactura",
        "serieFactura", "folioFactura", PolizaCancelada: null, CanceladaPac: "canceladaPAC");

    // Complementos de pago de clientes ("Consulta de CFDI Complemento Recepción Pago"): la serie es con la que se
    // subió (p. ej. COMFR) y la póliza va en el depósito, no aquí.
    private static readonly Entidad PagosClientes = new(
        "DbRecepcionPagosXML", "EMITIDO", "uuid", Rfc: "", "razonSocial", "fechaRecepcionPago",
        "serieRecepcionPago", "folioRecepcionPago", PolizaCancelada: null, CanceladaPac: null,
        Total: "monto", ConPoliza: false);

    // Diferencia de total que se tolera por redondeo.
    private const decimal ToleranciaTotal = 0.01m;

    private sealed class Fuentes
    {
        public SatMetadatoDto? Sat;
        public CfdiConciliacionDto? Boveda;
        public FilaSinube? Sinube;
        public Entidad? EntidadSinube;
    }

    public async Task<ResultadoConciliacion> ConciliarAsync(
        EmpresaDto empresa, TipoConciliacion tipo, int anio, int mes, CancellationToken cancelacion = default)
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        Entidad e = tipo == TipoConciliacion.Clientes ? Clientes : Proveedores;
        bool recibidos = tipo == TipoConciliacion.Proveedores;
        SinubeConfiguracionDto configuracion = sinube.ObtenerConfiguracion(empresa);
        long periodo = anio * 100L + mes;

        // SINUBE clasifica en "mes" por la fecha del comprobante; SAT y bóveda filtran por fecha de emisión.
        string Consulta(Entidad x)
        {
            string campos = string.Join(", ", new[]
            {
                x.Uuid, x.Rfc, x.RazonSocial, x.Fecha, x.Total, x.Serie, x.Folio, "statusValidadoSAT",
                x.ConPoliza ? "tienePoliza, tipoPoliza, numPoliza, mesPoliza" : "", x.PolizaCancelada ?? "", x.CanceladaPac ?? ""
            }.Where(c => c.Length > 0));

            return $"SELECT {campos} FROM {x.Nombre} " +
                   $"WHERE empresa = '{configuracion.Empresa.Replace("'", "")}' AND mes = {periodo}";
        }

        // Clientes: facturas y notas (DbFactura) y complementos de pago (DbRecepcionPagosXML).
        Entidad[] entidades = tipo == TipoConciliacion.Clientes ? [e, PagosClientes] : [e];

        // SINUBE (la parte lenta, por red) corre mientras se leen la bóveda y los metadatos del SAT.
        Task<List<FilaSinube>[]> tareaSinube = Task.WhenAll(
            entidades.Select(x => datos.ConsultarAsync(configuracion, Consulta(x), cancelacion)));

        var desde = new DateTime(anio, mes, 1);
        List<CfdiConciliacionDto> deBoveda = repo.ObtenerCfdiConciliacion(empresa.EmpresaId, e.Clasificacion, desde, desde.AddMonths(1));
        (SatMetadatosPeriodoDto? periodoSat, List<SatMetadatoDto> deSat) = await metadatosSat.ObtenerAsync(empresa, recibidos, anio, mes);

        List<FilaSinube>[] deSinube = await tareaSinube;
        bool conSat = periodoSat is not null;

        var porUuid = new Dictionary<string, Fuentes>(StringComparer.OrdinalIgnoreCase);
        Fuentes De(string uuid) => porUuid.TryGetValue(uuid, out Fuentes? f) ? f : porUuid[uuid] = new Fuentes();

        foreach (SatMetadatoDto s in deSat)
            De(s.Uuid.Trim()).Sat = s;

        foreach (CfdiConciliacionDto c in deBoveda)
            De(c.Uuid.Trim()).Boveda = c;

        for (int i = 0; i < entidades.Length; i++)
        {
            foreach (FilaSinube f in deSinube[i])
            {
                // Las facturas sin timbrar no tienen UUID y no existen en el SAT.
                if (f.Texto(entidades[i].Uuid) is { Length: > 0 } uuid && De(uuid.Trim()) is { Sinube: null } fuentes)
                {
                    fuentes.Sinube = f;
                    fuentes.EntidadSinube = entidades[i];
                }
            }
        }

        // SINUBE lleva la nómina en otra entidad (DbNominaFactura): en clientes es solo informativa.
        int nominas = 0;
        if (tipo == TipoConciliacion.Clientes)
        {
            foreach (string uuid in porUuid.Where(p => p.Value.Sat?.Efecto == "N" || p.Value.Boveda?.TipoCfdi == "N").Select(p => p.Key).ToList())
            {
                porUuid.Remove(uuid);
                nominas++;
            }
        }

        List<ConciliacionFilaDto> filas = porUuid.Values
            .Select(f => CrearFila(e, recibidos, conSat, f, periodo))
            .OrderBy(f => f.Fecha)
            .ThenBy(f => f.RazonSocial)
            .ToList();

        return new ResultadoConciliacion(
            tipo, configuracion.Empresa, anio, mes, DateTime.Now, filas, nominas, periodoSat, reloj.Elapsed);
    }

    private static ConciliacionFilaDto CrearFila(Entidad principal, bool recibidos, bool conSat, Fuentes f, long periodo)
    {
        SatMetadatoDto? sat = f.Sat;
        CfdiConciliacionDto? b = f.Boveda;
        FilaSinube? s = f.Sinube;
        Entidad e = f.EntidadSinube ?? principal;

        bool existeSat = conSat ? sat is not null : b is not null;
        string estatusSat = sat is not null ? sat.Estatus : b is not null ? NormalizarEstatus(b.EstadoSat) : "";
        string estatusSinube = s is null ? "" : EstatusSinube(e, s);
        bool poliza = s is not null && s.Booleano("tienePoliza") && !(e.PolizaCancelada is string pc && s.Booleano(pc));
        long? mesPoliza = poliza ? s!.Entero("mesPoliza") : null;
        decimal? totalSinube = s?.Decimal(e.Total);
        decimal? totalSat = sat?.Monto ?? b?.Total;

        var diferencias = new List<string>();

        // Un CFDI cancelado en el SAT no tiene que estar registrado en SINUBE ni tener XML en la bóveda.
        bool canceladoEnSat = conSat && sat?.Estatus == "Cancelado";

        if (!existeSat)
            diferencias.Add(conSat ? "No aparece en el SAT" : "No está en la bóveda (SAT)");

        if (conSat && sat is not null && b is null && !canceladoEnSat)
            diferencias.Add("Falta el XML en la bóveda");

        if (s is null && !canceladoEnSat)
            diferencias.Add("No está en SINUBE");

        if (existeSat && s is not null)
        {
            if (EsEstatusConocido(estatusSat) && EsEstatusConocido(estatusSinube) && estatusSat != estatusSinube)
                diferencias.Add($"Estatus distinto (SAT {estatusSat}, SINUBE {estatusSinube})");
            else if (estatusSat == "Cancelado" && estatusSinube != "Cancelado")
                diferencias.Add("Cancelado en el SAT, activo en SINUBE");

            // Los metadatos traen 0 en los complementos de pago: ahí no se compara el total.
            if (totalSinube is decimal t && totalSat is decimal ts && ts != 0 && Math.Abs(t - ts) > ToleranciaTotal)
                diferencias.Add($"Total distinto (SAT {ts:N2}, SINUBE {t:N2})");
        }

        string rfcSat = sat is null ? "" : recibidos ? sat.RfcEmisor : sat.RfcReceptor;
        string nombreSat = sat is null ? "" : recibidos ? sat.NombreEmisor : sat.NombreReceptor;

        return new ConciliacionFilaDto
        {
            Uuid = (sat?.Uuid ?? b?.Uuid ?? s!.Texto(e.Uuid) ?? "").ToUpperInvariant(),
            RazonSocial = Primero(b?.NombreContraparte, nombreSat, s?.Texto(e.RazonSocial)),
            Rfc = Primero(b?.RfcContraparte, rfcSat, e.Rfc.Length > 0 ? s?.Texto(e.Rfc) : null),
            Fecha = b?.FechaEmision ?? sat?.FechaEmision ?? s?.Fecha(e.Fecha),
            Total = totalSat ?? totalSinube ?? 0,
            Tipo = NombreTipo(b?.TipoCfdi ?? sat?.Efecto ?? ""),
            Serie = Primero(b?.Serie, s?.Texto(e.Serie)),
            Folio = Primero(b?.Folio, s?.Texto(e.Folio)),
            Pac = b?.RfcPac ?? "",
            ExisteSat = existeSat,
            ExisteBoveda = b is not null,
            ExisteSinube = s is not null,
            EstatusSat = estatusSat,
            EstatusSinube = estatusSinube,
            Poliza = poliza,
            PolizaTexto = poliza ? $"{s!.Texto("tipoPoliza")}-{s.Entero("numPoliza")}" : "",
            MesPoliza = mesPoliza,
            MesDistinto = mesPoliza is long m && m != periodo,
            TotalSinube = totalSinube,
            Diferencias = diferencias
        };
    }

    private static string Primero(params string?[] valores) =>
        valores.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    /// <summary>
    /// statusValidadoSAT si SINUBE ya validó (0 Cancelado, 1 Vigente, 2 No encontrado). En clientes casi nunca
    /// valida: entonces canceladaPAC indica si SINUBE la tiene cancelada.
    /// </summary>
    private static string EstatusSinube(Entidad e, FilaSinube s) => s.Entero("statusValidadoSAT") switch
    {
        // En los complementos de pago el código es de dos dígitos: 1x vigente, 2x cancelado.
        >= 10 and < 20 when !e.ConPoliza => "Vigente",
        >= 20 and < 30 when !e.ConPoliza => "Cancelado",
        0 => "Cancelado",
        1 => "Vigente",
        2 => "No encontrado",
        _ when e.CanceladaPac is string cp && s.Booleano(cp) => "Cancelado",
        _ => "Sin validar"
    };

    private static string NormalizarEstatus(string estado) => estado.Trim().ToUpperInvariant() switch
    {
        "VIGENTE" => "Vigente",
        "CANCELADO" => "Cancelado",
        "NO ENCONTRADO" => "No encontrado",
        _ => "Sin validar"
    };

    private static bool EsEstatusConocido(string estatus) => estatus is "Vigente" or "Cancelado" or "No encontrado";

    private static string NombreTipo(string tipo) => tipo switch
    {
        "I" => "Ingreso",
        "E" => "Egreso",
        "P" => "Pago",
        "N" => "Nómina",
        "T" => "Traslado",
        _ => tipo
    };

    public static string Titulo(TipoConciliacion tipo) =>
        tipo == TipoConciliacion.Clientes ? "Clientes (emitidos)" : "Proveedores (recibidos)";

    public byte[] GenerarExcel(ResultadoConciliacion r, IEnumerable<ConciliacionFilaDto> filas)
    {
        using var libro = new XLWorkbook();
        IXLWorksheet hoja = libro.AddWorksheet(r.Tipo == TipoConciliacion.Clientes ? "Clientes" : "Proveedores");

        hoja.Cell(1, 1).Value = $"Conciliación SAT · bóveda · SINUBE · {Titulo(r.Tipo)} · {r.Empresa} · Periodo {r.Anio}{r.Mes:00}";
        hoja.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        hoja.Cell(2, 1).Value = $"Generado {r.Fecha:dd/MM/yyyy HH:mm} · " +
            (r.MetadatosSat is { } m ? $"metadatos del SAT al {m.Consultado:dd/MM/yyyy HH:mm}" : "sin metadatos del SAT (estatus de la bóveda)") +
            (r.NominasOmitidas > 0 ? $" · {r.NominasOmitidas} recibos de nómina no se concilian" : "");

        string[] encabezados =
        [
            "Razón social", "RFC", "Fecha emisión", "Total", "Conciliada", "Existe en SAT", "XML en bóveda", "Existe en SINUBE",
            "Estatus en SAT", "Estatus en SINUBE", "Tipo", "Serie", "Folio", "UUID", "Póliza", "Mes póliza",
            "Mes distinto", "PAC (RFC)", "Diferencias"
        ];

        for (int i = 0; i < encabezados.Length; i++)
            hoja.Cell(4, i + 1).Value = encabezados[i];

        int fila = 5;
        foreach (ConciliacionFilaDto f in filas)
        {
            object?[] valores =
            [
                f.RazonSocial, f.Rfc, f.Fecha, f.Total, SiNo(f.Conciliada), SiNo(f.ExisteSat), SiNo(f.ExisteBoveda), SiNo(f.ExisteSinube),
                f.EstatusSat, f.EstatusSinube, f.Tipo, f.Serie, f.Folio, f.Uuid, f.PolizaTexto,
                f.MesPoliza?.ToString() ?? "", SiNo(f.MesDistinto), f.Pac, string.Join("; ", f.Diferencias)
            ];

            for (int i = 0; i < valores.Length; i++)
                hoja.Cell(fila, i + 1).Value = XLCellValue.FromObject(valores[i]);

            fila++;
        }

        IXLRange tabla = hoja.Range(4, 1, Math.Max(fila - 1, 5), encabezados.Length);
        tabla.CreateTable().Theme = XLTableTheme.TableStyleMedium2;
        hoja.Column(3).Style.DateFormat.Format = "dd/MM/yyyy";
        hoja.Column(4).Style.NumberFormat.Format = "#,##0.00";
        hoja.Columns().AdjustToContents(4, fila, 8, 60);

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return memoria.ToArray();
    }

    private static string SiNo(bool valor) => valor ? "Sí" : "No";
}
