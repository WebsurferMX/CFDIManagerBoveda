using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CFDIManager.Web.Services.Cif;

public sealed class RegimenCif
{
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string FechaInicio { get; set; } = "";
    public string FechaFin { get; set; } = "";
}

public sealed class ActividadCif
{
    public string Orden { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Porcentaje { get; set; } = "";
    public string FechaInicio { get; set; } = "";
    public string FechaFin { get; set; } = "";
}

public sealed class ObligacionCif
{
    public string Descripcion { get; set; } = "";
    public string Vencimiento { get; set; } = "";
    public string FechaInicio { get; set; } = "";
    public string FechaFin { get; set; } = "";

    /// <summary>Anual o mensual, deducido del vencimiento.</summary>
    public string Periodicidad =>
        Vencimiento.Contains("cierre del ejercicio", StringComparison.OrdinalIgnoreCase) ||
        Vencimiento.Contains("anual", StringComparison.OrdinalIgnoreCase) ||
        Descripcion.Contains("anual", StringComparison.OrdinalIgnoreCase)
            ? "Declaración anual"
            : Vencimiento.Contains("mes", StringComparison.OrdinalIgnoreCase)
                ? "Declaración mensual"
                : "";
}

/// <summary>Datos de la Constancia de Situación Fiscal (CIF) extraídos del PDF del SAT.</summary>
public sealed class ConstanciaFiscalDto
{
    public string IdCif { get; set; } = "";
    public string Rfc { get; set; } = "";
    public string Curp { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string RegimenCapital { get; set; } = "";
    public string NombreComercial { get; set; } = "";
    public string InicioOperaciones { get; set; } = "";
    public string EstatusPadron { get; set; } = "";
    public string UltimoCambioEstado { get; set; } = "";
    public string LugarFechaEmision { get; set; } = "";

    public Dictionary<string, string> Domicilio { get; set; } = new();

    public List<RegimenCif> Regimenes { get; set; } = new();
    public List<ActividadCif> Actividades { get; set; } = new();
    public List<ObligacionCif> Obligaciones { get; set; } = new();

    public DateTime Cargada { get; set; }
    public string ArchivoOriginal { get; set; } = "";

    /// <summary>Partes que no se pudieron leer; la constancia se guarda de todos modos.</summary>
    public List<string> Avisos { get; set; } = new();
}

/// <summary>
/// Lee la Constancia de Situación Fiscal en PDF y guarda el resultado en
/// Storage\Cif\{empresa}\ (constancia.pdf y constancia.json). Las tablas se leen por la
/// posición de cada palabra: así el pie de página ("Página 2 de 2") no se mezcla con los
/// renglones y los textos que ocupan varias líneas se juntan en su renglón.
/// </summary>
public sealed class ConstanciaFiscalService
{
    private static readonly JsonSerializerOptions OpcionesJson = new() { WriteIndented = true };

    private static readonly Regex Fecha = new(@"^\d{2}/\d{2}/\d{4}$");

    private static readonly string[] Etiquetas =
    {
        "idCIF", "RFC", "CURP", "Nombre (s)", "Primer Apellido", "Segundo Apellido", "Denominación/Razón Social",
        "Régimen Capital", "Nombre Comercial", "Fecha inicio de operaciones", "Estatus en el padrón",
        "Fecha de último cambio de estado", "Código Postal", "Tipo de Vialidad", "Nombre de Vialidad",
        "Número Exterior", "Número Interior", "Nombre de la Colonia", "Nombre de la Localidad",
        "Nombre del Municipio o Demarcación Territorial", "Nombre de la Entidad Federativa", "Entre Calle", "Y Calle",
        "Correo Electrónico", "Tel. Fijo Lada", "Número", "Estado de la Situación"
    };

    private static readonly string[] CamposDomicilio =
    {
        "Código Postal", "Tipo de Vialidad", "Nombre de Vialidad", "Número Exterior", "Número Interior",
        "Nombre de la Colonia", "Nombre de la Localidad", "Nombre del Municipio o Demarcación Territorial",
        "Nombre de la Entidad Federativa", "Entre Calle", "Y Calle"
    };

