using CFDIParser.Models;
using Npgsql;

namespace CFDIParser.Services;

// Configuración del servidor SMTP y bitácora de correos enviados (CorreoService en la web).
public partial class PostgreSqlRepository
{
    /// <summary>Tablas de correo. Idempotente; se llama al arrancar.</summary>
    public void AsegurarCorreo()
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS configuracion_correo (
                configuracion_id INTEGER PRIMARY KEY DEFAULT 1 CHECK (configuracion_id = 1),
                servidor TEXT NOT NULL DEFAULT '',
                puerto INTEGER NOT NULL DEFAULT 587,
                seguridad TEXT NOT NULL DEFAULT 'AUTO',
                usuario TEXT NOT NULL DEFAULT '',
                password TEXT,
                remitente_email TEXT NOT NULL DEFAULT '',
                remitente_nombre TEXT NOT NULL DEFAULT 'CFDI Manager',
                url_aplicacion TEXT NOT NULL DEFAULT '',
                habilitado BOOLEAN NOT NULL DEFAULT FALSE,
                fecha_actualizacion TIMESTAMP NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS correos_enviados (
                correo_id BIGSERIAL PRIMARY KEY,
                fecha TIMESTAMP NOT NULL DEFAULT NOW(),
                tipo TEXT NOT NULL,
                destinatarios TEXT NOT NULL,
                asunto TEXT NOT NULL,
                adjuntos TEXT NOT NULL DEFAULT '',
                enviado BOOLEAN NOT NULL,
                error TEXT NOT NULL DEFAULT '',
                enviado_por TEXT NOT NULL DEFAULT '',
                empresa_id BIGINT
            );

            CREATE INDEX IF NOT EXISTS ix_correos_enviados_fecha ON correos_enviados (fecha DESC);", cn);

        cmd.ExecuteNonQuery();
    }

    public ConfiguracionCorreoDto ObtenerConfiguracionCorreo()
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand("SELECT * FROM configuracion_correo WHERE configuracion_id = 1", cn);
        using var dr = cmd.ExecuteReader();

        if (!dr.Read())
            return new ConfiguracionCorreoDto();

        return new ConfiguracionCorreoDto
        {
            Servidor = dr["servidor"].ToString() ?? "",
            Puerto = Convert.ToInt32(dr["puerto"]),
            Seguridad = dr["seguridad"].ToString() ?? "AUTO",
            Usuario = dr["usuario"].ToString() ?? "",
            Password = dr["password"] as string,
            RemitenteEmail = dr["remitente_email"].ToString() ?? "",
            RemitenteNombre = dr["remitente_nombre"].ToString() ?? "",
            UrlAplicacion = dr["url_aplicacion"].ToString() ?? "",
            Habilitado = Convert.ToBoolean(dr["habilitado"]),
            FechaActualizacion = Convert.ToDateTime(dr["fecha_actualizacion"])
        };
    }

    public void GuardarConfiguracionCorreo(ConfiguracionCorreoDto c)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO configuracion_correo (configuracion_id, servidor, puerto, seguridad, usuario, password,
                                              remitente_email, remitente_nombre, url_aplicacion, habilitado, fecha_actualizacion)
            VALUES (1, @servidor, @puerto, @seguridad, @usuario, @password,
                    @remitente_email, @remitente_nombre, @url_aplicacion, @habilitado, NOW())
            ON CONFLICT (configuracion_id) DO UPDATE SET
                servidor = EXCLUDED.servidor,
                puerto = EXCLUDED.puerto,
                seguridad = EXCLUDED.seguridad,
                usuario = EXCLUDED.usuario,
                password = EXCLUDED.password,
                remitente_email = EXCLUDED.remitente_email,
                remitente_nombre = EXCLUDED.remitente_nombre,
                url_aplicacion = EXCLUDED.url_aplicacion,
                habilitado = EXCLUDED.habilitado,
                fecha_actualizacion = NOW()", cn);

        cmd.Parameters.AddWithValue("servidor", c.Servidor ?? "");
        cmd.Parameters.AddWithValue("puerto", c.Puerto);
        cmd.Parameters.AddWithValue("seguridad", c.Seguridad ?? "AUTO");
        cmd.Parameters.AddWithValue("usuario", c.Usuario ?? "");
        cmd.Parameters.AddWithValue("password", (object?)c.Password ?? DBNull.Value);
        cmd.Parameters.AddWithValue("remitente_email", c.RemitenteEmail ?? "");
        cmd.Parameters.AddWithValue("remitente_nombre", c.RemitenteNombre ?? "");
        cmd.Parameters.AddWithValue("url_aplicacion", c.UrlAplicacion ?? "");
        cmd.Parameters.AddWithValue("habilitado", c.Habilitado);
        cmd.ExecuteNonQuery();
    }

    public void RegistrarCorreoEnviado(CorreoEnviadoDto correo)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO correos_enviados (tipo, destinatarios, asunto, adjuntos, enviado, error, enviado_por, empresa_id)
            VALUES (@tipo, @destinatarios, @asunto, @adjuntos, @enviado, @error, @enviado_por, @empresa_id)", cn);

        cmd.Parameters.AddWithValue("tipo", correo.Tipo);
        cmd.Parameters.AddWithValue("destinatarios", correo.Destinatarios);
        cmd.Parameters.AddWithValue("asunto", correo.Asunto);
        cmd.Parameters.AddWithValue("adjuntos", correo.Adjuntos ?? "");
        cmd.Parameters.AddWithValue("enviado", correo.Enviado);
        cmd.Parameters.AddWithValue("error", correo.Error ?? "");
        cmd.Parameters.AddWithValue("enviado_por", correo.EnviadoPor ?? "");
        cmd.Parameters.AddWithValue("empresa_id", (object?)correo.EmpresaId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<CorreoEnviadoDto> ObtenerCorreosEnviados(int cantidad)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(
            "SELECT * FROM correos_enviados ORDER BY fecha DESC LIMIT @cantidad", cn);
        cmd.Parameters.AddWithValue("cantidad", cantidad);

        using var dr = cmd.ExecuteReader();
        var resultado = new List<CorreoEnviadoDto>();

        while (dr.Read())
        {
            resultado.Add(new CorreoEnviadoDto
            {
                CorreoId = Convert.ToInt64(dr["correo_id"]),
                Fecha = Convert.ToDateTime(dr["fecha"]),
                Tipo = dr["tipo"].ToString() ?? "",
                Destinatarios = dr["destinatarios"].ToString() ?? "",
                Asunto = dr["asunto"].ToString() ?? "",
                Adjuntos = dr["adjuntos"].ToString() ?? "",
                Enviado = Convert.ToBoolean(dr["enviado"]),
                Error = dr["error"].ToString() ?? "",
                EnviadoPor = dr["enviado_por"].ToString() ?? "",
                EmpresaId = dr["empresa_id"] is DBNull ? null : Convert.ToInt64(dr["empresa_id"])
            });
        }

        return resultado;
    }
}
