using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace CFDIManager.Web.Services.Cif;

/// <summary>Opinión de cumplimiento (SAT o IMSS) leída del PDF.</summary>
public sealed class OpinionCumplimientoDto
{
    /// <summary>SAT o IMSS.</summary>
    public string Tipo { get; set; } = "SAT";

    public string Rfc { get; set; } = "";

    /// <summary>POSITIVO, NEGATIVO, SIN OBLIGACIONES, NO INSCRITO o PENDIENTE (no se reconoció el sentido).</summary>
    public string Sentido { get; set; } = "PENDIENTE";

    public string Folio { get; set; } = "";

    /// <summary>Fecha de revisión/emisión que trae el documento, si se encontró.</summary>
    public DateTime? FechaEmision { get; set; }

    public DateTime Cargada { get; set; }

    public string ArchivoOriginal { get; set; } = "";
}

/// <summary>Acuse de declaración (mensual, anual o DIOT) guardado por la empresa.</summary>
public sealed class DeclaracionSatDto
{
    public string Id { get; set; } = "";

    /// <summary>MENSUAL, ANUAL o DIOT (declaración informativa de operaciones con terceros, mensual).</summary>
    public string Tipo { get; set; } = "MENSUAL";

    /// <summary>Folio de recepción del acuse (DIOT del aplicativo anterior; la plataforma nueva solo trae número de operación).</summary>
    public string Folio { get; set; } = "";

    /// <summary>DIOT: Mensual, medio de presentación (Internet) y versión del acuse.</summary>
    public string Periodicidad { get; set; } = "";

    public string Medio { get; set; } = "";

    public string Version { get; set; } = "";

    /// <summary>DIOT: total de operaciones que relaciona.</summary>
    public int? Operaciones { get; set; }

    /// <summary>DIOT: total del IVA pagado sin incluir importaciones.</summary>
    public decimal? IvaPagado { get; set; }

    public int Ejercicio { get; set; }

    /// <summary>1-12 para mensuales; 0 para anuales.</summary>
    public int Periodo { get; set; }

    /// <summary>Normal o Complementaria, si el acuse lo dice.</summary>
    public string TipoDeclaracion { get; set; } = "";

    public string NumeroOperacion { get; set; } = "";

    public DateTime? FechaPresentacion { get; set; }

    public decimal? ImporteAPagar { get; set; }

    public string Archivo { get; set; } = "";

    public string ArchivoOriginal { get; set; } = "";

    public DateTime Cargada { get; set; }
}

/// <summary>
/// Archivos SAT de cada empresa en Storage\ArchivosSat\{empresa}\: opiniones de cumplimiento
/// (opinion-sat.pdf/.json, opinion-imss.pdf/.json) y acuses de declaraciones
/// (declaraciones\*.pdf con su índice declaraciones.json). Los datos se leen del texto del PDF.
/// </summary>
public sealed class ArchivosSatService
{
    private static readonly JsonSerializerOptions OpcionesJson = new() { WriteIndented = true };

    private static readonly CultureInfo Mx = new("es-MX");

    private static readonly Regex Rfc = new("[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}", RegexOptions.Compiled);

    private readonly IWebHostEnvironment _ambiente;

    public ArchivosSatService(IWebHostEnvironment ambiente)
    {
        _ambiente = ambiente;
    }

    public string Carpeta(long empresaId) =>
        CarpetaStorage.Ruta("ArchivosSat", empresaId.ToString(CultureInfo.InvariantCulture));

    // ---------- Opiniones de cumplimiento ----------

    public OpinionCumplimientoDto? ObtenerOpinion(long empresaId, string tipo)
    {
        string archivo = Path.Combine(Carpeta(empresaId), $"opinion-{tipo.ToLowerInvariant()}.json");

        return File.Exists(archivo)
            ? JsonSerializer.Deserialize<OpinionCumplimientoDto>(File.ReadAllText(archivo))
            : null;
    }

    public string? RutaOpinion(long empresaId, string tipo)
    {
        string ruta = Path.Combine(Carpeta(empresaId), $"opinion-{tipo.ToLowerInvariant()}.pdf");
        return File.Exists(ruta) ? ruta : null;
    }

