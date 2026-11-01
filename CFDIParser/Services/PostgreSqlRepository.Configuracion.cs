using Npgsql;

namespace CFDIParser.Services;

/// <summary>Ajustes generales de la app (clave/valor), p. ej. el periodo de trabajo.</summary>
public partial class PostgreSqlRepository
{
    private const string CrearConfiguracionApp = """
        CREATE TABLE IF NOT EXISTS configuracion_app (
            clave TEXT PRIMARY KEY,
            valor TEXT NOT NULL,
            actualizado TIMESTAMP NOT NULL DEFAULT NOW()
        );
        """;

    public string? ObtenerConfiguracionApp(string clave)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(CrearConfiguracionApp + "SELECT valor FROM configuracion_app WHERE clave = @clave;", cn);
        cmd.Parameters.AddWithValue("clave", clave);

        return cmd.ExecuteScalar() as string;
    }

    public void GuardarConfiguracionApp(string clave, string valor)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(CrearConfiguracionApp + """
            INSERT INTO configuracion_app (clave, valor) VALUES (@clave, @valor)
            ON CONFLICT (clave) DO UPDATE SET valor = EXCLUDED.valor, actualizado = NOW();
            """, cn);

        cmd.Parameters.AddWithValue("clave", clave);
        cmd.Parameters.AddWithValue("valor", valor);
        cmd.ExecuteNonQuery();
    }
}
