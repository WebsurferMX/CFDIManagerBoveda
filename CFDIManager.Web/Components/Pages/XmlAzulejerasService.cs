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

    /// <param name="SerieNueva">Null: la serie no cambia, solo el nombre del receptor.</param>
    private sealed record EquivalenciaSerie(
        string? SerieNueva,
        string NombreReceptor);

    // Regla para todas las series de la empresa que no tengan una propia.
    private const string TodasLasSeries = "*";

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
                                "CLIENTE MOSTRADOR"),

                        // Conserva la serie (así está en SINUBE); solo público general cambia de nombre.
                        ["50E"] =
                            new(
                                null,
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
                    },

                // Conserva la serie; solo el nombre del receptor a público general.
                ["ASN260505492"] =
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        [TodasLasSeries] =
                            new(
                                null,
                                "CLIENTE MOSTRADOR")
                    }
            };

    public static bool EsAzulejera(string rfc) => Equivalencias.ContainsKey(rfc);

    /// <summary>Regla de la serie (o la de todas las series) con la serie destino ya resuelta.</summary>
    private static EquivalenciaSerie? Regla(Dictionary<string, EquivalenciaSerie> reglas, string serie) =>
        (reglas.GetValueOrDefault(serie) ?? reglas.GetValueOrDefault(TodasLasSeries)) is { } regla
            ? regla with { SerieNueva = regla.SerieNueva ?? serie }
            : null;

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
                EquivalenciaSerie? regla = Regla(reglas, g.Key);
                List<CfdiConsultaDto> publico = g.Where(EsPublicoGeneral).ToList();
                List<CfdiConsultaDto> conRfc = regla is not null && IncluyeConRfc(reglas, regla, g.Key)
                    ? g.Where(c => !EsPublicoGeneral(c) && !EsNomina(c)).ToList()
                    : [];

                return new SerieAzulejeraDto
                {
                    Serie = g.Key,
                    PorTipo = g.GroupBy(c => c.TipoCfdi).ToDictionary(t => t.Key, t => t.Count()),
                    PublicoGeneral = publico.Count,
                    ImportePublicoGeneral = publico.Sum(c => c.Total),
                    ConRfc = conRfc.Count,
                    ImporteConRfc = conRfc.Sum(c => c.Total),
                    SerieNueva = regla?.SerieNueva ?? "",
                    NombreReceptor = regla?.NombreReceptor ?? ""
                };
            })
            .OrderByDescending(s => s.TieneEquivalencia)
            .ThenBy(s => s.Serie, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool EsNomina(CfdiConsultaDto c) => c.TipoCfdi.Equals("N", StringComparison.OrdinalIgnoreCase);

    // Clientes con RFC de la serie también se suben desde aquí (con la serie convertida si cambia, p. ej. 72A → 72AA,
    // o tal cual, p. ej. 50E) cuando la serie tiene regla propia; la regla general "*" solo toca a público general.
    private static bool IncluyeConRfc(Dictionary<string, EquivalenciaSerie> reglas, EquivalenciaSerie regla, string serie) =>
        reglas.ContainsKey(serie) ||
        !string.Equals(regla.SerieNueva, serie, StringComparison.OrdinalIgnoreCase);

    // A público general y que no sea nómina: la nómina no se modifica ni se sube a SINUBE.
    private static bool EsPublicoGeneral(CfdiConsultaDto c) =>
        c.RfcReceptor.Equals(RfcPublicoGeneral, StringComparison.OrdinalIgnoreCase) &&
        !c.TipoCfdi.Equals("N", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Modifica los emitidos de las <paramref name="series"/> elegidas (con equivalencia) y deja los XML en
    /// Storage\Azulejeras\RFC\fecha para subirlos a SINUBE desde la pantalla (ya no se descarga ZIP).
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

            // Series elegidas con equivalencia: a público general cambian serie y nombre del receptor; a clientes con
            // RFC solo la serie (en SINUBE toda la serie de la sucursal va con la serie convertida, p. ej. 72AA).
            bool publico = EsPublicoGeneral(cfdi);

            if (EsNomina(cfdi) ||
                !series.Contains(cfdi.Serie.Trim()) ||
                Regla(reglas, cfdi.Serie.Trim()) is not { } equivalencia ||
                (!publico && !IncluyeConRfc(reglas, equivalencia, cfdi.Serie.Trim())))
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
                SerieNueva = equivalencia.SerieNueva ?? cfdi.Serie,
                NombreOriginal = cfdi.NombreReceptor,
                NombreNuevo = publico ? equivalencia.NombreReceptor : cfdi.NombreReceptor,
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
                if (publico)
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

        return resultado;
    }
}
