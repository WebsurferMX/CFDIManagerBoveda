using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using CFDIParser.Models;
using CFDIParser.Services;
using Microsoft.AspNetCore.Components.Forms;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Acciones del panel de la pantalla Administración de XML. Trabaja sobre los CFDI de
/// la empresa en PostgreSQL y sus XML en la ruta de la empresa; lo que se importa pasa
/// por <see cref="ImportacionCfdiService"/>, igual que la pantalla de Importaciones.
/// </summary>
public class AdministracionXmlService
{
    private static readonly Dictionary<string, string> Tipos = new()
    {
        ["I"] = "Factura",
        ["E"] = "NotaCredito",
        ["T"] = "Traslado",
        ["P"] = "Pago",
        ["N"] = "Nómina"
    };

    private readonly PostgreSqlRepository _repo;
    private readonly XmlCfdiParser _parser;
    private readonly ImportacionCfdiService _importacion;
    private readonly ConsultaEstadoSatService _estadoSat;
    private readonly RepresentacionImpresaService _pdf;
    private readonly IWebHostEnvironment _ambiente;

    public AdministracionXmlService(
        PostgreSqlRepository repo,
        XmlCfdiParser parser,
        ImportacionCfdiService importacion,
        ConsultaEstadoSatService estadoSat,
        RepresentacionImpresaService pdf,
        IWebHostEnvironment ambiente)
    {
        _repo = repo;
        _parser = parser;
        _importacion = importacion;
        _estadoSat = estadoSat;
        _pdf = pdf;
        _ambiente = ambiente;
    }

    private string Storage(params string[] partes) =>
        CarpetaStorage.Ruta(partes);

    // ---------- un CFDI ----------

    public AdminXmlFilaDto Obtener(EmpresaDto empresa, long cfdiId)
    {
        return _repo.ObtenerSeleccionAdministracionXml(new AdminXmlFiltroDto { EmpresaId = empresa.EmpresaId, Ids = [cfdiId] })
                   .FirstOrDefault()
               ?? throw new InvalidOperationException("El CFDI ya no existe.");
    }

    public string LeerXml(EmpresaDto empresa, long cfdiId)
    {
        string ruta = RutaExistente(Obtener(empresa, cfdiId));
        return File.ReadAllText(ruta);
    }

    public byte[] GenerarPdf(EmpresaDto empresa, long cfdiId)
    {
        return _pdf.Generar(_parser.Parse(RutaExistente(Obtener(empresa, cfdiId))));
    }

    public byte[] GenerarVistaPdf(EmpresaDto empresa, long cfdiId)
    {
        return _pdf.GenerarImagen(_parser.Parse(RutaExistente(Obtener(empresa, cfdiId))));
    }

