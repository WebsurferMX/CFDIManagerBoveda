using CFDIManager.Web.Models;
using CFDIManager.Web.Services.Sat;
using CFDIParser.Models;

namespace CFDIManager.Web.Services.Interfaces;

public interface ISatDescargaService
{
    /// <summary>Abre la e.firma configurada y revisa contraseña, RFC, tipo y vigencia.</summary>
    Task<SatEstadoDto> ValidarConfiguracionAsync(
        EmpresaDto empresa);

    /// <summary>Se autentica con el SAT; devuelve la vigencia del token obtenido.</summary>
    Task<DateTime> ProbarConexionAsync(
        EmpresaDto empresa,
        CancellationToken cancelacion = default);

    /// <param name="origen">Ventana o Cierre en las de la descarga automática (ver SolicitudSatDto.Origen).</param>
    Task<SolicitudSatDto> CrearSolicitudAsync(
        EmpresaDto empresa,
        ParametrosSolicitudSat parametros,
        CancellationToken cancelacion = default,
        bool automatica = false,
        string origen = "");

    Task<SolicitudSatDto> CrearSolicitudPorUuidAsync(
        EmpresaDto empresa,
        string uuid,
        CancellationToken cancelacion = default);

    Task<SolicitudSatDto> VerificarAsync(
        EmpresaDto empresa,
        string solicitudId,
        CancellationToken cancelacion = default);

    /// <summary>
    /// Descarga los paquetes pendientes. Los XML se importan a CFDI Manager;
    /// los metadatos quedan disponibles con <see cref="LeerMetadatos"/>.
    /// </summary>
    /// <param name="soloUuids">Si se indica, del paquete solo se importan esos UUID (el resto ni se lee).</param>
    /// <param name="soloNuevos">Omite sin abrirlos los XML cuyo UUID ya está en la bóveda (ventanas que se traslapan).</param>
    Task<SolicitudSatDto> DescargarAsync(
        EmpresaDto empresa,
        string solicitudId,
        IProgress<string>? progreso = null,
        CancellationToken cancelacion = default,
        IReadOnlySet<string>? soloUuids = null,
        bool soloNuevos = false);

    Task<List<SolicitudSatDto>> ListarSolicitudesAsync(
        long empresaId);

    /// <summary>
    /// El SAT no permite cancelar una solicitud: se deja de verificar y descargar aquí.
    /// Lo ya importado se conserva.
    /// </summary>
    Task<SolicitudSatDto> CancelarAsync(
        long empresaId,
        string solicitudId);

    /// <summary>Quita la solicitud de la lista y borra sus paquetes; los CFDI importados se conservan.</summary>
    Task EliminarAsync(
        long empresaId,
        string solicitudId);

    /// <param name="marcarImportados">Consulta la base por cada UUID para llenar YaImportado (lento con miles).</param>
    List<SatCfdiDto> LeerMetadatos(
        EmpresaDto empresa,
        string solicitudId,
        bool marcarImportados = true);
}
