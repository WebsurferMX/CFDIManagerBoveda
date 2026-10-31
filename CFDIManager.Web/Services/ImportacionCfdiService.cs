using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services;

/// <summary>
/// Importa un XML ya presente en disco: lo clasifica contra la empresa,
/// lo copia a su carpeta definitiva y lo registra en la base de datos.
/// Si la contraparte del CFDI es otra empresa activa (facturación entre empresas
/// del grupo), también lo registra en ella. Lo usan Importaciones, Descargas SAT,
/// Administración de XML y la sincronización de Storage.
/// </summary>
public class ImportacionCfdiService
{
    private readonly PostgreSqlRepository _repositorio;
    private readonly XmlCfdiParser _parser;
    private readonly RutaXmlService _rutas;

    private List<EmpresaDto>? _empresasActivas;

    public ImportacionCfdiService(
        PostgreSqlRepository repositorio,
        XmlCfdiParser parser,
        RutaXmlService rutas)
    {
        _repositorio =
            repositorio;

        _parser =
            parser;

        _rutas =
            rutas;
    }

    /// <param name="enSitio">
    /// El XML ya está en la carpeta de la empresa (sincronización de Storage):
    /// se registra con su ruta actual sin copiarlo, y si el UUID ya existe no se modifica.
    /// </param>
    /// <param name="registrarContraparte">
    /// Registrar también en la empresa contraparte si es otra empresa activa.
    /// Es false en ese segundo registro para no repetir el proceso.
    /// </param>
    public ResultadoImportacionCfdi Importar(
        EmpresaDto empresa,
        string rutaXml,
        string nombreArchivo,
        bool enSitio = false,
        bool registrarContraparte = true)
    {
        CfdiDto? cfdi =
            null;

        try
        {
            cfdi =
                _parser.Parse(
                    rutaXml);

            string? clasificacion =
                DeterminarClasificacion(
                    cfdi,
                    empresa.Rfc);

            if (clasificacion is null)
            {
                _repositorio.GuardarImportacion(
                    nombreArchivo,
                    cfdi.UUID,
                    "NO IDENTIFICADO",
                    "El RFC activo no coincide con el CFDI.");

                return new ResultadoImportacionCfdi
                {
                    Uuid =
                        cfdi.UUID ?? "",

                    Estatus =
                        "OMITIDO",

                    Detalle =
                        "El RFC de la empresa activa no coincide " +
                        "con el emisor ni con el receptor."
                };
            }

            bool yaExistia =
                _repositorio.ExisteCfdi(
                    empresa.EmpresaId,
                    cfdi.UUID);

            if (enSitio && yaExistia)
            {
                return new ResultadoImportacionCfdi
                {
                    Contraparte =
                        registrarContraparte
                            ? RegistrarEnContraparte(empresa, cfdi, rutaXml, nombreArchivo)
                            : "",

                    Uuid =
                        cfdi.UUID ?? "",

                    Clasificacion =
                        clasificacion,

                    Estatus =
                        "EXISTENTE",

                    YaExistia =
                        true,

                    Detalle =
                        "El UUID ya estaba registrado."
                };
            }

            string rutaDefinitiva =
                enSitio
                    ? Path.GetFullPath(rutaXml)
                    : CopiarADestinoDefinitivo(
                        empresa,
                        cfdi,
                        clasificacion,
                        rutaXml,
                        nombreArchivo);

            if (yaExistia)
            {
                // Un CFDI timbrado no cambia: con el mismo UUID solo se actualiza su
                // ubicación; sus conceptos ya están guardados.
                _repositorio.ActualizarUbicacionCfdi(
                    empresa.EmpresaId,
                    cfdi.UUID,
                    clasificacion,
                    rutaDefinitiva);
            }
            else
            {
                long cfdiId =
                    _repositorio.GuardarCfdi(
                        empresa.EmpresaId,
                        cfdi,
                        clasificacion,
                        rutaDefinitiva);

                _repositorio.GuardarConceptos(
                    cfdiId,
                    cfdi.Conceptos);
            }

            _repositorio.GuardarImportacion(
                nombreArchivo,
                cfdi.UUID,
                yaExistia
                    ? "ACTUALIZADO"
                    : "CORRECTO",
                null);

            return new ResultadoImportacionCfdi
            {
                Contraparte =
                    registrarContraparte
                        ? RegistrarEnContraparte(empresa, cfdi, rutaDefinitiva, nombreArchivo)
                        : "",

                Uuid =
                    cfdi.UUID ?? "",

                Clasificacion =
                    clasificacion,

                Estatus =
                    yaExistia
                        ? "ACTUALIZADO"
                        : "CORRECTO",

                YaExistia =
                    yaExistia,

                Detalle =
                    yaExistia
                        ? "El UUID ya estaba registrado; se actualizó su ubicación."
                        : "El CFDI se importó correctamente."
            };
        }
        catch (Exception ex)
        {
            string uuid =
                cfdi?.UUID
                ?? "";

            try
            {
                _repositorio.GuardarImportacion(
                    nombreArchivo,
                    uuid,
                    "ERROR",
                    ex.Message);
            }
            catch
            {
                // El error original es el que se muestra.
            }

            return new ResultadoImportacionCfdi
            {
                Uuid =
                    uuid,

                Estatus =
                    "ERROR",

                Detalle =
                    ex.Message
            };
        }
    }

