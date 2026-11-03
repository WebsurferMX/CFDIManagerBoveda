using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Sinube;

/// <summary>Una empresa y un lado (clientes o proveedores) del barrido: cuántos XML hay y cuántos faltan en SINUBE.</summary>
public sealed class PendientesSinubeFila
{
    public required EmpresaDto Empresa { get; init; }

    public bool Clientes { get; init; }

    /// <summary>XML del periodo en la bóveda, sin nómina.</summary>
    public int Xml { get; init; }

    /// <summary>Los que SINUBE confirma y los que la app ya subió o el usuario confirmó.</summary>
    public int EnSinube { get; init; }

    public int PendientesIngreso { get; init; }

    public int PendientesEgreso { get; init; }

    public int PendientesPago { get; init; }

    public int PendientesOtros { get; init; }

    public int Pendientes => PendientesIngreso + PendientesEgreso + PendientesPago + PendientesOtros;

    /// <summary>Sin acceso a SINUBE o la consulta falló.</summary>
    public string? Aviso { get; init; }
}

/// <summary>
/// Barrido de Empresas → Analizar: por empresa y lado, los XML del mes (sin nómina) contra lo que ya tiene SINUBE
/// (facturas de clientes, depósitos y XML de proveedores) y lo ya subido desde la app. Solo lectura.
/// </summary>
public class PendientesSinubeService(PostgreSqlRepository repo, SinubeService sinube, SinubeDatosService datos)
{
    public async Task<List<PendientesSinubeFila>> AnalizarAsync(
        IReadOnlyList<EmpresaDto> empresas, DateTime periodo, IProgress<string>? progreso, CancellationToken cancelacion)
    {
        var filas = new List<PendientesSinubeFila>();
        var desde = new DateTime(periodo.Year, periodo.Month, 1);
        int n = 0;

        foreach (EmpresaDto empresa in empresas)
        {
            cancelacion.ThrowIfCancellationRequested();
            progreso?.Report($"Analizando {++n} de {empresas.Count}: {empresa.Rfc}…");

            SinubeConfiguracionDto config = sinube.ObtenerConfiguracion(empresa);

            if (SinubeService.FaltanDatos(config, requierePassword: true) is not null || string.IsNullOrWhiteSpace(config.Usuario))
            {
                filas.Add(new PendientesSinubeFila { Empresa = empresa, Clientes = true, Aviso = "Sin acceso a SINUBE" });
                continue;
            }

            foreach (bool clientes in new[] { true, false })
            {
                List<(string Uuid, string Tipo)> xml = repo.ObtenerCfdiParaSinube(empresa.EmpresaId, clientes, desde, desde.AddMonths(1));

                if (xml.Count == 0)
                    continue;

                try
                {
                    HashSet<string> enSinube = await datos.UuidsEnSinubeAsync(config, clientes, [desde], cancelacion);

                    // Los pagos de clientes viven en DbRecepcionPagosXML (y a veces DbDeposito, por mes de pago).
                    if (clientes && xml.Any(x => x.Tipo == "P"))
                        enSinube.UnionWith(await datos.UuidsDepositosAsync(config, [desde.AddMonths(-1), desde, desde.AddMonths(1)], cancelacion));

                    enSinube.UnionWith(repo.ObtenerUuidsEnviadosSinube(empresa.EmpresaId, clientes ? "C" : "P"));

                    var pendientes = xml.Where(x => !enSinube.Contains(x.Uuid)).ToList();

                    filas.Add(new PendientesSinubeFila
                    {
                        Empresa = empresa,
                        Clientes = clientes,
                        Xml = xml.Count,
                        EnSinube = xml.Count - pendientes.Count,
                        PendientesIngreso = pendientes.Count(p => p.Tipo == "I"),
                        PendientesEgreso = pendientes.Count(p => p.Tipo == "E"),
                        PendientesPago = pendientes.Count(p => p.Tipo == "P"),
                        PendientesOtros = pendientes.Count(p => p.Tipo is not ("I" or "E" or "P"))
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    filas.Add(new PendientesSinubeFila { Empresa = empresa, Clientes = clientes, Xml = xml.Count, Aviso = ex.Message });
                }
            }
        }

        return filas;
    }
}
