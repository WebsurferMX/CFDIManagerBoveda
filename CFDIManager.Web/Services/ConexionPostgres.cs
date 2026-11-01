using Npgsql;

namespace CFDIManager.Web.Services;

/// <summary>Cadena de conexión a PostgreSQL: ConnectionStrings:PostgreSQL más la contraseña de CFDI_POSTGRES_PASSWORD.</summary>
public static class ConexionPostgres
{
    public static string Cadena(IConfiguration configuration)
    {
        string cadenaBase =
            configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException(
                "No existe ConnectionStrings:PostgreSQL.");

        // Variable de entorno o, en desarrollo, user secrets (dotnet user-secrets set CFDI_POSTGRES_PASSWORD ...).
        string password =
            Environment.GetEnvironmentVariable(
                "CFDI_POSTGRES_PASSWORD")
            ?? configuration["CFDI_POSTGRES_PASSWORD"]
            ?? throw new InvalidOperationException(
                "No existe la variable CFDI_POSTGRES_PASSWORD (ni en user secrets).");

        return new NpgsqlConnectionStringBuilder(cadenaBase) { Password = password }.ConnectionString;
    }
}