    // Catálogo c_RegimenFiscal del SAT: palabras del nombre en la CIF → clave.
    private static readonly (string Clave, string[] Palabras)[] Regimenes =
    {
        ("601", new[] { "general de ley personas morales" }),
        ("603", new[] { "personas morales con fines no lucrativos" }),
        ("605", new[] { "sueldos y salarios" }),
        ("606", new[] { "arrendamiento" }),
        ("607", new[] { "enajenacion o adquisicion de bienes" }),
        ("608", new[] { "demas ingresos" }),
        ("610", new[] { "residentes en el extranjero" }),
        ("611", new[] { "dividendos" }),
        ("612", new[] { "actividades empresariales y profesionales" }),
        ("614", new[] { "ingresos por intereses" }),
        ("615", new[] { "obtencion de premios" }),
        ("616", new[] { "sin obligaciones fiscales" }),
        ("620", new[] { "sociedades cooperativas de produccion" }),
        ("621", new[] { "incorporacion fiscal" }),
        ("622", new[] { "actividades agricolas" }),
        ("623", new[] { "opcional para grupos de sociedades" }),
        ("624", new[] { "coordinados" }),
        ("625", new[] { "plataformas tecnologicas" }),
        ("626", new[] { "simplificado de confianza" })
    };

    private readonly IWebHostEnvironment _ambiente;

    public ConstanciaFiscalService(IWebHostEnvironment ambiente)
    {
        _ambiente = ambiente;
    }

    public ConstanciaFiscalDto? Obtener(long empresaId)
    {
        string archivo = Path.Combine(Carpeta(empresaId), "constancia.json");

        return File.Exists(archivo)
            ? JsonSerializer.Deserialize<ConstanciaFiscalDto>(File.ReadAllText(archivo))
            : null;
    }

    public string? RutaPdf(long empresaId)
    {
        string ruta = Path.Combine(Carpeta(empresaId), "constancia.pdf");
        return File.Exists(ruta) ? ruta : null;
    }

