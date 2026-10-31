using CFDIParser.Models;

namespace CFDIManager.Web.Services.Interfaces;

public interface IEfosSatService
{
    /// <summary>Descarga el Listado completo 69-B del portal del SAT y reemplaza la lista cargada.</summary>
    Task<ResultadoImportacionEfosDto> DescargarDelSatAsync(
        CancellationToken cancelacion = default);

    /// <summary>Carga el Listado completo 69-B desde un archivo CSV descargado del SAT.</summary>
    Task<ResultadoImportacionEfosDto> ImportarAsync(
        Stream archivo,
        string nombreArchivo,
        CancellationToken cancelacion = default);
}
