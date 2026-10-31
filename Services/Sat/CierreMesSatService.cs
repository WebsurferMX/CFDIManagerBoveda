using System.Collections.Concurrent;
using CFDIManager.Web.Services.Correo;
using CFDIManager.Web.Services.Seguridad;
using CFDIManager.Web.Services.Sinube;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Sat;

/// <summary>
/// Cierre de mes del SAT por empresa: metadatos de emitidos y recibidos del mes, descarga de los XML que faltan
/// en la bóveda y registro en sat_cierres (con aviso por correo a los administradores). Corre en segundo plano:
/// lo lanza la descarga automática los primeros días del mes o el usuario desde Descargas SAT.
/// Los recibidos cancelados sin XML no se pueden obtener (el SAT responde 5012); quedan contados en el registro.
/// </summary>
public sealed class CierreMesSatService(IServiceScopeFactory scopes, ILogger<CierreMesSatService> log)
{
    /// <summary>Empresas con un cierre en curso y su avance.</summary>
    public ConcurrentDictionary<long, string> EnCurso { get; } = new();

    public event Action? Cambio;

    /// <summary>Lanza el cierre del mes; false si esa empresa ya tiene uno en curso.</summary>
    public bool Iniciar(EmpresaDto empresa, int anio, int mes, bool automatico)
    {
        if (!EnCurso.TryAdd(empresa.EmpresaId, "Iniciando…"))
            return false;

        Cambio?.Invoke();
        _ = Task.Run(() => EjecutarAsync(empresa, anio, mes, automatico));
        return true;
    }

    private async Task EjecutarAsync(EmpresaDto empresa, int anio, int mes, bool automatico)
    {
        using IServiceScope scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();
        var metadatos = scope.ServiceProvider.GetRequiredService<SatMetadatosService>();

        var cierre = new SatCierreDto
        {
            EmpresaId = empresa.EmpresaId,
            Periodo = SatMetadatosService.Periodo(anio, mes),
            Inicio = DateTime.Now,
            Estado = "En curso",
            Automatico = automatico
        };

        repo.GuardarCierre(cierre);

        var progreso = new Progress<string>(texto =>
        {
            EnCurso[empresa.EmpresaId] = texto;
            Cambio?.Invoke();
        });

        var detalle = new List<string>();

        try
        {
            foreach (bool recibidos in new[] { false, true })
            {
                string que = recibidos ? "recibidos" : "emitidos";
                IProgress<string> avance = new Progress<string>(t => ((IProgress<string>)progreso).Report($"{Capitalizar(que)}: {t}"));

                SatMetadatosPeriodoDto periodo = await metadatos.ActualizarAsync(
                    empresa, recibidos, anio, mes, avance, CancellationToken.None, origen: "Cierre");

                if (recibidos)
                    cierre.MetadatosRecibidos = periodo.Registros;
                else
                    cierre.MetadatosEmitidos = periodo.Registros;

                (List<string> vigentes, List<string> cancelados) = SinXml(repo, metadatos, empresa, recibidos, anio, mes, await metadatos.ObtenerAsync(empresa, recibidos, anio, mes));

                // El SAT no entrega recibidos cancelados: en recibidos solo se piden los vigentes.
                List<string> aPedir = recibidos ? [] : cancelados;

                if (vigentes.Count + aPedir.Count > 0)
                {
                    SatMetadatosService.ResultadoXmlFaltantes r = await metadatos.DescargarXmlFaltantesAsync(
                        empresa, recibidos, anio, mes, vigentes, aPedir, avance, CancellationToken.None, origen: "Cierre");

                    cierre.XmlImportados += r.Importados;

                    if (r.Rechazados.Count > 0)
                        detalle.Add($"{Capitalizar(que)}: el SAT no entregó {r.Rechazados.Count}.");
                }

                (vigentes, cancelados) = SinXml(repo, metadatos, empresa, recibidos, anio, mes, await metadatos.ObtenerAsync(empresa, recibidos, anio, mes));

                if (recibidos)
                    cierre.CanceladosSinXml += cancelados.Count;
                else
                    cierre.OtrosSinXml += cancelados.Count;

                cierre.OtrosSinXml += vigentes.Count;
            }

            cierre.Estado = cierre.OtrosSinXml == 0 ? "Completo" : "Con pendientes";
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falló el cierre de mes {Periodo} de {Rfc}", cierre.Periodo, empresa.Rfc);
            cierre.Estado = "Error";
            detalle.Add(ex.Message);
        }

        cierre.Fin = DateTime.Now;
        cierre.Detalle = string.Join(" ", detalle);
        repo.GuardarCierre(cierre);

        log.LogInformation(
            "Cierre {Periodo} {Rfc}: {Estado}; {Importados} XML importados, {Cancelados} recibidos cancelados sin XML, {Otros} pendientes.",
            cierre.Periodo, empresa.Rfc, cierre.Estado, cierre.XmlImportados, cierre.CanceladosSinXml, cierre.OtrosSinXml);

        await AvisarAsync(scope, empresa, cierre);

        EnCurso.TryRemove(empresa.EmpresaId, out _);
        Cambio?.Invoke();
    }