    public void Localizar(EmpresaDto empresa, long cfdiId)
    {
        string ruta = RutaExistente(Obtener(empresa, cfdiId));
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ruta}\"") { UseShellExecute = false });
    }

    /// <summary>Abre en el Explorador una carpeta de Storage (reportes, PDF, renombrados).</summary>
    public void AbrirCarpeta(string carpeta)
    {
        string raiz = Path.GetFullPath(Storage()) + Path.DirectorySeparatorChar;
        string destino = Path.GetFullPath(carpeta);

        if (!destino.StartsWith(raiz, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(destino))
        {
            throw new InvalidOperationException("Carpeta no permitida.");
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{destino}\"") { UseShellExecute = false });
    }

    /// <summary>Nodos del complemento tal como vienen en el XML (pagos, nómina, impuestos locales…).</summary>
    public List<NodoXml> LeerComplementos(EmpresaDto empresa, long cfdiId)
    {
        XElement raiz = XElement.Load(RutaExistente(Obtener(empresa, cfdiId)));

        return raiz.Elements()
            .Where(e => e.Name.LocalName == "Complemento")
            .SelectMany(e => e.Elements())
            .Select(NodoXml.Desde)
            .ToList();
    }

    public List<ImpuestoLocalDto> LeerImpuestosLocales(EmpresaDto empresa, long cfdiId)
    {
        XElement raiz = XElement.Load(RutaExistente(Obtener(empresa, cfdiId)));

        XElement? locales = raiz.Descendants().FirstOrDefault(e => e.Name.LocalName == "ImpuestosLocales");

        if (locales is null)
        {
            return new();
        }

        decimal Num(XElement e, string a) =>
            decimal.TryParse((string?)e.Attribute(a), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) ? v : 0;

        return locales.Elements()
            .Select(e => e.Name.LocalName switch
            {
                "TrasladosLocales" => new ImpuestoLocalDto("Trasladado", (string?)e.Attribute("ImpLocTrasladado") ?? "", Num(e, "TasadeTraslado"), Num(e, "Importe")),
                "RetencionesLocales" => new ImpuestoLocalDto("Retenido", (string?)e.Attribute("ImpLocRetenido") ?? "", Num(e, "TasadeRetencion"), Num(e, "Importe")),
                _ => null
            })
            .OfType<ImpuestoLocalDto>()
            .ToList();
    }

    // ---------- Excel ----------

    /// <summary>"OCT 2026", "ENE-MAR 2026", "2026" o "SELECCIÓN" para los títulos del 80-20.</summary>
    private static string PeriodoTexto(AdminXmlFiltroDto f)
    {
        if (f.Ids.Count > 0 || f.Anio is not int anio)
            return f.Ids.Count > 0 ? "SELECCIÓN" : "TODOS LOS AÑOS";

        string Mes(int m) => System.Globalization.CultureInfo.GetCultureInfo("es-MX").DateTimeFormat.GetAbbreviatedMonthName(m).TrimEnd('.').ToUpperInvariant();

        List<int> meses = f.Meses.Order().ToList();

        return meses.Count switch
        {
            0 => $"{anio}",
            1 when !f.Acumular && f.Dias.Count > 0 => $"{Dias(f.Dias)} {Mes(meses[0])} {anio}",
            1 when !f.Acumular => $"{Mes(meses[0])} {anio}",
            1 => $"ENE-{Mes(meses[0])} {anio}",
            _ => $"{Mes(meses[0])}-{Mes(meses[^1])} {anio}"
        };
    }

    // "30", "28-30" si son seguidos, o "1, 5, 9".
    private static string Dias(List<int> dias)
    {
        List<int> orden = dias.Order().ToList();

        return orden.Count == 1 ? $"{orden[0]}"
            : orden[^1] - orden[0] == orden.Count - 1 ? $"{orden[0]}-{orden[^1]}"
            : string.Join(", ", orden);
    }

    public string NombreReporte(EmpresaDto empresa, AdminXmlFiltroDto f, string reporte)
    {
        string dir = f.Clasificacion == "RECIBIDO" ? "Recibidos" : "Emitidos";
        string periodo = f.Anio is int a ? $"{a}{(f.Meses.Count > 0 ? "_" + string.Join("-", f.Meses.Order().Select(m => m.ToString("00"))) : "")}" : "todo";

        if (f.Anio is not null && f.Meses.Count == 1 && !f.Acumular && f.Dias.Count > 0)
            periodo += "_d" + string.Join("-", f.Dias.Order().Select(d => d.ToString("00")));
        string tipo = reporte == "general" ? "" : "_" + reporte.Replace(':', '_');
        return $"{empresa.Rfc}_{dir}_{periodo}{tipo}.xlsx";
    }

    /// <summary>
    /// Excel del filtro y su nombre de archivo. El reporte general es el de MiBovedaCFDI (se lee de los XML):
    /// hojas Factura, NCredito, Traslado, Pago y Nomina y, para emitidos, Resumen Clientes y Dashboard.
    /// </summary>
    public (byte[] Bytes, string Nombre) GenerarExcel(EmpresaDto empresa, AdminXmlFiltroDto f, string reporte)
    {
        if (reporte == "pareto")
        {
            // El 80-20 usa facturas y notas de crédito del filtro sin importar la pestaña abierta.
            f.Pestana = "";
            bool clientes = f.Ids.Count > 0
                ? _repo.ObtenerSeleccionAdministracionXml(f).All(x => x.Clasificacion != "RECIBIDO")
                : f.Clasificacion != "RECIBIDO";

            byte[] bytes = Reporte8020.Crear(_repo.ObtenerSeleccionAdministracionXml(f), clientes, empresa.Rfc, PeriodoTexto(f));
            string nombre = $"{empresa.Rfc} 80-20 {(clientes ? "Clientes" : "Proveedores")} {PeriodoTexto(f)}.xlsx";

            return (bytes, nombre);
        }

        // Un solo mes (sin acumular) da "ENE 2026"; varios meses o acumulado, el año.
        int? mesFiltro = f.Ids.Count == 0 && f.Meses.Count == 1 && !f.Acumular ? f.Meses[0] : null;

        // Agrupado por receptor de emitidos y por emisor de recibidos: la hoja agrupada como tabla, con su dashboard
        // (ventas por receptor o cuentas por pagar) que lee solo de esa tabla.
        if (reporte is "agrupado:receptor" or "agrupado:emisor")
        {
            bool recibidos = reporte == "agrupado:emisor";
            List<AdminXmlFilaDto> seleccion = _repo.ObtenerSeleccionAdministracionXml(SinPestana(f));

            if (seleccion.Count > 0 && seleccion.All(x => (x.Clasificacion == "RECIBIDO") == recibidos))
            {
                var filtro = new ReporteGeneral.Filtro(empresa.Rfc, recibidos ? "R" : "E", f.Ids.Count > 0 ? null : f.Anio, mesFiltro);
                (byte[] agrupado, _) = recibidos ? ReporteGeneral.CrearPorEmisor(seleccion, filtro) : ReporteGeneral.CrearPorReceptor(seleccion, filtro);
                return (agrupado, NombreReporte(empresa, f, reporte));
            }
        }

        // Agrupado por mes: la hoja Por Mes como tabla con el dashboard de tendencia mensual.
        if (reporte == "agrupado:mes")
        {
            List<AdminXmlFilaDto> seleccion = _repo.ObtenerSeleccionAdministracionXml(SinPestana(f));
            bool recibidos = seleccion.Count > 0 && seleccion.All(x => x.Clasificacion == "RECIBIDO");

            if (seleccion.Count > 0 && (recibidos || seleccion.All(x => x.Clasificacion != "RECIBIDO")))
            {
                (byte[] porMes, _) = ReporteGeneral.CrearPorMes(
                    seleccion, new ReporteGeneral.Filtro(empresa.Rfc, recibidos ? "R" : "E", f.Ids.Count > 0 ? null : f.Anio, null), recibidos);
                return (porMes, NombreReporte(empresa, f, reporte));
            }
        }

        bool general = reporte == "general";

        if (!general && reporte != "conceptos" && !reporte.StartsWith("agrupado:"))
        {
            return (GenerarExcel(f, reporte), NombreReporte(empresa, f, reporte));
        }

        // Todos los reportes llevan al frente el dashboard (ventas por cliente en emitidos, cuentas por pagar en
        // recibidos) con las hojas Factura, NCredito y Pago de las que leen sus fórmulas. En los reportes que no son el
        // general el dashboard toma todo el filtro, sin importar la pestaña abierta.
        var entradas = new List<ReporteGeneral.Entrada>();

        foreach (AdminXmlFilaDto x in _repo.ObtenerSeleccionAdministracionXml(general ? f : SinPestana(f)))
        {
            XDocument xml;

            try
            {
                xml = XDocument.Load(x.RutaXml);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
            {
                // XML movido o borrado después de registrarse: no entra al reporte.
                continue;
            }

            if (xml.Root?.Name.LocalName != "Comprobante")
            {
                continue;
            }

            string estado = x.EstadoSat.ToUpperInvariant() is "" or "PENDIENTE" or "NO VALIDADO" ? "" : x.EstadoSat;
            entradas.Add(new ReporteGeneral.Entrada(
                new ReporteGeneral.Origen(empresa.Rfc, x.Clasificacion == "RECIBIDO" ? "R" : "E", x.RutaXml, x.FechaEmision),
                xml, estado));
        }

        // Un solo mes (sin acumular) da "ENE 2026"; varios meses o acumulado, el año.
        int? mes = f.Ids.Count == 0 && f.Meses.Count == 1 && !f.Acumular ? f.Meses[0] : null;
        string? direccion = f.Ids.Count > 0 ? null : f.Clasificacion == "RECIBIDO" ? "R" : "E";

        // Una selección con emitidos y recibidos mezclados no lleva dashboard.
        ReporteGeneral.Tablero tablero =
            entradas.Count > 0 && entradas.All(x => x.Fila.Direccion == "R") ? ReporteGeneral.Tablero.CuentasPorPagar
            : ReporteGeneral.Tablero.VentasCompacto;

        (byte[] libro, string archivo) = ReporteGeneral.Crear(
            entradas, new ReporteGeneral.Filtro(empresa.Rfc, direccion, f.Ids.Count > 0 ? null : f.Anio, mes), tablero,
            general ? null : hojas => AgregarHojas(hojas, f, reporte));

        return (libro, general ? archivo : NombreReporte(empresa, f, reporte));
    }

    /// <summary>reporte: general (una hoja por tipo), conceptos o agrupado:{emisor|receptor|mes|uso|metodo|tipo}.</summary>
    public byte[] GenerarExcel(AdminXmlFiltroDto f, string reporte)
    {
        using var libro = new XLWorkbook();
        AgregarHojas(libro, f, reporte);

        if (!libro.Worksheets.Any())
        {
            libro.AddWorksheet("Sin datos").Cell(1, 1).Value = "No hay CFDI para el filtro seleccionado";
        }

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return memoria.ToArray();
    }

    private static AdminXmlFiltroDto SinPestana(AdminXmlFiltroDto f) => new()
    {
        EmpresaId = f.EmpresaId, Clasificacion = f.Clasificacion, Anio = f.Anio, Meses = f.Meses, Dias = f.Dias,
        Acumular = f.Acumular, Busqueda = f.Busqueda, Filtrar = f.Filtrar, Ids = f.Ids
    };

    /// <summary>Hojas propias del reporte (conceptos, agrupado o una por tipo) en un libro existente.</summary>
    private void AgregarHojas(XLWorkbook libro, AdminXmlFiltroDto f, string reporte)
    {
        if (reporte.StartsWith("agrupado:"))
        {
            HojaAgrupada(libro, f, reporte["agrupado:".Length..]);
        }
        else if (reporte == "conceptos")
        {
            var renglones = new List<object?[]>();

            foreach (AdminXmlFilaDto x in _repo.ObtenerSeleccionAdministracionXml(f))
            {
                foreach (ConceptoDto k in _repo.ObtenerConceptosCfdi(f.EmpresaId, x.CfdiId))
                {
                    renglones.Add([x.Serie, x.Folio, x.Uuid, x.EstadoSat, Tipos.GetValueOrDefault(x.TipoCfdi, x.TipoCfdi), x.FechaEmision,
                        x.RfcEmisor, x.NombreEmisor, x.RfcReceptor, x.NombreReceptor,
                        k.ClaveProdServ, k.NoIdentificacion, k.Cantidad, k.ClaveUnidad, k.Unidad, k.Descripcion,
                        k.ValorUnitario, k.Descuento ?? 0, k.Importe,
                        k.Impuestos.Where(i => EsTraslado(i) && i.Impuesto == "002").Sum(i => i.Importe),
                        k.Impuestos.Where(i => !EsTraslado(i) && i.Impuesto == "002").Sum(i => i.Importe),
                        k.Impuestos.Where(i => !EsTraslado(i) && i.Impuesto == "001").Sum(i => i.Importe),
                        x.Moneda, x.RutaXml]);
                }
            }

            Hoja(libro, "Conceptos",
                ["Serie", "Folio", "UUID", "Estado SAT", "Tipo", "Fecha Emision", "RFC Emisor", "Nombre Emisor",
                 "RFC Receptor", "Nombre Receptor", "ClaveProdServ", "NoIdentificacion", "Cantidad", "ClaveUnidad", "Unidad", "Descripcion",
                 "ValorUnitario", "Descuento", "Importe", "IVA Trasladado", "IVA Retenido", "ISR Retenido", "Moneda", "Archivo XML"],
                [16, 17, 18, 19, 20, 21], renglones);
        }
        else
        {
            string[] columnas =
            [
                "Serie", "Folio", "UUID", "Estado SAT", "Tipo", "Fecha Emision", "Fecha Timbrado",
                "RFC Emisor", "Nombre Emisor", "RFC Receptor", "Nombre Receptor", "UsoCFDI", "SubTotal", "Descuento", "IVA",
                "IEPS", "Retenido IVA", "Retenido ISR", "Total", "Monto pagos", "Moneda", "Tipo De Cambio", "FormaDePago",
                "Metodo de Pago", "Archivo XML"
            ];

            List<AdminXmlFilaDto> filas = _repo.ObtenerSeleccionAdministracionXml(f);

            foreach (var (tipo, hoja) in new[] { ("I", "Factura"), ("E", "NCredito"), ("T", "Traslado"), ("P", "Pago"), ("N", "Nomina") })
            {
                List<AdminXmlFilaDto> deTipo = filas.Where(x => x.TipoCfdi == tipo).ToList();

                if (deTipo.Count == 0)
                {
                    continue;
                }

                Hoja(libro, hoja, columnas, [12, 13, 14, 15, 16, 17, 18, 19],
                    deTipo.Select(x => new object?[]
                    {
                        x.Serie, x.Folio, x.Uuid, x.EstadoSat, Tipos.GetValueOrDefault(x.TipoCfdi, x.TipoCfdi), x.FechaEmision,
                        x.FechaTimbrado, x.RfcEmisor, x.NombreEmisor, x.RfcReceptor, x.NombreReceptor,
                        x.UsoCfdi, x.Subtotal, x.Descuento, x.Iva, x.Ieps, x.RetIva, x.RetIsr, x.Total, x.MontoTotalPagos,
                        x.Moneda, x.TipoCambio, x.FormaPago, x.MetodoPago, x.RutaXml
                    }));
            }
        }
    }

    /// <summary>Guarda el reporte en Storage\Reportes\{RFC} y regresa la ruta con el contenido para descargarlo también.</summary>
    public (string Ruta, byte[] Contenido) GuardarExcel(EmpresaDto empresa, AdminXmlFiltroDto f, string reporte)
    {
        string carpeta = Storage("Reportes", empresa.Rfc);
        Directory.CreateDirectory(carpeta);
        (byte[] contenido, string nombre) = GenerarExcel(empresa, f, reporte);
        nombre = Limpiar(nombre);
        string ruta = Path.Combine(carpeta, nombre);

        // No sobrescribe un reporte guardado antes con el mismo nombre (puede tener cambios hechos a mano).
        for (int i = 2; File.Exists(ruta); i++)
        {
            ruta = Path.Combine(carpeta, $"{Path.GetFileNameWithoutExtension(nombre)} ({i}).xlsx");
        }

        File.WriteAllBytes(ruta, contenido);
        return (ruta, contenido);
    }

    /// <summary>
    /// Detalle de comprobantes agrupado con un renglón "Total ..." por grupo (esquema de Excel para contraer)
    /// y total general. Como la pantalla de Agrupaciones: facturas menos notas de crédito, sin importar la pestaña.
    /// </summary>
    private void HojaAgrupada(XLWorkbook libro, AdminXmlFiltroDto f, string por)
    {
        AdminXmlFiltroDto sinPestana = SinPestana(f);

        Func<AdminXmlFilaDto, string> clave = por switch
        {
            "receptor" => x => $"{x.RfcReceptor} {x.NombreReceptor}",
            "mes" => x => x.FechaEmision.ToString("yyyy-MM"),
            "uso" => x => x.UsoCfdi,
            "metodo" => x => x.MetodoPago,
            "tipo" => x => Tipos.GetValueOrDefault(x.TipoCfdi, x.TipoCfdi),
            _ => x => $"{x.RfcEmisor} {x.NombreEmisor}"
        };

        static decimal Signo(AdminXmlFilaDto x) => x.TipoCfdi == "E" ? -1 : 1;

        var grupos = _repo.ObtenerSeleccionAdministracionXml(sinPestana)
            .Where(x => x.TipoCfdi is "I" or "E")
            .GroupBy(clave)
            .OrderByDescending(g => g.Sum(x => Signo(x) * x.Total))
            .ToList();

        string titulo = por switch
        {
            "emisor" => "Emisor", "receptor" => "Receptor", "mes" => "Mes", "uso" => "Uso CFDI",
            "metodo" => "Método de pago", _ => "Tipo"
        };

        IXLWorksheet ws = libro.AddWorksheet($"Por {titulo}");
        string[] columnas =
        [
            titulo, "Serie", "Folio", "UUID", "Tipo", "Fecha Emision", "RFC Emisor", "Nombre Emisor",
            "RFC Receptor", "Nombre Receptor", "UsoCFDI", "Metodo de Pago", "SubTotal", "IVA", "Total"
        ];
        int[] dinero = [13, 14, 15];

        for (int c = 0; c < columnas.Length; c++)
        {
            ws.Cell(1, c + 1).Value = columnas[c];
        }

        ws.Row(1).Style.Font.Bold = true;

        int r = 2;

        foreach (var g in grupos)
        {
            int inicio = r;

            foreach (AdminXmlFilaDto x in g.OrderBy(x => x.FechaEmision))
            {
                object?[] valores =
                [
                    g.Key, x.Serie, x.Folio, x.Uuid, Tipos.GetValueOrDefault(x.TipoCfdi, x.TipoCfdi), x.FechaEmision,
                    x.RfcEmisor, x.NombreEmisor, x.RfcReceptor, x.NombreReceptor, x.UsoCfdi, x.MetodoPago,
                    Signo(x) * x.Subtotal, Signo(x) * x.Iva, Signo(x) * x.Total
                ];

                for (int c = 0; c < valores.Length; c++)
                {
                    ws.Cell(r, c + 1).Value = valores[c] switch
                    {
                        decimal d => d,
                        DateTime dt => dt,
                        _ => XLCellValue.FromObject(valores[c])
                    };
                }

                r++;
            }

            ws.Rows(inicio, r - 1).Group();
            ws.Cell(r, 1).Value = $"Total {g.Key}";
            ws.Cell(r, 2).FormulaA1 = $"SUBTOTAL(3,D{inicio}:D{r - 1})";

            foreach (int c in dinero)
            {
                string letra = ws.Column(c + 1).ColumnLetter();
                ws.Cell(r, c + 1).FormulaA1 = $"SUBTOTAL(9,{letra}{inicio}:{letra}{r - 1})";
            }

            ws.Row(r).Style.Font.Bold = true;
            r++;
        }

        if (r > 2)
        {
            ws.Rows(2, r - 1).Group();
        }

        // SUBTOTAL ignora los subtotales de cada grupo, así que el total general no los suma dos veces.
        ws.Cell(r, 1).Value = "Total general";
        ws.Row(r).Style.Font.Bold = true;

        if (r > 2)
        {
            ws.Cell(r, 2).FormulaA1 = $"SUBTOTAL(3,D2:D{r - 1})";

            foreach (int c in dinero)
            {
                string letra = ws.Column(c + 1).ColumnLetter();
                ws.Cell(r, c + 1).FormulaA1 = $"SUBTOTAL(9,{letra}2:{letra}{r - 1})";
            }
        }

        foreach (int c in dinero)
        {
            ws.Column(c + 1).Style.NumberFormat.Format = "#,##0.00";
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200), 8, 50);
    }

    private static bool EsTraslado(ImpuestoDto i) => i.Tipo.StartsWith("TRAS", StringComparison.OrdinalIgnoreCase);

    /// <summary>Hoja con encabezado fijo y un renglón final con sumas en las columnas de importe (índices base 0).</summary>
    private static void Hoja(XLWorkbook libro, string nombre, string[] columnas, int[] dinero, IEnumerable<object?[]> renglones)
    {
        IXLWorksheet ws = libro.AddWorksheet(nombre);

        for (int c = 0; c < columnas.Length; c++)
        {
            ws.Cell(1, c + 1).Value = columnas[c];
        }

        ws.Row(1).Style.Font.Bold = true;

        int r = 2;

        foreach (object?[] valores in renglones)
        {
            for (int c = 0; c < valores.Length; c++)
            {
                ws.Cell(r, c + 1).Value = valores[c] switch
                {
                    null => Blank.Value,
                    decimal d => d,
                    int i => i,
                    DateTime dt => dt,
                    _ => XLCellValue.FromObject(valores[c])
                };
            }

            r++;
        }

        ws.Cell(r, 1).Value = "Totales";
        ws.Row(r).Style.Font.Bold = true;

        foreach (int c in dinero)
        {
            string letra = ws.Column(c + 1).ColumnLetter();
            ws.Column(c + 1).Style.NumberFormat.Format = "#,##0.00";

            if (r > 2)
            {
                ws.Cell(r, c + 1).FormulaA1 = $"SUM({letra}2:{letra}{r - 1})";
            }
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200), 8, 50);
    }

    // ---------- procesos masivos ----------

    /// <summary>XML del filtro en un ZIP que se queda guardado en Storage\ZIP\{RFC} (copia en la bóveda).</summary>
    public Task<ResultadoProcesoXml> ZipXmlAsync(EmpresaDto empresa, AdminXmlFiltroDto f, IProgress<ResultadoProcesoXml>? progreso, CancellationToken ct)
    {
        string carpeta = Storage("ZIP", empresa.Rfc);
        string nombre = Path.GetFileNameWithoutExtension(NombreReporte(empresa, f, "general")) + "_XML.zip";
        string ruta = Path.Combine(carpeta, nombre);

        // No sobrescribe un ZIP guardado antes con el mismo nombre.
        for (int i = 2; File.Exists(ruta); i++)
        {
            ruta = Path.Combine(carpeta, $"{Path.GetFileNameWithoutExtension(nombre)} ({i}).zip");
        }

        return ArmarZipAsync(f, ruta, Path.GetFileName(ruta), carpeta, ".xml", x => File.ReadAllBytes(RutaExistente(x)), progreso, ct);
    }

    /// <summary>PDF del filtro generados al vuelo en un ZIP temporal: no se guarda, se borra al descargarlo.</summary>
    public Task<ResultadoProcesoXml> ZipPdfAsync(EmpresaDto empresa, AdminXmlFiltroDto f, IProgress<ResultadoProcesoXml>? progreso, CancellationToken ct)
    {
        string ruta = Path.Combine(DescargasTemporales.CarpetaTemporal, $"{Guid.NewGuid():N}.zip");
        string nombre = Path.GetFileNameWithoutExtension(NombreReporte(empresa, f, "general")) + "_PDF.zip";

        return ArmarZipAsync(f, ruta, nombre, "", ".pdf", x => _pdf.Generar(_parser.Parse(RutaExistente(x))), progreso, ct);
    }

    /// <summary>
    /// Un archivo por CFDI del filtro (los UUID repetidos entran una vez), llamado {UUID}{extension} o como diga
    /// <paramref name="nombreEntrada"/> (con "_2", "_3"... si se repite). Si se cancela, no deja el ZIP a medias.
    /// </summary>
    private Task<ResultadoProcesoXml> ArmarZipAsync(AdminXmlFiltroDto f, string ruta, string nombre, string carpeta, string extension,
        Func<AdminXmlFilaDto, byte[]> contenido, IProgress<ResultadoProcesoXml>? progreso, CancellationToken ct,
        Func<AdminXmlFilaDto, string>? nombreEntrada = null)
    {
        return Task.Run(() =>
        {
            List<AdminXmlFilaDto> filas = _repo.ObtenerSeleccionAdministracionXml(f);
            var r = new ResultadoProcesoXml { Total = filas.Count, Carpeta = carpeta, Archivo = ruta, NombreArchivo = nombre };
            var agregados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);

            try
            {
                using (var zip = ZipFile.Open(ruta, ZipArchiveMode.Create))
                {
                    foreach (AdminXmlFilaDto x in filas)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (agregados.Add(x.Uuid))
                        {
                            try
                            {
                                byte[] bytes = contenido(x);
                                string baseEntrada = nombreEntrada?.Invoke(x) ?? x.Uuid;
                                string entradaNombre = baseEntrada + extension;

                                for (int i = 2; !nombres.Add(entradaNombre); i++)
                                {
                                    entradaNombre = $"{baseEntrada}_{i}{extension}";
                                }

                                using Stream entrada = zip.CreateEntry(entradaNombre, CompressionLevel.Fastest).Open();
                                entrada.Write(bytes);
                                r.Correctos++;
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                r.Errores++;
                            }
                        }

                        r.Hechos++;
                        progreso?.Report(r);
                    }
                }

                if (r.Correctos == 0)
                {
                    File.Delete(ruta);
                    r.Archivo = "";
                }
            }
            catch
            {
                File.Delete(ruta);
                throw;
            }

            return r;
        }, ct);
    }

    /// <summary>Campos que se pueden usar en el nombre del archivo (Renombrar XML's), en el orden del asistente.</summary>
    public static readonly string[] CamposNombre =
        ["UUID", "Serie", "Folio", "FechaEmision", "RFCEmisor", "RFCReceptor", "Total", "EstadoSAT", "TipoComprobante", "SubContRfcLabora"];

    /// <summary>
    /// Nombre del archivo (sin .xml) con los campos en el orden elegido. Los vacíos se omiten.
    /// SubContRfcLabora sale del complemento de nómina, por eso se lee del XML solo si se usa.
    /// </summary>
    public string NombreArchivo(AdminXmlFilaDto x, IReadOnlyList<string> campos, string separador)
    {
        string nombre = string.Join(separador, campos.Select(c => Limpiar(Valor(x, c))).Where(v => v != ""));
        return nombre == "" ? x.Uuid : nombre;
    }

    private string Valor(AdminXmlFilaDto x, string campo) => campo switch
    {
        "UUID" => x.Uuid,
        "Serie" => x.Serie,
        "Folio" => x.Folio,
        "FechaEmision" => x.FechaEmision.ToString("yyyy-MM-dd"),
        "RFCEmisor" => x.RfcEmisor,
        "RFCReceptor" => x.RfcReceptor,
        "Total" => x.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        "EstadoSAT" => x.EstadoSat,
        "TipoComprobante" => x.TipoCfdi,
        "SubContRfcLabora" => RfcLaboraSubcontratacion(x),
        _ => ""
    };

    // nomina12:Receptor/nomina12:SubContratacion/@RfcLabora; si hay varias, la primera.
    private string RfcLaboraSubcontratacion(AdminXmlFilaDto x)
    {
        try
        {
            return System.Xml.Linq.XDocument.Load(RutaExistente(x))
                .Descendants().FirstOrDefault(e => e.Name.LocalName == "SubContratacion")
                ?.Attribute("RfcLabora")?.Value ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// Copias de los XML con el nombre de la estructura elegida, en un ZIP temporal (Emitidos\ y Recibidos\) que se
    /// descarga en el navegador y se borra: así llegan al equipo del usuario aunque trabaje remoto. Los originales no se tocan.
    /// </summary>
    public Task<ResultadoProcesoXml> RenombrarAsync(EmpresaDto empresa, AdminXmlFiltroDto f, IReadOnlyList<string> campos, string separador, IProgress<ResultadoProcesoXml>? progreso, CancellationToken ct)
    {
        string ruta = Path.Combine(DescargasTemporales.CarpetaTemporal, $"{Guid.NewGuid():N}.zip");
        string nombre = Path.GetFileNameWithoutExtension(NombreReporte(empresa, f, "general")) + "_Renombrados.zip";

        return ArmarZipAsync(f, ruta, nombre, "", ".xml", x => File.ReadAllBytes(RutaExistente(x)), progreso, ct,
            x => $"{(x.Clasificacion == "RECIBIDO" ? "Recibidos" : "Emitidos")}/{NombreArchivo(x, campos, separador)}");
    }

    private const int ConsultasSatSimultaneas = 3;

    /// <summary>Orden en que se valida: facturas primero, nómina al final.</summary>
    public static readonly string[] OrdenTiposSat = ["I", "E", "T", "P", "N"];

    /// <summary>
    /// Consulta pública del SAT (no requiere e.firma) y guarda el estado en la tabla cfdi. Hace varias consultas
    /// a la vez; cada respuesta se deja en <see cref="ResultadoProcesoXml.Estados"/> para pintarla al momento.
    /// </summary>
    public async Task<ResultadoProcesoXml> ValidarEstadoSatAsync(EmpresaDto empresa, IReadOnlyList<AdminXmlFilaDto> filas, IProgress<ResultadoProcesoXml>? progreso, CancellationToken ct)
    {
        List<AdminXmlFilaDto> ordenadas = filas
            .OrderBy(x => Array.IndexOf(OrdenTiposSat, x.TipoCfdi) is int i and >= 0 ? i : OrdenTiposSat.Length)
            .ToList();

        var r = new ResultadoProcesoXml { Total = ordenadas.Count };

        foreach (var g in ordenadas.GroupBy(x => x.TipoCfdi))
        {
            r.PorTipo[g.Key] = (0, g.Count());
        }

        // Se bloquea sobre el propio resultado: la pantalla toma el mismo candado al leer el avance.
        object candado = r;

        await Parallel.ForEachAsync(ordenadas, new ParallelOptions { MaxDegreeOfParallelism = ConsultasSatSimultaneas, CancellationToken = ct }, async (x, token) =>
        {
            string? nuevo = null;

            try
            {
                EstadoSatDto estado = await _estadoSat.ConsultarAsync(x.Uuid, x.RfcEmisor, x.RfcReceptor, x.Total, token);

                if (estado.ConsultaExitosa)
                {
                    _repo.ActualizarEstadoSat(empresa.EmpresaId, x.CfdiId, estado);
                    nuevo = estado.Estado;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
            {
                // Un CFDI que no responde cuenta como error; los demás siguen.
            }

            lock (candado)
            {
                if (nuevo is null)
                {
                    r.Errores++;
                }
                else
                {
                    r.Correctos++;
                    r.Detalle[nuevo] = r.Detalle.GetValueOrDefault(nuevo) + 1;
                    r.Estados.Enqueue((x.CfdiId, nuevo));
                }

                var (hechos, total) = r.PorTipo[x.TipoCfdi];
                r.PorTipo[x.TipoCfdi] = (hechos + 1, total);
                r.Hechos++;
            }

            progreso?.Report(r);
        });

        return r;
    }

    /// <summary>
    /// Importa todos los XML de una carpeta (y subcarpetas), por ejemplo la bóveda de AdminXML
    /// de la empresa. Cada XML se copia a la ruta de la empresa y se registra en la base.
    /// </summary>
    public Task<ResultadoProcesoXml> ImportarCarpetaAsync(EmpresaDto empresa, string carpeta, IProgress<ResultadoProcesoXml>? progreso, CancellationToken ct)
    {
        if (!Directory.Exists(carpeta))
        {
            throw new InvalidOperationException($"No existe la carpeta {carpeta}.");
        }

        return Task.Run(() =>
        {
            List<string> archivos = Directory.EnumerateFiles(carpeta, "*.xml", SearchOption.AllDirectories).ToList();
            var r = new ResultadoProcesoXml { Total = archivos.Count };

            foreach (string archivo in archivos)
            {
                ct.ThrowIfCancellationRequested();
                Contar(r, _importacion.Importar(empresa, archivo, Path.GetFileName(archivo)));
                r.Hechos++;

                if (r.Hechos % 20 == 0 || r.Hechos == r.Total)
                {
                    progreso?.Report(r);
                }
            }

            return r;
        }, ct);
    }

    public async Task<ResultadoProcesoXml> ImportarArchivosAsync(EmpresaDto empresa, IReadOnlyList<IBrowserFile> archivos, IProgress<ResultadoProcesoXml>? progreso, CancellationToken ct)
    {
        string temporal = Storage("Temporal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporal);
        var r = new ResultadoProcesoXml { Total = archivos.Count };

        try
        {
            foreach (IBrowserFile archivo in archivos)
            {
                ct.ThrowIfCancellationRequested();
                string ruta = Path.Combine(temporal, Guid.NewGuid().ToString("N") + ".xml");

                await using (FileStream destino = File.Create(ruta))
                {
                    await archivo.OpenReadStream(25L * 1024 * 1024, ct).CopyToAsync(destino, ct);
                }

                Contar(r, _importacion.Importar(empresa, ruta, archivo.Name));
                File.Delete(ruta);
                r.Hechos++;
                progreso?.Report(r);
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

        return r;
    }

    private static void Contar(ResultadoProcesoXml r, ResultadoImportacionCfdi i)
    {
        if (i.Estatus is "CORRECTO" or "ACTUALIZADO")
        {
            r.Correctos++;
        }
        else
        {
            r.Errores++;
        }

        r.Detalle[i.Estatus] = r.Detalle.GetValueOrDefault(i.Estatus) + 1;
    }

    private static string RutaExistente(AdminXmlFilaDto x)
    {
        if (string.IsNullOrWhiteSpace(x.RutaXml) || !File.Exists(x.RutaXml))
        {
            throw new InvalidOperationException($"No se encontró el XML de {x.Uuid} en {x.RutaXml}.");
        }

        return x.RutaXml;
    }

    private static string Limpiar(string? valor)
    {
        string texto = (valor ?? "").Trim();

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            texto = texto.Replace(c, '_');
        }

        return texto;
    }
}

public sealed class ResultadoProcesoXml
{
    public int Total { get; set; }
    public int Hechos { get; set; }
    public int Correctos { get; set; }
    public int Errores { get; set; }
    public string Carpeta { get; set; } = "";

    /// <summary>Archivo que armó el proceso (ZIP) y el nombre con que se descarga; vacío si no armó nada.</summary>
    public string Archivo { get; set; } = "";
    public string NombreArchivo { get; set; } = "";

    public Dictionary<string, int> Detalle { get; } = new();

    /// <summary>Avance por tipo de comprobante (I, E, T, P, N): hechos de total.</summary>
    public Dictionary<string, (int Hechos, int Total)> PorTipo { get; } = new();

    /// <summary>Estados SAT recién consultados que la pantalla aún no pinta.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<(long CfdiId, string Estado)> Estados { get; } = new();
}

public sealed record ImpuestoLocalDto(string Tipo, string Nombre, decimal Tasa, decimal Importe);

/// <summary>Nodo XML genérico para mostrar complementos sin interpretarlos.</summary>
public sealed class NodoXml
{
    public string Nombre { get; init; } = "";
    public List<KeyValuePair<string, string>> Atributos { get; init; } = new();
    public List<NodoXml> Hijos { get; init; } = new();

    public static NodoXml Desde(XElement e) => new()
    {
        Nombre = e.Name.LocalName,
        Atributos = e.Attributes()
            .Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "schemaLocation")
            .Select(a => new KeyValuePair<string, string>(a.Name.LocalName, a.Value))
            .ToList(),
        Hijos = e.Elements().Select(Desde).ToList()
    };
}
