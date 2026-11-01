using System.IO.Compression;
using System.Xml.Linq;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services;

/// <summary>
/// XML Azulejeras: a los emitidos a público general (XAXX010101000) de las series de mostrador se les cambia la
/// serie y el nombre del receptor según la sucursal (p. ej. 61A → 61AA "CLIENTE MOSTRADOR ATIZAPAN"). Se analiza
/// el periodo por serie, se generan las series elegidas (ZIP + copia en Storage\Azulejeras para subir a SINUBE).
/// </summary>
public sealed class XmlAzulejerasService
{
    private const string RfcPublicoGeneral =
        "XAXX010101000";

    private readonly PostgreSqlRepository _repo;

    public XmlAzulejerasService(
        PostgreSqlRepository repo)
    {
        _repo = repo;
    }

    private sealed record EquivalenciaSerie(
        string SerieNueva,
        string NombreReceptor);

    private static readonly Dictionary<string,
        Dictionary<string, EquivalenciaSerie>>
        Equivalencias =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["AAT040526RQ9"] =
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["61A"] =
                            new(
                                "61AA",
                                "CLIENTE MOSTRADOR ATIZAPAN"),

                        ["62A"] =
                            new(
                                "62AA",
                                "CLIENTE MOSTRADOR NICOLAS ROMERO"),