    /// <summary>Lee la opinión, revisa que sea del RFC de la empresa y la guarda (sustituye la anterior).</summary>
    public OpinionCumplimientoDto CargarOpinion(long empresaId, string rfcEmpresa, string tipo, byte[] pdf, string nombreArchivo)
    {
        tipo = tipo.ToUpperInvariant() == "IMSS" ? "IMSS" : "SAT";
        string texto = Texto(pdf);
        string mayusculas = SinAcentos(texto).ToUpperInvariant();

        if (!mayusculas.Contains("OPINION") || !mayusculas.Contains("CUMPLIMIENTO"))
            throw new InvalidOperationException("El PDF no parece una Opinión de cumplimiento.");

        var opinion = new OpinionCumplimientoDto
        {
            Tipo = tipo,
            Rfc = PrimerRfc(texto, rfcEmpresa),
            Sentido = Sentido(mayusculas),
            // El SAT pone las etiquetas juntas y luego los valores: "RFC Folio CPO7501138I6 26NF9823411".
            Folio = Regex.Match(texto, @"Folio:?\s*(?:[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}\s+)?([0-9A-Z]{6,})").Groups[1].Value,
            FechaEmision = PrimeraFecha(texto),
            Cargada = DateTime.Now,
            ArchivoOriginal = nombreArchivo
        };

        if (opinion.Rfc != "" && !string.IsNullOrWhiteSpace(rfcEmpresa) &&
            !opinion.Rfc.Equals(rfcEmpresa.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"La opinión es del RFC {opinion.Rfc}, no de la empresa ({rfcEmpresa.Trim().ToUpperInvariant()}).");
        }

        string carpeta = Carpeta(empresaId);
        Directory.CreateDirectory(carpeta);
        File.WriteAllBytes(Path.Combine(carpeta, $"opinion-{tipo.ToLowerInvariant()}.pdf"), pdf);
        File.WriteAllText(Path.Combine(carpeta, $"opinion-{tipo.ToLowerInvariant()}.json"), JsonSerializer.Serialize(opinion, OpcionesJson));

        return opinion;
    }

