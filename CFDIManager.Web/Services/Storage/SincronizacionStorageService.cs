using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Storage;

/// <summary>
/// Mantiene la base como índice de la carpeta de XML de cada empresa (Storage):
/// registra los XML nuevos o modificados y retira los CFDI cuyo XML ya no existe.
/// La carpeta es la fuente de verdad; no copia, mueve ni borra archivos.
/// </summary>
public class SincronizacionStorageService
{
    // Si falta más de esta fracción de los XML registrados se asume un problema de acceso
    // (unidad de red desconectada, carpeta movida) y no se elimina nada.
    private const double MaximoFaltantes = 0.5;

    private readonly PostgreSqlRepository _repo;
    private readonly ImportacionCfdiService _importacion;
    private readonly ILogger<SincronizacionStorageService> _log;

    public SincronizacionStorageService(
        PostgreSqlRepository repo,
        ImportacionCfdiService importacion,
        ILogger<SincronizacionStorageService> log)
    {
        _repo = repo;
        _importacion = importacion;
        _log = log;
    }

    public ResultadoSincronizacion SincronizarEmpresa(
        EmpresaDto empresa,
        IProgress<ResultadoSincronizacion>? progreso,
        CancellationToken ct)
    {
        string carpeta = _importacion.ObtenerCarpetaBaseEmpresa(empresa);
        var r = new ResultadoSincronizacion { EmpresaId = empresa.EmpresaId, Rfc = empresa.Rfc, Carpeta = carpeta, Inicio = DateTime.Now };

        if (!Directory.Exists(carpeta))
        {
            r.Aviso = $"No existe la carpeta {carpeta}; no se hizo ningún cambio.";
            r.Fin = DateTime.Now;
            return r;
        }

        // 1. Archivos actuales y los ya revisados en pasadas anteriores.
        Dictionary<string, FileInfo> actuales = Directory
            .EnumerateFiles(carpeta, "*.xml", SearchOption.AllDirectories)
            .Where(a => !a.Contains($"{Path.DirectorySeparatorChar}NoIdentificados{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(a => new FileInfo(a))
            .ToDictionary(f => f.FullName, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, (DateTime Modificado, long Tamano)> conocidos = _repo.ObtenerArchivosStorage(empresa.EmpresaId);

        r.Archivos = actuales.Count;

        // 2. Nuevos o modificados: se registran con su ruta actual.
        List<FileInfo> porRevisar = actuales.Values
            .Where(f => !conocidos.TryGetValue(f.FullName, out var c) || c.Tamano != f.Length || Math.Abs((c.Modificado - f.LastWriteTime).TotalSeconds) > 1)
            .ToList();

        r.PorRevisar = porRevisar.Count;

        foreach (FileInfo f in porRevisar)
        {
            ct.ThrowIfCancellationRequested();

            ResultadoImportacionCfdi i = _importacion.Importar(empresa, f.FullName, f.Name, enSitio: true);

            switch (i.Estatus)
            {
                case "CORRECTO": r.Nuevos++; break;
                case "EXISTENTE": r.YaRegistrados++; break;
                case "OMITIDO": r.Omitidos++; break;
                default: r.Errores++; break;
            }

            _repo.RegistrarArchivoStorage(empresa.EmpresaId, f.FullName, i.Uuid, f.LastWriteTime, f.Length, i.Estatus, i.Detalle);
            r.Revisados++;

            if (r.Revisados % 25 == 0)
            {
                progreso?.Report(r);
            }
        }

        // 3. Archivos que desaparecieron de la carpeta.
        List<string> desaparecidos = conocidos.Keys.Where(k => !actuales.ContainsKey(k)).ToList();
        _repo.QuitarArchivosStorage(empresa.EmpresaId, desaparecidos);

        // 4. CFDI cuyo XML ya no existe: se reubican a otra copia o se retiran.
        List<(long CfdiId, string Uuid, string Clasificacion, string Ruta)> enCarpeta = _repo.ObtenerCfdiEnCarpeta(empresa.EmpresaId, carpeta);
        var sinXml = enCarpeta.Where(c => !actuales.ContainsKey(c.Ruta) && !File.Exists(c.Ruta)).ToList();

        if (sinXml.Count > 0 && (actuales.Count == 0 || sinXml.Count > enCarpeta.Count * MaximoFaltantes))
        {
            r.Aviso = $"Faltan {sinXml.Count:N0} de {enCarpeta.Count:N0} XML registrados; parece un problema de acceso a la carpeta, no se eliminó nada.";
        }
        else
        {
            foreach (var c in sinXml)
            {
                string? otra = _repo.BuscarOtraCopiaStorage(empresa.EmpresaId, c.Uuid, c.Ruta);

                if (otra is not null && File.Exists(otra))
                {
                    _repo.ActualizarUbicacionCfdi(empresa.EmpresaId, c.Uuid, c.Clasificacion, otra);
                    r.Reubicados++;
                }
                else
                {
                    _repo.EliminarCfdi(empresa.EmpresaId, c.CfdiId);
                    r.Eliminados++;
                }
            }
        }

        r.Fin = DateTime.Now;
        progreso?.Report(r);

        if (r.Nuevos + r.Reubicados + r.Eliminados + r.Errores > 0 || r.Aviso != "")
        {
            _log.LogInformation(
                "Storage {Rfc}: {Nuevos} nuevos, {Reubicados} reubicados, {Eliminados} retirados, {Errores} con error. {Aviso}",
                empresa.Rfc, r.Nuevos, r.Reubicados, r.Eliminados, r.Errores, r.Aviso);
        }

        return r;
    }

    /// <summary>
    /// Registra en la empresa contraparte los CFDI entre empresas del grupo que aún no
    /// tiene (por ejemplo, si se dio de alta la empresa después). Devuelve cuántos registró.
    /// </summary>
    public int RegistrarIntercompaniasPendientes(CancellationToken ct)
    {
        Dictionary<long, EmpresaDto> empresas = _repo.ObtenerEmpresasActivas().ToDictionary(e => e.EmpresaId);
        int registrados = 0;

        foreach (var (empresaId, ruta, uuid) in _repo.ObtenerIntercompaniasPendientes())
        {
            ct.ThrowIfCancellationRequested();

            if (!empresas.TryGetValue(empresaId, out EmpresaDto? destino) || !File.Exists(ruta))
            {
                continue;
            }

            ResultadoImportacionCfdi i = _importacion.Importar(destino, ruta, Path.GetFileName(ruta), enSitio: false, registrarContraparte: false);

            if (i.Estatus is "CORRECTO" or "ACTUALIZADO")
            {
                registrados++;
            }
            else
            {
                _log.LogWarning("No se pudo registrar el CFDI {Uuid} en {Rfc}: {Detalle}", uuid, destino.Rfc, i.Detalle);
            }
        }

        if (registrados > 0)
        {
            _log.LogInformation("CFDI entre empresas registrados en la contraparte: {Registrados}", registrados);
        }

        return registrados;
    }
}

public sealed class ResultadoSincronizacion
{
    public long EmpresaId { get; init; }
    public string Rfc { get; init; } = "";
    public string Carpeta { get; init; } = "";
    public DateTime Inicio { get; init; }
    public DateTime? Fin { get; set; }
    public int Archivos { get; set; }
    public int PorRevisar { get; set; }
    public int Revisados { get; set; }
    public int Nuevos { get; set; }
    public int YaRegistrados { get; set; }
    public int Omitidos { get; set; }
    public int Errores { get; set; }
    public int Reubicados { get; set; }
    public int Eliminados { get; set; }
    public string Aviso { get; set; } = "";

    public string Resumen =>
        Aviso != "" && Revisados == 0 ? Aviso
        : $"{Archivos:N0} XML · {Nuevos:N0} nuevos" +
          (YaRegistrados > 0 ? $" · {YaRegistrados:N0} ya registrados" : "") +
          (Reubicados > 0 ? $" · {Reubicados:N0} reubicados" : "") +
          (Eliminados > 0 ? $" · {Eliminados:N0} retirados (XML eliminado)" : "") +
          (Omitidos > 0 ? $" · {Omitidos:N0} de otro RFC" : "") +
          (Errores > 0 ? $" · {Errores:N0} con error" : "") +
          (Aviso != "" ? $" · {Aviso}" : "");
}
