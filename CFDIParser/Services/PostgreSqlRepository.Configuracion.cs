using CFDIParser.Models;
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

    // ---------- historial de respaldos (Herramientas) ----------

    private const string CrearRespaldos = """
        CREATE TABLE IF NOT EXISTS respaldos (
            respaldo_id BIGSERIAL PRIMARY KEY,
            equipo TEXT NOT NULL,
            tipo TEXT NOT NULL,
            inicio TIMESTAMP NOT NULL DEFAULT NOW(),
            fin TIMESTAMP,
            estado TEXT NOT NULL,
            archivo TEXT NOT NULL DEFAULT '',
            tamano BIGINT NOT NULL DEFAULT 0,
            detalle TEXT NOT NULL DEFAULT ''
        );
        """;

    public long IniciarRespaldo(string equipo, string tipo)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(CrearRespaldos + """
            INSERT INTO respaldos (equipo, tipo, estado) VALUES (@equipo, @tipo, 'En curso') RETURNING respaldo_id;
            """, cn);

        cmd.Parameters.AddWithValue("equipo", equipo);
        cmd.Parameters.AddWithValue("tipo", tipo);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void TerminarRespaldo(long respaldoId, string estado, string archivo, long tamano, string detalle)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand("""
            UPDATE respaldos SET fin = NOW(), estado = @estado, archivo = @archivo, tamano = @tamano, detalle = @detalle
            WHERE respaldo_id = @id;
            """, cn);

        cmd.Parameters.AddWithValue("id", respaldoId);
        cmd.Parameters.AddWithValue("estado", estado);
        cmd.Parameters.AddWithValue("archivo", archivo);
        cmd.Parameters.AddWithValue("tamano", tamano);
        cmd.Parameters.AddWithValue("detalle", detalle);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Si ya se intentó hoy un respaldo de ese tipo en el equipo (aunque haya fallado): un intento automático por día,
    /// así un error no se repite cada minuto ni se vuelve a hacer al reiniciar. Para reintentar está "Respaldar ahora".
    /// </summary>
    public bool HayRespaldoHoy(string equipo, string tipo)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(CrearRespaldos + """
            SELECT EXISTS (SELECT 1 FROM respaldos WHERE equipo = @equipo AND tipo = @tipo AND inicio::date = CURRENT_DATE);
            """, cn);

        cmd.Parameters.AddWithValue("equipo", equipo);
        cmd.Parameters.AddWithValue("tipo", tipo);
        return (bool)cmd.ExecuteScalar()!;
    }

    public List<RespaldoDto> ObtenerRespaldos(string equipo, int limite)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(CrearRespaldos + """
            SELECT respaldo_id, equipo, tipo, inicio, fin, estado, archivo, tamano, detalle
            FROM respaldos WHERE equipo = @equipo ORDER BY inicio DESC LIMIT @limite;
            """, cn);

        cmd.Parameters.AddWithValue("equipo", equipo);
        cmd.Parameters.AddWithValue("limite", limite);

        var lista = new List<RespaldoDto>();
        using NpgsqlDataReader dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            lista.Add(new RespaldoDto
            {
                RespaldoId = dr.GetInt64(0),
                Equipo = dr.GetString(1),
                Tipo = dr.GetString(2),
                Inicio = dr.GetDateTime(3),
                Fin = dr.IsDBNull(4) ? null : dr.GetDateTime(4),
                Estado = dr.GetString(5),
                Archivo = dr.GetString(6),
                Tamano = dr.GetInt64(7),
                Detalle = dr.GetString(8)
            });
        }

        return lista;
    }
}