    /// <summary>El RFC de la empresa si aparece en el documento; si no, el primero que se encuentre.</summary>
    private static string PrimerRfc(string texto, string rfcEmpresa)
    {
        List<string> encontrados = Rfc.Matches(texto.ToUpperInvariant()).Select(m => m.Value).ToList();

        return encontrados.FirstOrDefault(r => r.Equals(rfcEmpresa?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? encontrados.FirstOrDefault()
            ?? "";
    }

    /// <summary>
    /// SAT: "...se emite opinión del cumplimiento de obligaciones fiscales en sentido positivo";
    /// IMSS: "...opinión ... en sentido POSITIVA". También "sin obligaciones" y "no inscrito".
    /// </summary>
    private static string Sentido(string mayusculas)
    {
        if (Regex.IsMatch(mayusculas, @"SENTIDO\s+NEGATIV[OA]") || mayusculas.Contains("OPINION NEGATIVA"))
            return "NEGATIVO";

        if (Regex.IsMatch(mayusculas, @"SENTIDO\s+POSITIV[OA]") || mayusculas.Contains("OPINION POSITIVA"))
            return "POSITIVO";

        if (mayusculas.Contains("SIN OBLIGACIONES"))
            return "SIN OBLIGACIONES";

        if (mayusculas.Contains("NO INSCRITO"))
            return "NO INSCRITO";

        if (Regex.IsMatch(mayusculas, @"\bNEGATIV[OA]\b"))
            return "NEGATIVO";

        return Regex.IsMatch(mayusculas, @"\bPOSITIV[OA]\b") ? "POSITIVO" : "PENDIENTE";
    }

    // ---------- Declaraciones ----------

    public List<DeclaracionSatDto> ObtenerDeclaraciones(long empresaId)
    {
        string indice = Path.Combine(Carpeta(empresaId), "declaraciones", "declaraciones.json");

        return File.Exists(indice)
            ? JsonSerializer.Deserialize<List<DeclaracionSatDto>>(File.ReadAllText(indice)) ?? []
            : [];
    }

    public string? RutaDeclaracion(long empresaId, string id)
    {
        DeclaracionSatDto? d = ObtenerDeclaraciones(empresaId).FirstOrDefault(x => x.Id == id);

        if (d is null)
            return null;

        string ruta = Path.Combine(Carpeta(empresaId), "declaraciones", d.Archivo);
        return File.Exists(ruta) ? ruta : null;
    }

    /// <summary>
    /// Guarda el acuse. Ejercicio y periodo se toman del PDF cuando los trae; si no, los elegidos en pantalla.
    /// </summary>
    public DeclaracionSatDto CargarDeclaracion(
        long empresaId, string rfcEmpresa, string tipo, int ejercicio, int periodo, byte[] pdf, string nombreArchivo,
        bool validarContenido = true)
    {
        string texto = Texto(pdf);
        string mayusculas = SinAcentos(texto).ToUpperInvariant();

        if (validarContenido && !mayusculas.Contains("DECLARACION") && !mayusculas.Contains("ACUSE") && !mayusculas.Contains("LINEA DE CAPTURA"))
            throw new InvalidOperationException("El PDF no parece un acuse de declaración del SAT.");

        string rfcPdf = PrimerRfc(texto, rfcEmpresa);

        if (rfcPdf != "" && !string.IsNullOrWhiteSpace(rfcEmpresa) && !rfcPdf.Equals(rfcEmpresa.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"El acuse es del RFC {rfcPdf}, no de la empresa ({rfcEmpresa.Trim().ToUpperInvariant()}).");

        // El acuse de la DIOT ("Declaración informativa de operaciones con terceros") se guarda como DIOT aunque en
        // pantalla esté elegido Mensual; si se eligió DIOT, el PDF tiene que serlo.
        bool esDiot = mayusculas.Contains("OPERACIONES CON TERCEROS");
        tipo = tipo.ToUpperInvariant();

        if (validarContenido && tipo == "DIOT" && !esDiot)
            throw new InvalidOperationException("El PDF no es un acuse de la DIOT (declaración informativa de operaciones con terceros).");

        tipo = esDiot ? "DIOT" : tipo == "ANUAL" ? "ANUAL" : "MENSUAL";

        if (Regex.Match(mayusculas, @"EJERCICIO(?:\s+FISCAL)?:?\s*(20\d{2})") is { Success: true } e)
            ejercicio = int.Parse(e.Groups[1].Value, CultureInfo.InvariantCulture);

        // "Periodo: Junio" (DIOT anterior) o "Período de la declaración: Agosto" (DIOT nueva).
        if (tipo != "ANUAL" && Regex.Match(mayusculas, @"PERIODO(?: DE LA DECLARACION)?:?\s*([A-Z]+)") is { Success: true } p && Mes(p.Groups[1].Value) is int mes)
            periodo = mes;

        var declaracion = new DeclaracionSatDto
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Tipo = tipo,
            Ejercicio = ejercicio,
            Periodo = tipo == "ANUAL" ? 0 : periodo,
            TipoDeclaracion = Regex.Match(mayusculas, @"TIPO DE DECLARACION:?\s*(NORMAL|COMPLEMENTARIA)").Groups[1].Value switch
            {
                "NORMAL" => "Normal",
                "COMPLEMENTARIA" => "Complementaria",
                _ => ""
            },
            NumeroOperacion = Regex.Match(mayusculas, @"(?:NUMERO|NO\.?) DE OPERACION:?\s*(\d{6,})").Groups[1].Value,
            FechaPresentacion = PrimeraFecha(texto),
            ImporteAPagar = esDiot ? null : Importe(mayusculas),
            ArchivoOriginal = nombreArchivo,
            Cargada = DateTime.Now
        };

        if (esDiot)
            LeerDiot(mayusculas, declaracion);

        if (esDiot && declaracion.Folio != "" && ObtenerDeclaraciones(empresaId).Any(d => d.Tipo == "DIOT" && d.Folio == declaracion.Folio))
            throw new InvalidOperationException($"El acuse de la DIOT con folio {declaracion.Folio} ya está cargado.");

        if ((!esDiot || declaracion.Folio == "") && TieneOperacion(empresaId, declaracion.NumeroOperacion))
            throw new InvalidOperationException($"El acuse con número de operación {declaracion.NumeroOperacion} ya está cargado.");

        string nombre = tipo switch
        {
            "ANUAL" => $"{ejercicio}-anual",
            "DIOT" => $"{ejercicio}-{declaracion.Periodo:00}-diot",
            _ => $"{ejercicio}-{declaracion.Periodo:00}"
        };
        declaracion.Archivo = $"{nombre}-{declaracion.Id}.pdf";

        string carpeta = Path.Combine(Carpeta(empresaId), "declaraciones");
        Directory.CreateDirectory(carpeta);
        File.WriteAllBytes(Path.Combine(carpeta, declaracion.Archivo), pdf);

        List<DeclaracionSatDto> lista = ObtenerDeclaraciones(empresaId);
        lista.Add(declaracion);
        GuardarIndice(empresaId, lista);

        return declaracion;
    }

    /// <summary>
    /// Datos del acuse de la DIOT: aplicativo anterior ("Periodo: Junio", "Folio de Recepción", número de operación
    /// alfanumérico como 273FA) o plataforma nueva ("Período de la declaración: Agosto", "Fecha y hora de
    /// presentación: 30/09/2026 12:15", periodicidad, medio, versión, operaciones e IVA pagado). Lo que no encuentra
    /// lo deja como estaba.
    /// </summary>
    private static void LeerDiot(string mayusculas, DeclaracionSatDto d)
    {
        string Valor(string patron) => Regex.Match(mayusculas, patron).Groups[1].Value;

        if (Valor(@"EJERCICIO(?:\s+FISCAL)?:?\s*(20\d{2})") is { Length: > 0 } ejercicio)
            d.Ejercicio = int.Parse(ejercicio, CultureInfo.InvariantCulture);
        if (Mes(Valor(@"PERIODO(?: DE LA DECLARACION)?:?\s*([A-Z]+)")) is int mes)
            d.Periodo = mes;
        if (Valor(@"(?:NUMERO|NO\.?) DE OPERACION:?\s*([0-9A-Z]{3,})") is { Length: > 0 } operacion)
            d.NumeroOperacion = operacion;

        // El primer dd/mm/aaaa del texto puede ser el encabezado de impresión del navegador: se toma el del campo.
        Match f = Regex.Match(mayusculas, @"FECHA (?:Y HORA )?DE PRESENTACION:?\s*(\d{2}/\d{2}/20\d{2})(?:\s+(\d{1,2}:\d{2}))?");
        if (f.Success && DateTime.TryParseExact(f.Groups[1].Value + (f.Groups[2].Success ? " " + f.Groups[2].Value : ""),
                ["dd/MM/yyyy", "dd/MM/yyyy H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha))
            d.FechaPresentacion = fecha;

        d.Folio = Valor(@"FOLIO DE RECEPCION:?\s*(\d{4,})");
        d.Periodicidad = Titulo(Valor(@"PERIODICIDAD:?\s*([A-Z]+)"));
        d.Medio = Titulo(Valor(@"MEDIO DE PRESENTACION:?\s*([A-Z]+)"));
        d.Version = Valor(@"VERSION:?\s*(\d+)");
        d.Operaciones = Valor(@"TOTAL DE OPERACIONES QUE RELACIONA\s+(\d+)") is { Length: > 0 } ops ? int.Parse(ops, CultureInfo.InvariantCulture) : null;
        d.IvaPagado = Valor(@"SIN INCLUIR IMPORTACIONES(?:\s+VACIO)?\s+IVA PAGADO\s+\$?\s*([\d,]+(?:\.\d{1,2})?)") is { Length: > 0 } iva
            ? decimal.Parse(iva.Replace(",", ""), CultureInfo.InvariantCulture)
            : null;
    }

    private static string Titulo(string valor) => valor.Length == 0 ? "" : valor[..1] + valor[1..].ToLowerInvariant();

    /// <summary>
    /// Vuelve a leer los acuses de DIOT guardados antes de que la app reconociera el formato nuevo (sin
    /// periodicidad): corrige periodo, fecha y hora, operaciones e IVA pagado con lo que trae su PDF.
    /// </summary>
    public void ReleerDiot(long empresaId)
    {
        List<DeclaracionSatDto> lista = ObtenerDeclaraciones(empresaId);
        bool cambio = false;

        foreach (DeclaracionSatDto d in lista.Where(d => d.Tipo == "DIOT" && d.Periodicidad == "" && d.Folio == ""))
        {
            if (RutaDeclaracion(empresaId, d.Id) is not string ruta)
                continue;

            try
            {
                LeerDiot(SinAcentos(Texto(File.ReadAllBytes(ruta))).ToUpperInvariant(), d);
                cambio = true;
            }
            catch (InvalidOperationException)
            {
                // PDF ilegible: se queda como estaba.
            }
        }

        if (cambio)
            GuardarIndice(empresaId, lista);
    }

    public bool TieneOperacion(long empresaId, string numeroOperacion) =>
        numeroOperacion != "" && ObtenerDeclaraciones(empresaId).Any(d => d.NumeroOperacion == numeroOperacion);

    /// <summary>
    /// Guarda un acuse bajado del portal del SAT. Lo que el PDF no traiga se completa con los
    /// datos del portal (número de operación, fecha, tipo y periodo).
    /// </summary>
    public DeclaracionSatDto GuardarDescargada(
        long empresaId, string rfcEmpresa, string tipo, int ejercicio, int periodo, byte[] pdf,
        string numeroOperacion, string tipoDeclaracion, DateTime? fechaPresentacion)
    {
        DeclaracionSatDto d = CargarDeclaracion(empresaId, rfcEmpresa, tipo, ejercicio, periodo, pdf, $"SAT-{numeroOperacion}.pdf", validarContenido: false);

        List<DeclaracionSatDto> lista = ObtenerDeclaraciones(empresaId);
        DeclaracionSatDto guardada = lista.First(x => x.Id == d.Id);

        if (numeroOperacion != "") guardada.NumeroOperacion = numeroOperacion;
        if (guardada.TipoDeclaracion == "" && tipoDeclaracion != "") guardada.TipoDeclaracion = tipoDeclaracion;
        if (fechaPresentacion is not null) guardada.FechaPresentacion = fechaPresentacion;
        if (guardada.Tipo == "MENSUAL" && periodo is >= 1 and <= 12) guardada.Periodo = periodo;

        GuardarIndice(empresaId, lista);
        return guardada;
    }

    public void EliminarDeclaracion(long empresaId, string id)
    {
        List<DeclaracionSatDto> lista = ObtenerDeclaraciones(empresaId);

        if (lista.FirstOrDefault(x => x.Id == id) is not DeclaracionSatDto d)
            return;

        string ruta = Path.Combine(Carpeta(empresaId), "declaraciones", d.Archivo);

        if (File.Exists(ruta))
            File.Delete(ruta);

        lista.Remove(d);
        GuardarIndice(empresaId, lista);
    }

    private void GuardarIndice(long empresaId, List<DeclaracionSatDto> lista) =>
        File.WriteAllText(
            Path.Combine(Carpeta(empresaId), "declaraciones", "declaraciones.json"),
            JsonSerializer.Serialize(lista.OrderByDescending(x => x.Ejercicio).ThenByDescending(x => x.Periodo).ToList(), OpcionesJson));

    /// <summary>"Cantidad a pagar: $ 12,345" / "Total a pagar 12,345.00" / "Importe a pagar".</summary>
    private static decimal? Importe(string mayusculas)
    {
        Match m = Regex.Match(mayusculas, @"(?:CANTIDAD|TOTAL|IMPORTE)\s+A\s+PAGAR:?\s*\$?\s*([\d,]+(?:\.\d{1,2})?)");

        return m.Success && decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal valor)
            ? valor
            : null;
    }

    private static int? Mes(string texto)
    {
        for (int m = 1; m <= 12; m++)
        {
            if (SinAcentos(Mx.DateTimeFormat.GetMonthName(m)).Equals(texto, StringComparison.OrdinalIgnoreCase))
                return m;
        }

        return null;
    }

    // ---------- Texto del PDF ----------

    private static string Texto(byte[] pdf)
    {
        try
        {
            using PdfDocument documento = PdfDocument.Open(pdf);
            return string.Join("\n", documento.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"No se pudo leer el PDF: {ex.Message}");
        }
    }

    /// <summary>Primera fecha "29 de octubre de 2026", "29/10/2026" o "2026-10-29" del texto.</summary>
    private static DateTime? PrimeraFecha(string texto)
    {
        Match larga = Regex.Match(SinAcentos(texto), @"(\d{1,2})\s+de\s+([a-zA-Z]+)\s+de\s+(20\d{2})", RegexOptions.IgnoreCase);

        if (larga.Success && Mes(larga.Groups[2].Value) is int mes &&
            int.TryParse(larga.Groups[1].Value, out int dia) && dia is >= 1 and <= 31)
        {
            try
            {
                return new DateTime(int.Parse(larga.Groups[3].Value, CultureInfo.InvariantCulture), mes, dia);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Fecha imposible (31 de febrero): se prueba con los otros formatos.
            }
        }

        Match corta = Regex.Match(texto, @"\b(\d{2}/\d{2}/20\d{2}|20\d{2}-\d{2}-\d{2})\b");

        return corta.Success &&
               DateTime.TryParseExact(corta.Value, ["dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha)
            ? fecha
            : null;
    }

    private static string SinAcentos(string texto) =>
        new string(texto.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
}