    /// <summary>Lee el PDF, revisa que sea del RFC de la empresa y lo guarda junto con los datos.</summary>
    public ConstanciaFiscalDto Cargar(long empresaId, string rfcEmpresa, byte[] pdf, string nombreArchivo)
    {
        ConstanciaFiscalDto cif = Leer(pdf);

        if (cif.Rfc == "")
            throw new InvalidOperationException("El PDF no parece una Constancia de Situación Fiscal del SAT (no se encontró el RFC).");

        if (!string.IsNullOrWhiteSpace(rfcEmpresa) && !cif.Rfc.Equals(rfcEmpresa.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"La constancia es del RFC {cif.Rfc}, no de la empresa ({rfcEmpresa.Trim().ToUpperInvariant()}).");

        cif.Cargada = DateTime.Now;
        cif.ArchivoOriginal = nombreArchivo;

        string carpeta = Carpeta(empresaId);
        Directory.CreateDirectory(carpeta);

        File.WriteAllBytes(Path.Combine(carpeta, "constancia.pdf"), pdf);
        File.WriteAllText(Path.Combine(carpeta, "constancia.json"), JsonSerializer.Serialize(cif, OpcionesJson));

        return cif;
    }

    private string Carpeta(long empresaId) =>
        CarpetaStorage.Ruta("Cif", empresaId.ToString(CultureInfo.InvariantCulture));

    // ---------- lectura del PDF ----------

    private sealed record Linea(int Pagina, double Y, List<Word> Palabras)
    {
        public string Texto => string.Join(" ", Palabras.Select(p => p.Text));
    }

    private static ConstanciaFiscalDto Leer(byte[] pdf)
    {
        List<Linea> lineas;

        try
        {
            lineas = LeerLineas(pdf);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"No se pudo leer el PDF: {ex.Message}");
        }

        var cif = new ConstanciaFiscalDto();
        string texto = string.Join("\n", lineas.Select(l => l.Texto));

        Dictionary<string, string> v = LeerEtiquetas(lineas);

        cif.IdCif = Valor(v, "idCIF");
        cif.Rfc = Regex.Match(Valor(v, "RFC"), "[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}").Value;
        cif.Curp = Valor(v, "CURP");
        cif.Nombre = Valor(v, "Denominación/Razón Social");

        if (cif.Nombre == "")
            cif.Nombre = string.Join(" ", new[] { Valor(v, "Nombre (s)"), Valor(v, "Primer Apellido"), Valor(v, "Segundo Apellido") }.Where(s => s != ""));

        cif.RegimenCapital = Valor(v, "Régimen Capital");
        cif.NombreComercial = Valor(v, "Nombre Comercial");
        cif.InicioOperaciones = Valor(v, "Fecha inicio de operaciones");
        cif.EstatusPadron = Valor(v, "Estatus en el padrón");
        cif.UltimoCambioEstado = Valor(v, "Fecha de último cambio de estado");

        foreach (string campo in CamposDomicilio)
        {
            if (Valor(v, campo) is { Length: > 0 } valor)
                cif.Domicilio[campo] = valor;
        }

        // "CUAUHTEMOC , CIUDAD DE MEXICO A 28 DE SEPTIEMBRE DE 2026": en mayúsculas, puede venir partido
        // en dos líneas y mezclado con la leyenda "Nombre, denominación o razón social".
        int lugar = texto.IndexOf("Lugar y Fecha de Emisi", StringComparison.OrdinalIgnoreCase);
        int idCif = texto.IndexOf("idCIF", StringComparison.OrdinalIgnoreCase);
        string encabezado = lugar >= 0 && idCif > lugar ? texto[lugar..idCif] : texto;
        encabezado = Regex.Replace(encabezado, @"Nombre, denominaci[oó]n o raz[oó]n|\bsocial\b|Lugar y Fecha de Emisi[oó]n", " ");

        Match emision = Regex.Match(encabezado, @"[A-ZÁÉÍÓÚÑÜ][A-ZÁÉÍÓÚÑÜ .,]*?\s*,\s*[A-ZÁÉÍÓÚÑÜ .]+?\s+A\s+\d{1,2}\s+DE\s+[A-ZÁÉÍÓÚÑ]+\s+DE\s+\d{4}");
        cif.LugarFechaEmision = emision.Success ? Regex.Replace(emision.Value, @"\s+", " ").Replace(" ,", ",").Trim() : "";

        // El final de una razón social larga queda en la línea del lugar ("POLIETILENO MIGUEL HIDALGO, ...").
        // Solo si el nombre no cabe completo en su propia línea del encabezado.
        string[] lugarPalabras = cif.LugarFechaEmision.Split(' ');
        string[] nombrePalabras = cif.Nombre.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool nombrePartido = cif.Nombre != "" &&
            !lineas.TakeWhile(l => !l.Texto.StartsWith("idCIF", StringComparison.OrdinalIgnoreCase))
                .Any(l => l.Texto.Contains(cif.Nombre.Trim(), StringComparison.OrdinalIgnoreCase));

        for (int n = nombrePartido ? Math.Min(nombrePalabras.Length, lugarPalabras.Length - 1) : 0; n > 0; n--)
        {
            if (lugarPalabras.Take(n).SequenceEqual(nombrePalabras.TakeLast(n)))
            {
                cif.LugarFechaEmision = string.Join(" ", lugarPalabras.Skip(n));
                break;
            }
        }

        foreach (string[] fila in LeerTabla(lineas, "Regímenes", new[] { "Régimen", "Fecha", "Fecha" }, cif.Avisos))
        {
            cif.Regimenes.Add(new RegimenCif
            {
                Nombre = fila[0],
                Clave = ClaveRegimen(fila[0]),
                FechaInicio = fila[1],
                FechaFin = fila[2]
            });
        }

        foreach (string[] fila in LeerTabla(lineas, "Actividades Económicas", new[] { "Orden", "Actividad", "Porcentaje", "Fecha", "Fecha" }, cif.Avisos))
        {
            // Lo que no sea número en Orden es inicio de la descripción y en Porcentaje, su final
            // (la descripción ocupa casi todo el ancho de su columna).
            string orden = Regex.Match(fila[0], @"\d+").Value;
            string inicioTexto = Regex.Replace(fila[0], @"^\s*\d+\s*", "").Trim();
            string porcentaje = Regex.Match(fila[2], @"\d+(\.\d+)?").Value;
            string finTexto = Regex.Replace(fila[2], @"\d+(\.\d+)?\s*%?", "").Trim();

            cif.Actividades.Add(new ActividadCif
            {
                Orden = orden,
                Descripcion = $"{inicioTexto} {fila[1]} {finTexto}".Trim(),
                Porcentaje = porcentaje,
                FechaInicio = fila[3],
                FechaFin = fila[4]
            });
        }

        cif.Actividades = cif.Actividades
            .OrderBy(a => int.TryParse(a.Orden, out int n) ? n : int.MaxValue)
            .ToList();

        foreach (string[] fila in LeerTabla(lineas, "Obligaciones", new[] { "Descripción", "Descripción", "Fecha", "Fecha" }, cif.Avisos))
        {
            cif.Obligaciones.Add(new ObligacionCif
            {
                Descripcion = fila[0],
                Vencimiento = fila[1],
                FechaInicio = fila[2],
                FechaFin = fila[3]
            });
        }

        return cif;
    }