    /// <summary>
    /// Si el emisor o receptor del CFDI es otra empresa activa que aún no lo tiene,
    /// lo registra también en ella (copiando el XML a su carpeta). Devuelve el RFC de
    /// esa empresa, o vacío si no aplica. Un fallo aquí no invalida el registro principal.
    /// </summary>
    private string RegistrarEnContraparte(
        EmpresaDto empresa,
        CfdiDto cfdi,
        string rutaXml,
        string nombreArchivo)
    {
        string emisor = NormalizarRfc(cfdi.RFCEmisor);
        string receptor = NormalizarRfc(cfdi.RFCReceptor);

        if (emisor == receptor)
        {
            return "";
        }

        _empresasActivas ??= _repositorio.ObtenerEmpresasActivas();

        EmpresaDto? contraparte =
            _empresasActivas.FirstOrDefault(e =>
                e.EmpresaId != empresa.EmpresaId &&
                NormalizarRfc(e.Rfc) is var rfc &&
                (rfc == emisor || rfc == receptor));

        if (contraparte is null ||
            _repositorio.ExisteCfdi(contraparte.EmpresaId, cfdi.UUID))
        {
            return "";
        }

        ResultadoImportacionCfdi r =
            Importar(
                contraparte,
                rutaXml,
                nombreArchivo,
                enSitio: false,
                registrarContraparte: false);

        return r.Estatus is "CORRECTO" or "ACTUALIZADO"
            ? contraparte.Rfc
            : "";
    }

    private static string? DeterminarClasificacion(
        CfdiDto cfdi,
        string rfcEmpresa)
    {
        string rfcNormalizado =
            NormalizarRfc(
                rfcEmpresa);

        string rfcEmisor =
            NormalizarRfc(
                cfdi.RFCEmisor);

        string rfcReceptor =
            NormalizarRfc(
                cfdi.RFCReceptor);

        if (rfcEmisor == rfcNormalizado)
        {
            return "EMITIDO";
        }

        if (rfcReceptor == rfcNormalizado)
        {
            return "RECIBIDO";
        }

        return null;
    }

    private string CopiarADestinoDefinitivo(
        EmpresaDto empresa,
        CfdiDto cfdi,
        string clasificacion,
        string rutaXml,
        string nombreArchivo)
    {
        string carpetaBase =
            ObtenerCarpetaBaseEmpresa(
                empresa);

        string carpetaClasificacion =
            clasificacion == "EMITIDO"
                ? "Emitidos"
                : "Recibidos";

        int anio =
            cfdi.FechaEmision.Year > 1
                ? cfdi.FechaEmision.Year
                : DateTime.Today.Year;

        int mes =
            cfdi.FechaEmision.Month > 0
                ? cfdi.FechaEmision.Month
                : DateTime.Today.Month;

        string carpetaDestino =
            Path.Combine(
                carpetaBase,
                carpetaClasificacion,
                anio.ToString(),
                mes.ToString("00"));

        Directory.CreateDirectory(
            carpetaDestino);

        string uuidSeguro =
            LimpiarNombreArchivo(
                cfdi.UUID);

        string nombreDestino =
            string.IsNullOrWhiteSpace(uuidSeguro)
                ? nombreArchivo
                : $"{uuidSeguro}.xml";

        string rutaDestino =
            Path.Combine(
                carpetaDestino,
                nombreDestino);

        File.Copy(
            rutaXml,
            rutaDestino,
            overwrite: true);

        return Path.GetFullPath(
            rutaDestino);
    }

    /// <summary>Carpeta donde se guardan los XML de la empresa: &lt;ruta XML general&gt;\RFC.</summary>
    public string ObtenerCarpetaBaseEmpresa(
        EmpresaDto empresa)
    {
        return _rutas.CarpetaEmpresa(
            empresa);
    }

    private static string LimpiarNombreArchivo(
        string? valor)
    {
        string resultado =
            (valor ?? "")
                .Trim();

        foreach (char caracter
                 in Path.GetInvalidFileNameChars())
        {
            resultado =
                resultado.Replace(
                    caracter,
                    '_');
        }

        return resultado;
    }

    private static string NormalizarRfc(
        string? rfc)
    {
        return (rfc ?? "")
            .Trim()
            .ToUpperInvariant();
    }
}

public sealed class ResultadoImportacionCfdi
{
    public string Uuid { get; init; } =
        "";

    /// <summary>EMITIDO o RECIBIDO; vacío si no se identificó o hubo error.</summary>
    public string Clasificacion { get; init; } =
        "";

    /// <summary>RFC de otra empresa del grupo en la que también se registró (CFDI entre empresas).</summary>
    public string Contraparte { get; init; } =
        "";

    /// <summary>CORRECTO, ACTUALIZADO, EXISTENTE (solo en sincronización), OMITIDO o ERROR.</summary>
    public string Estatus { get; init; } =
        "";

    public bool YaExistia { get; init; }

    public string Detalle { get; init; } =
        "";
}