    // UUID del mes que el SAT tiene y la bóveda no, separados en vigentes y cancelados.
    private static (List<string> Vigentes, List<string> Cancelados) SinXml(
        PostgreSqlRepository repo, SatMetadatosService _, EmpresaDto empresa, bool recibidos, int anio, int mes,
        (SatMetadatosPeriodoDto? Periodo, List<SatMetadatoDto> Metadatos) sat)
    {
        HashSet<string> boveda = repo.ObtenerUuidsEmpresa(empresa.EmpresaId);
        List<SatMetadatoDto> faltan = sat.Metadatos.Where(m => !boveda.Contains(m.Uuid.ToUpperInvariant())).ToList();

        return (
            faltan.Where(m => m.Estatus != "Cancelado").Select(m => m.Uuid).ToList(),
            faltan.Where(m => m.Estatus == "Cancelado").Select(m => m.Uuid).ToList());
    }

    // Resumen por correo a los administradores activos con correo (si el envío está configurado).
    private async Task AvisarAsync(IServiceScope scope, EmpresaDto empresa, SatCierreDto c)
    {
        try
        {
            var correo = scope.ServiceProvider.GetRequiredService<CorreoService>();
            var repo = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();

            if (!correo.EstaConfigurado())
                return;

            List<string> para = repo.ObtenerUsuariosEnRol(Roles.Administrador.ToUpperInvariant())
                .Where(u => u.Activo && CorreoService.EsCorreoValido(u.Email))
                .Select(u => u.Email)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (para.Count == 0)
                return;

            string periodo = $"{c.Periodo / 100}-{c.Periodo % 100:00}";

            (string html, string texto) = PlantillaCorreo.Construir(new PlantillaCorreo.Contenido
            {
                Titulo = $"Cierre de mes del SAT · {empresa.NombreParaMostrar} · {periodo}",
                Parrafos =
                [
                    c.Estado switch
                    {
                        "Completo" => "La bóveda quedó completa: tiene el XML de todos los CFDI que el SAT reporta para el mes (salvo recibidos cancelados, que el SAT no entrega).",
                        "Con pendientes" => "Quedaron CFDI sin XML en la bóveda. Revísalos en Conciliación SINUBE o vuelve a ejecutar el cierre más tarde.",
                        _ => "El cierre no pudo terminar. Revisa el detalle en Descargas SAT y vuelve a ejecutarlo."
                    }
                ],
                Datos =
                [
                    ("Empresa", $"{empresa.Rfc} · {empresa.NombreParaMostrar}"),
                    ("Estado", c.Estado),
                    ("CFDI emitidos en el SAT", c.MetadatosEmitidos.ToString("N0")),
                    ("CFDI recibidos en el SAT", c.MetadatosRecibidos.ToString("N0")),
                    ("XML descargados", c.XmlImportados.ToString("N0")),
                    ("Recibidos cancelados sin XML", c.CanceladosSinXml.ToString("N0")),
                    ("Pendientes sin XML", c.OtrosSinXml.ToString("N0"))
                ],
                Nota = string.IsNullOrWhiteSpace(c.Detalle) ? null : c.Detalle
            });

            await correo.EnviarAsync(new MensajeCorreo
            {
                Tipo = "Cierre de mes",
                Para = para,
                Asunto = $"Cierre de mes SAT {empresa.Rfc} {periodo}: {c.Estado}",
                CuerpoHtml = html,
                CuerpoTexto = texto,
                EnviadoPor = "Descarga automática",
                EmpresaId = empresa.EmpresaId
            });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo enviar el aviso del cierre de {Rfc}", empresa.Rfc);
        }
    }

    private static string Capitalizar(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