    private static List<Linea> LeerLineas(byte[] pdf)
    {
        var lineas = new List<Linea>();

        using PdfDocument documento = PdfDocument.Open(pdf);

        foreach (Page pagina in documento.GetPages())
        {
            // Palabras con la misma altura (±2.5 pt) forman una línea; de arriba hacia abajo.
            var grupos = new List<(double Y, List<Word> Palabras)>();

            foreach (Word palabra in pagina.GetWords().OrderByDescending(p => p.BoundingBox.Bottom))
            {
                double y = palabra.BoundingBox.Bottom;
                int i = grupos.FindIndex(g => Math.Abs(g.Y - y) <= 2.5);

                if (i < 0)
                    grupos.Add((y, new List<Word> { palabra }));
                else
                    grupos[i].Palabras.Add(palabra);
            }

            foreach (var (y, palabras) in grupos.OrderByDescending(g => g.Y))
            {
                List<Word> ordenadas = QuitarPie(palabras.OrderBy(p => p.BoundingBox.Left).ToList());

                if (ordenadas.Count > 0)
                    lineas.Add(new Linea(pagina.Number, y, ordenadas));
            }
        }

        return lineas;
    }

    /// <summary>Quita "Página N de M" de una línea (el pie del SAT puede quedar a la altura de un renglón).</summary>
    private static List<Word> QuitarPie(List<Word> palabras)
    {
        for (int i = 0; i + 3 < palabras.Count; i++)
        {
            if (palabras[i].Text.Equals("Página", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(palabras[i + 1].Text, @"^\[?\d+\]?$") &&
                palabras[i + 2].Text.Equals("de", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(palabras[i + 3].Text, @"^\[?\d+\]?$"))
            {
                palabras.RemoveRange(i, 4);
                i--;
            }
        }

        return palabras;
    }

    /// <summary>Valor de cada etiqueta "Etiqueta: valor"; en una línea puede haber dos etiquetas (domicilio).</summary>
    private static Dictionary<string, string> LeerEtiquetas(List<Linea> lineas)
    {
        var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string alternativas = string.Join("|", Etiquetas.OrderByDescending(e => e.Length).Select(Regex.Escape));
        var patron = new Regex($@"(?<![\p{{L}}])({alternativas})\s*:\s*(.*?)(?=\s*(?:{alternativas})\s*:|$)", RegexOptions.IgnoreCase);

        foreach (Linea linea in lineas)
        {
            foreach (Match m in patron.Matches(linea.Texto))
            {
                string etiqueta = Etiquetas.First(e => e.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                valores.TryAdd(etiqueta, m.Groups[2].Value.Trim());
            }
        }

        return valores;
    }

    private static string Valor(Dictionary<string, string> valores, string etiqueta) =>
        valores.TryGetValue(etiqueta, out string? v) ? v : "";

    // ---------- tablas ----------

    private static readonly string[] FinDeSeccion =
    {
        "Actividades Económicas", "Regímenes", "Obligaciones", "Sus datos personales", "Cadena Original", "Sello Digital"
    };

    private static readonly string[] Ruido =
    {
        "Contacto", "Av. Hidalgo", "Atención telefónica", "MarcaSAT", "desde cualquier parte", "Hidalgo 77", "Col. Guerrero"
    };

    /// <summary>
    /// Renglones de la tabla que sigue al título <paramref name="titulo"/>. Cada columna empieza
    /// donde está su encabezado; el límite real se ajusta al hueco vertical entre columnas.
    /// En la CIF cada renglón empieza en la línea que trae la fecha de inicio y los textos
    /// largos siguen en las líneas de abajo (incluso en la página siguiente).
    /// </summary>
    private static List<string[]> LeerTabla(List<Linea> lineas, string titulo, string[] encabezados, List<string> avisos)
    {
        int inicio = lineas.FindIndex(l => l.Texto.StartsWith(titulo, StringComparison.OrdinalIgnoreCase));

        if (inicio < 0)
        {
            // Sin obligaciones es normal (p. ej. solo sueldos y salarios).
            if (titulo != "Obligaciones")
                avisos.Add($"No se encontró la sección {titulo}.");

            return new();
        }

        int columnas = encabezados.Length;
        int columnaFecha = Array.IndexOf(encabezados, "Fecha");
        double[]? limites = null;
        var filas = new List<List<string>[]>();

        for (int i = inicio + 1; i < lineas.Count; i++)
        {
            Linea linea = lineas[i];
            string texto = linea.Texto;

            if (FinDeSeccion.Any(f => texto.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                break;

            if (Ruido.Any(r => texto.Contains(r, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Encabezado de la tabla (se repite si la tabla sigue en otra página).
            if (texto.Contains("Fecha Inicio", StringComparison.OrdinalIgnoreCase))
            {
                limites = Limites(linea, encabezados, lineas, i);
                continue;
            }

            if (limites is null)
                continue;

            var celdas = new List<string>[columnas];

            for (int c = 0; c < columnas; c++)
                celdas[c] = new List<string>();

            foreach (Word p in linea.Palabras)
                celdas[Math.Max(0, Array.FindLastIndex(limites, lim => p.BoundingBox.Left >= lim))].Add(p.Text);

            bool empiezaRenglon = celdas[columnaFecha].Count > 0 && Fecha.IsMatch(celdas[columnaFecha][0]);

            if (empiezaRenglon)
                filas.Add(celdas);
            else if (filas.Count > 0)
            {
                for (int c = 0; c < columnas; c++)
                    filas[^1][c].AddRange(celdas[c]);
            }
        }

        if (limites is null)
            avisos.Add($"No se encontró el encabezado de la tabla {titulo}.");
        else if (filas.Count == 0)
            avisos.Add($"No se pudieron leer los renglones de la tabla {titulo}.");

        return filas
            .Select(f => f.Select(c => string.Join(" ", c).Trim()).ToArray())
            .ToList();
    }

    /// <summary>Inicio (x) de cada columna.</summary>
    private static double[] Limites(Linea encabezado, string[] nombres, List<Linea> lineas, int indiceEncabezado)
    {
        var x = new double[nombres.Length];
        var ancho = new double[nombres.Length];
        int desde = 0;

        for (int c = 0; c < nombres.Length; c++)
        {
            int j = encabezado.Palabras.FindIndex(desde, p => Normalizar(p.Text).StartsWith(Normalizar(nombres[c])));

            if (j < 0)
            {
                // Sin la palabra esperada: reparte el resto del ancho.
                x[c] = c == 0 ? 0 : x[c - 1] + 60;
                continue;
            }

            x[c] = encabezado.Palabras[j].BoundingBox.Left;
            ancho[c] = encabezado.Palabras[j].BoundingBox.Width;
            desde = j + 1;
        }

        // Huecos sin texto en los renglones de la tabla (pueden seguir en la página siguiente,
        // con las mismas columnas).
        List<(double Izq, double Der)> ocupado = lineas
            .Skip(indiceEncabezado + 1)
            .TakeWhile(l => !FinDeSeccion.Any(f => l.Texto.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .Where(l => !Ruido.Any(r => l.Texto.Contains(r, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(l => l.Palabras)
            .Select(p => (p.BoundingBox.Left, p.BoundingBox.Right))
            .OrderBy(r => r.Left)
            .ToList();

        var huecos = new List<(double Izq, double Der)>();
        double fin = double.MinValue;

        foreach (var (izq, der) in ocupado)
        {
            // Entre columnas del SAT puede haber apenas 2 pt.
            if (fin != double.MinValue && izq - fin > 1)
                huecos.Add((fin, izq));

            fin = Math.Max(fin, der);
        }

        var limites = new double[nombres.Length];
        limites[0] = double.MinValue;

        for (int c = 1; c < nombres.Length; c++)
        {
            // El encabezado va centrado: el hueco puede quedar un poco antes o debajo de su primera palabra.
            // Cada límite queda a la derecha del anterior (un hueco no sirve para dos columnas).
            double izquierda = c == 1 ? x[0] : Math.Max(x[c - 1], limites[c - 1] + 1);
            double derecha = x[c] + Math.Min(ancho[c], 30);

            var candidato = huecos
                .Where(h => (h.Izq + h.Der) / 2 > izquierda && (h.Izq + h.Der) / 2 <= derecha)
                .OrderByDescending(h => h.Der - h.Izq)
                .FirstOrDefault();

            limites[c] = candidato == default ? x[c] - 2 : (candidato.Izq + candidato.Der) / 2;
        }

        return limites;
    }

    private static string ClaveRegimen(string nombre)
    {
        string n = Normalizar(nombre);
        return Regimenes.FirstOrDefault(r => r.Palabras.Any(n.Contains)).Clave ?? "";
    }

    private static string Normalizar(string texto) =>
        new string(texto.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
}