                        ["63E"] =
                            new(
                                "63EE",
                                "CLIENTE MOSTRADOR EXPO")
                    },

                ["ABM090330T23"] =
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["7A"] =
                            new(
                                "7AA",
                                "CLIENTE MOSTRADOR")
                    },

                ["AZU010730A22"] =
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["4A"] =
                            new(
                                "4AA",
                                "CLIENTE MOSTRADOR")
                    },

                ["AMA071204531"] =
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["72A"] =
                            new(
                                "72AA",
                                "CLIENTE MOSTRADOR CHICOLOAPAN"),

                        ["73A"] =
                            new(
                                "73AA",
                                "CLIENTE MOSTRADOR CUAUTITLAN"),

                        ["74A"] =
                            new(
                                "74AA",
                                "CLIENTE MOSTRADOR LOS REYES"),

                        ["75A"] =
                            new(
                                "75AA",
                                "CLIENTE MOSTRADOR ZUMPANGO"),

                        ["76E"] =
                            new(
                                "76EE",
                                "CLIENTE MOSTRADOR EXPO SAN VICENTE CHICOLOAPAN")
                    }
            };

    public static bool EsAzulejera(string rfc) => Equivalencias.ContainsKey(rfc);

    private Dictionary<string, EquivalenciaSerie> Reglas(EmpresaDto empresa) =>
        Equivalencias.TryGetValue(empresa.Rfc, out Dictionary<string, EquivalenciaSerie>? reglas)
            ? reglas
            : throw new InvalidOperationException($"No existen equivalencias configuradas para {empresa.Rfc}.");

    // BuscarCfdi solo acepta páginas de 25, 50 o 100 (otro valor lo deja en 25): se recorren todas.
    private const int TamanoPagina = 100;

    private List<CfdiConsultaDto> Emitidos(EmpresaDto empresa, DateTime fechaInicio, DateTime fechaFin)
    {
        var todos = new List<CfdiConsultaDto>();

        for (int pagina = 1; ; pagina++)
        {
            List<CfdiConsultaDto> registros = _repo.BuscarCfdi(new CfdiFiltroDto
            {
                EmpresaId = empresa.EmpresaId,
                FechaInicio = fechaInicio.Date,
                FechaFin = fechaFin.Date,
                Clasificacion = "EMITIDO",
                Pagina = pagina,
                RegistrosPorPagina = TamanoPagina,
                OrdenarPor = "fecha_emision",
                OrdenDescendente = false
            }).Registros;

            todos.AddRange(registros);

            if (registros.Count < TamanoPagina)
                return todos;
        }
    }

    /// <summary>Emitidos del periodo por serie: cuántos de cada tipo, cuántos a público general y a qué serie se convierten.</summary>
    public List<SerieAzulejeraDto> Analizar(EmpresaDto empresa, DateTime fechaInicio, DateTime fechaFin)
    {
        Dictionary<string, EquivalenciaSerie> reglas = Reglas(empresa);

        return Emitidos(empresa, fechaInicio, fechaFin)
            .GroupBy(c => c.Serie.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                reglas.TryGetValue(g.Key, out EquivalenciaSerie? regla);
                List<CfdiConsultaDto> publico = g.Where(EsPublicoGeneral).ToList();

                return new SerieAzulejeraDto
                {
                    Serie = g.Key,
                    PorTipo = g.GroupBy(c => c.TipoCfdi).ToDictionary(t => t.Key, t => t.Count()),
                    PublicoGeneral = publico.Count,
                    ImportePublicoGeneral = publico.Sum(c => c.Total),
                    SerieNueva = regla?.SerieNueva ?? "",
                    NombreReceptor = regla?.NombreReceptor ?? ""
                };
            })
            .OrderByDescending(s => s.TieneEquivalencia)
            .ThenBy(s => s.Serie, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // A público general y que no sea nómina: la nómina no se modifica ni se sube a SINUBE.
    private static bool EsPublicoGeneral(CfdiConsultaDto c) =>
        c.RfcReceptor.Equals(RfcPublicoGeneral, StringComparison.OrdinalIgnoreCase) &&
        !c.TipoCfdi.Equals("N", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Modifica los emitidos a público general de las <paramref name="series"/> elegidas (con equivalencia).
    /// Arma el ZIP para descargar y deja los XML en Storage\Azulejeras\RFC\fecha para subirlos a SINUBE.
    /// </summary>
    public XmlAzulejerasResultado GenerarZip(
        EmpresaDto empresa,
        DateTime fechaInicio,
        DateTime fechaFin,
        IReadOnlySet<string> series)
    {
        ArgumentNullException.ThrowIfNull(empresa);

        Dictionary<string, EquivalenciaSerie> reglas = Reglas(empresa);
        List<CfdiConsultaDto> cfdis = Emitidos(empresa, fechaInicio, fechaFin);

        string carpeta = CarpetaStorage.Ruta("Azulejeras", empresa.Rfc, DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
        Directory.CreateDirectory(carpeta);

        var resultado = new XmlAzulejerasResultado { Carpeta = carpeta };

        foreach (CfdiConsultaDto cfdi in cfdis)
        {
            resultado.Leidos++;
            resultado.ImporteTotal += cfdi.Total;

            // Solo público general de las series elegidas que tienen equivalencia.
            if (!EsPublicoGeneral(cfdi) ||
                !series.Contains(cfdi.Serie.Trim()) ||
                !reglas.TryGetValue(cfdi.Serie.Trim(), out EquivalenciaSerie? equivalencia))
            {
                resultado.SinCambios++;
                resultado.ImporteSinCambios += cfdi.Total;
                continue;
            }

            var detalle = new XmlAzulejeraDetalleDto
            {
                CfdiId = cfdi.CfdiId,
                FechaEmision = cfdi.FechaEmision,
                TipoCfdi = cfdi.TipoCfdi,
                UUID = cfdi.UUID,
                SerieOriginal = cfdi.Serie,
                SerieNueva = equivalencia.SerieNueva,
                NombreOriginal = cfdi.NombreReceptor,
                NombreNuevo = equivalencia.NombreReceptor,
                Total = cfdi.Total
            };

            try
            {
                // La ruta guardada puede ser de otro equipo o de otra ubicación de Storage.
                string ruta = RutaXmlLocal.Resolver(cfdi.RutaXml);

                if (!File.Exists(ruta))
                    throw new FileNotFoundException("No se encontró el XML en la bóveda.");

                XDocument xml = XDocument.Load(ruta, LoadOptions.PreserveWhitespace);
                XElement? comprobante = xml.Root is { Name.LocalName: "Comprobante" } raiz ? raiz : null;
                XElement? receptor = comprobante?.Elements().FirstOrDefault(x => x.Name.LocalName == "Receptor");

                if (comprobante is null || receptor is null)
                    throw new InvalidOperationException("El XML no es un CFDI válido.");

                comprobante.SetAttributeValue("Serie", equivalencia.SerieNueva);
                receptor.SetAttributeValue("Nombre", equivalencia.NombreReceptor);

                string archivo = Path.Combine(carpeta, $"{(cfdi.UUID != "" ? cfdi.UUID : cfdi.CfdiId.ToString())}.xml");
                xml.Save(archivo, SaveOptions.DisableFormatting);

                // Anticipos y otros conceptos sin NoIdentificacion: se suben a SINUBE con otro modo de producto.
                if (comprobante.Descendants().Any(x => x.Name.LocalName == "Concepto" &&
                                                       string.IsNullOrWhiteSpace((string?)x.Attribute("NoIdentificacion"))))
                    resultado.ArchivosSinNoIdentificacion.Add(archivo);

                resultado.Archivos.Add(archivo);
                resultado.Modificados++;
                resultado.ImporteModificados += cfdi.Total;
                detalle.Resultado = "MODIFICADO";
            }
            catch (Exception ex)
            {
                resultado.Errores++;
                detalle.Resultado = "ERROR: " + ex.Message;
            }

            resultado.Detalles.Add(detalle);
        }

        using (var memoria = new MemoryStream())
        {
            using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
            {
                foreach (string archivo in resultado.Archivos)
                    zip.CreateEntryFromFile(archivo, Path.GetFileName(archivo), CompressionLevel.Optimal);
            }

            resultado.Contenido = memoria.ToArray();
        }

        resultado.TipoContenido = "application/zip";
        resultado.NombreArchivo = $"XML_Azulejeras_{empresa.Rfc}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

        return resultado;
    }
}
