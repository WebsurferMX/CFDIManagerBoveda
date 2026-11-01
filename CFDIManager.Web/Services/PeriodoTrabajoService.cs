using CFDIParser.Services;
using Microsoft.Extensions.Caching.Memory;

namespace CFDIManager.Web.Services;

/// <summary>
/// Mes con el que abren todas las pantallas. Lo define el Administrador en Herramientas:
/// automático (mes en curso) o un mes fijo, útil para terminar un mes cuando ya empezó el siguiente.
/// </summary>
public sealed class PeriodoTrabajoService(IServiceScopeFactory scopes, IMemoryCache cache)
{
    private const string Clave = "periodo_trabajo";
    private const string LlaveCache = "periodo-trabajo";

    // El servicio y la app de desarrollo comparten la base: se relee cada minuto.
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(1);

    /// <summary>Primer día del mes fijo, o null si es automático.</summary>
    public DateTime? MesFijo =>
        cache.GetOrCreate(LlaveCache, entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Vigencia;
            using IServiceScope scope = scopes.CreateScope();
            return Leer(scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>().ObtenerConfiguracionApp(Clave));
        });

    public bool EsAutomatico => MesFijo is null;

    /// <summary>Primer día del mes de trabajo.</summary>
    public DateTime Inicio => MesFijo ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    /// <summary>Hoy en automático; con mes fijo, su último día (o hoy si es el mes en curso).</summary>
    public DateTime Fin =>
        MesFijo is DateTime mes && mes.AddMonths(1) <= DateTime.Today
            ? mes.AddMonths(1).AddDays(-1)
            : DateTime.Today;

    public int Anio => Inicio.Year;

    public int Mes => Inicio.Month;

    /// <summary>Guarda el mes fijo (primer día) o null para volver al automático.</summary>
    public void Guardar(DateTime? mes)
    {
        using IServiceScope scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>()
            .GuardarConfiguracionApp(Clave, mes is DateTime m ? $"{m:yyyy-MM}" : "");

        cache.Remove(LlaveCache);
    }

    private static DateTime? Leer(string? valor) =>
        DateTime.TryParseExact(valor, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out DateTime mes)
            ? mes
            : null;
}
