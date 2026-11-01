using Microsoft.AspNetCore.Components.Forms;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services;

public class ImportacionXmlService
{
    private const long TamanoMaximoArchivo =
        25L * 1024L * 1024L;

    private readonly PostgreSqlRepository _repo;
    private readonly XmlCfdiParser _parser;
    private readonly IWebHostEnvironment _entorno;

    public ImportacionXmlService(
        PostgreSqlRepository repo,
        XmlCfdiParser parser,
        IWebHostEnvironment entorno)
    {
        _repo =
            repo;

        _parser =
            parser;

        _entorno =
            entorno;
    }

    public async Task<ImportacionWebResultadoDto>
        ImportarArchivoAsync(
            IBrowserFile archivo,
            EmpresaDto empresa,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentNullException.ThrowIfNull(
            archivo);

        ArgumentNullException.ThrowIfNull(
            empresa);

        var resultado =
            new ImportacionWebResultadoDto
            {
                Archivo =
                    archivo.Name
            };

        string? rutaTemporal =
            null;

        try
        {
            ValidarArchivo(
                archivo);

            if (empresa.EmpresaId <= 0)
            {
                throw new InvalidOperationException(
                    "La empresa seleccionada no es válida.");
            }

            if (string.IsNullOrWhiteSpace(
                    empresa.Rfc))
            {
                throw new InvalidOperationException(
                    "La empresa seleccionada no tiene RFC.");
            }

            string carpetaTemporal =
                CarpetaStorage.Ruta(
                    "Temporal");

            Directory.CreateDirectory(
                carpetaTemporal);

            rutaTemporal =
                Path.Combine(
                    carpetaTemporal,
                    $"{Guid.NewGuid():N}.xml");

            await using (
                Stream origen =
                    archivo.OpenReadStream(
                        TamanoMaximoArchivo,
                        cancellationToken))
            {
                await using var destino =
                    new FileStream(
                        rutaTemporal,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        useAsync: true);

                await origen.CopyToAsync(
                    destino,
                    cancellationToken);
            }

            CfdiDto cfdi =
                _parser.Parse(
                    rutaTemporal);

            /*
             * Esta es la única parte donde se utiliza cfdi.UUID.
             * Se convierte a una cadena no nula y se guarda
             * en resultado.UUID.
             */
            resultado.UUID =
                cfdi.UUID ?? "";

            if (string.IsNullOrWhiteSpace(
                    resultado.UUID))
            {
                throw new InvalidOperationException(
                    "El XML no contiene un UUID válido.");
            }

            string clasificacion =
                DeterminarClasificacion(
                    empresa.Rfc,
                    cfdi.RFCEmisor,
                    cfdi.RFCReceptor);

            resultado.Clasificacion =
                clasificacion;

            string nombreCarpeta =
                ObtenerNombreCarpeta(
                    clasificacion);

            string rutaBase =
                ResolverRutaEmpresa(
                    empresa);

            string carpetaDestino =
                Path.Combine(
                    rutaBase,
                    nombreCarpeta);

            Directory.CreateDirectory(
                carpetaDestino);

            string nombreSeguro =
                ObtenerNombreSeguro(
                    archivo.Name,
                    resultado.UUID);

            string rutaDestino =
                ObtenerRutaDisponible(
                    Path.Combine(
                        carpetaDestino,
                        nombreSeguro),
                    resultado.UUID);

            File.Move(
                rutaTemporal,
                rutaDestino);

            rutaTemporal =
                null;

            resultado.RutaXml =
                rutaDestino;

            if (clasificacion ==
                "NO_IDENTIFICADO")
            {
                resultado.Estatus =
                    "NO_IDENTIFICADO";

                resultado.Mensaje =
                    "El RFC de la empresa no aparece " +
                    "como emisor ni receptor.";

                _repo.GuardarImportacion(
                    archivo.Name,
                    resultado.UUID,
                    "NO_IDENTIFICADO",
                    resultado.Mensaje);

                return resultado;
            }

            if (_repo.ExisteCfdi(
                    empresa.EmpresaId,
                    resultado.UUID))
            {
                _repo.ActualizarMontoTotalPagos(
                    empresa.EmpresaId,
                    resultado.UUID,
                    cfdi.MontoTotalPagos);

                _repo.ActualizarUbicacionCfdi(
                    empresa.EmpresaId,
                    resultado.UUID,
                    clasificacion,
                    rutaDestino);

                _repo.GuardarImportacion(
                    archivo.Name,
                    resultado.UUID,
                    "DUPLICADO",
                    $"Clasificación: {clasificacion}");

                resultado.Estatus =
                    "DUPLICADO";

                resultado.Mensaje =
                    "El UUID ya estaba registrado. " +
                    "Se actualizó su ubicación.";

                return resultado;
            }

            long cfdiId =
                _repo.GuardarCfdi(
                    empresa.EmpresaId,
                    cfdi,
                    clasificacion,
                    rutaDestino);

            _repo.GuardarConceptos(
                cfdiId,
                cfdi.Conceptos);

            _repo.GuardarImportacion(
                archivo.Name,
                resultado.UUID,
                "OK",
                $"Clasificación: {clasificacion}");

            resultado.Estatus =
                "OK";

            resultado.Mensaje =
                clasificacion switch
                {
                    "EMITIDO" =>
                        "CFDI de ingresos importado.",

                    "RECIBIDO" =>
                        "CFDI de egresos importado.",

                    "PROPIO" =>
                        "CFDI propio importado.",

                    _ =>
                        "CFDI importado."
                };

            return resultado;
        }
        catch (Exception ex)
        {
            resultado.Estatus =
                "ERROR";

            resultado.Mensaje =
                ex.Message;

            try
            {
                _repo.GuardarImportacion(
                    archivo.Name,
                    resultado.UUID,
                    "ERROR",
                    ex.Message);
            }
            catch
            {
                /*
                 * No ocultar el error original si falla
                 * el registro de la bitácora.
                 */
            }

            return resultado;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(
                    rutaTemporal) &&
                File.Exists(
                    rutaTemporal))
            {
                try
                {
                    File.Delete(
                        rutaTemporal);
                }
                catch
                {
                    /*
                     * La limpieza temporal no debe
                     * interrumpir el resultado de
                     * la importación.
                     */
                }
            }
        }
    }

    private static void ValidarArchivo(
        IBrowserFile archivo)
    {
        string extension =
            Path.GetExtension(
                archivo.Name);

        if (!string.Equals(
                extension,
                ".xml",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "El archivo no tiene extensión XML.");
        }

        if (archivo.Size <= 0)
        {
            throw new InvalidOperationException(
                "El archivo está vacío.");
        }

        if (archivo.Size >
            TamanoMaximoArchivo)
        {
            throw new InvalidOperationException(
                "El XML excede el límite de 25 MB.");
        }
    }

    private static string DeterminarClasificacion(
        string rfcEmpresa,
        string? rfcEmisor,
        string? rfcReceptor)
    {
        string empresa =
            NormalizarRfc(
                rfcEmpresa);

        string emisor =
            NormalizarRfc(
                rfcEmisor);

        string receptor =
            NormalizarRfc(
                rfcReceptor);

        bool esEmisor =
            empresa == emisor;

        bool esReceptor =
            empresa == receptor;

        if (esEmisor &&
            esReceptor)
        {
            return "PROPIO";
        }

        if (esEmisor)
        {
            return "EMITIDO";
        }

        if (esReceptor)
        {
            return "RECIBIDO";
        }

        return "NO_IDENTIFICADO";
    }

    private static string ObtenerNombreCarpeta(
        string clasificacion)
    {
        return clasificacion switch
        {
            "EMITIDO" =>
                "Emitidos",

            "RECIBIDO" =>
                "Recibidos",

            "PROPIO" =>
                "Propios",

            _ =>
                "NoIdentificados"
        };
    }

    private string ResolverRutaEmpresa(
        EmpresaDto empresa)
    {
        string rutaConfigurada =
            empresa.RutaXml?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(
                rutaConfigurada))
        {
            rutaConfigurada =
                Path.Combine(
                    "Storage",
                    "XML",
                    empresa.Rfc);
        }

        return CarpetaStorage.Resolver(
            rutaConfigurada);
    }

    private static string ObtenerNombreSeguro(
        string nombreOriginal,
        string uuid)
    {
        string nombre =
            Path.GetFileName(
                nombreOriginal);

        foreach (
            char caracter
            in Path.GetInvalidFileNameChars())
        {
            nombre =
                nombre.Replace(
                    caracter,
                    '_');
        }

        if (string.IsNullOrWhiteSpace(
                nombre))
        {
            nombre =
                $"{uuid}.xml";
        }

        if (!nombre.EndsWith(
                ".xml",
                StringComparison.OrdinalIgnoreCase))
        {
            nombre +=
                ".xml";
        }

        return nombre;
    }

    private static string ObtenerRutaDisponible(
        string rutaDestino,
        string uuid)
    {
        if (!File.Exists(
                rutaDestino))
        {
            return rutaDestino;
        }

        string carpeta =
            Path.GetDirectoryName(
                rutaDestino)
            ?? throw new InvalidOperationException(
                "No se pudo determinar la carpeta destino.");

        string extension =
            Path.GetExtension(
                rutaDestino);

        string nombre =
            Path.GetFileNameWithoutExtension(
                rutaDestino);

        string sufijo =
            string.IsNullOrWhiteSpace(
                uuid)
                ? DateTime.Now.ToString(
                    "yyyyMMddHHmmssfff")
                : uuid.Trim();

        string nuevaRuta =
            Path.Combine(
                carpeta,
                $"{nombre}_{sufijo}{extension}");

        int consecutivo =
            1;

        while (File.Exists(
                   nuevaRuta))
        {
            nuevaRuta =
                Path.Combine(
                    carpeta,
                    $"{nombre}_{sufijo}_" +
                    $"{consecutivo}{extension}");

            consecutivo++;
        }

        return nuevaRuta;
    }

    private static string NormalizarRfc(
        string? rfc)
    {
        return (rfc ?? "")
            .Trim()
            .Replace(
                " ",
                "")
            .Replace(
                "-",
                "")
            .ToUpperInvariant();
    }
}