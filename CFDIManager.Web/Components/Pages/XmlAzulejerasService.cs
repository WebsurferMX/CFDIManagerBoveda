using System.IO.Compression;
using System.Xml.Linq;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services;

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

    public XmlAzulejerasResultado GenerarZip(
        EmpresaDto empresa,
        DateTime fechaInicio,
        DateTime fechaFin)
    {
        ArgumentNullException.ThrowIfNull(
            empresa);

        if (!Equivalencias.TryGetValue(
                empresa.Rfc,
                out Dictionary<string,
                    EquivalenciaSerie>? reglas))
        {
            throw new InvalidOperationException(
                $"No existen equivalencias configuradas para {empresa.Rfc}.");
        }

        var filtro =
            new CfdiFiltroDto
            {
                EmpresaId =
                    empresa.EmpresaId,

                FechaInicio =
                    fechaInicio.Date,

                FechaFin =
                    fechaFin.Date,

                Clasificacion =
                    "EMITIDO",

                Pagina = 1,

                RegistrosPorPagina = 50000,

                OrdenarPor =
                    "fecha_emision",

                OrdenDescendente =
                    false
            };

        List<CfdiConsultaDto> cfdis =
            _repo
                .BuscarCfdi(filtro)
                .Registros;

        var resultado =
            new XmlAzulejerasResultado();

        using var memoria =
            new MemoryStream();

        using (var zip =
            new ZipArchive(
                memoria,
                ZipArchiveMode.Create,
                true))
        {
            foreach (CfdiConsultaDto cfdi in cfdis)
            {
                resultado.Leidos++;
                try
                {
                    if (string.IsNullOrWhiteSpace(
                            cfdi.RutaXml))
                    {
                        resultado.SinCambios++;
                        continue;
                    }

                    if (!File.Exists(
                            cfdi.RutaXml))
                    {
                        resultado.Errores++;
                        continue;
                    }

                    XDocument xml =
                        XDocument.Load(
                            cfdi.RutaXml,
                            LoadOptions.PreserveWhitespace);

                    XElement? comprobante =
                        xml.Descendants()
                           .FirstOrDefault(
                                x =>
                                    x.Name.LocalName ==
                                    "Comprobante");

                    XElement? receptor =
                        xml.Descendants()
                           .FirstOrDefault(
                                x =>
                                    x.Name.LocalName ==
                                    "Receptor");

                    if (comprobante is null ||
                        receptor is null)
                    {
                        resultado.SinCambios++;
                        continue;
                    }

                    string rfcReceptor =
                        receptor
                            .Attribute("Rfc")
                            ?.Value
                        ?? "";

                    if (!rfcReceptor.Equals(
                            RfcPublicoGeneral,
                            StringComparison
                                .OrdinalIgnoreCase))
                    {
                        resultado.SinCambios++;
                        continue;
                    }

                    string serieActual =
                        comprobante
                            .Attribute("Serie")
                            ?.Value
                        ?? "";

                    if (!reglas.TryGetValue(
                            serieActual,
                            out EquivalenciaSerie?
                                equivalencia))
                    {
                        resultado.SinCambios++;
                        continue;
                    }

                    comprobante.SetAttributeValue(
                        "Serie",
                        equivalencia.SerieNueva);

                    receptor.SetAttributeValue(
                        "Nombre",
                        equivalencia.NombreReceptor);

                    string nombreArchivo =
                        string.IsNullOrWhiteSpace(
                            cfdi.UUID)
                            ? $"{cfdi.CfdiId}.xml"
                            : $"{cfdi.UUID}.xml";

                    ZipArchiveEntry entry =
                        zip.CreateEntry(
                            nombreArchivo,
                            CompressionLevel.Optimal);

                    using Stream stream =
                        entry.Open();

                    xml.Save(stream);

                    resultado.Modificados++;
                }
                catch
                {
                    resultado.Errores++;
                }
            }
        }

        resultado.Contenido =
            memoria.ToArray();

        resultado.TipoContenido =
            "application/zip";

        resultado.NombreArchivo =
            $"XML_Azulejeras_" +
            $"{empresa.Rfc}_" +
            $"{DateTime.Now:yyyyMMdd_HHmmss}.zip";

                    resultado.NombreArchivo =
            $"XML_Azulejeras_" +
            $"{empresa.Rfc}_" +
            $"{DateTime.Now:yyyyMMdd_HHmmss}.zip";

        return resultado;
}
  
    }
 
   

