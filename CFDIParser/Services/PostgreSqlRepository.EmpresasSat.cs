using CFDIParser.Models;
using Npgsql;

namespace CFDIParser.Services;
using CFDIManager.Web.Models;

public partial class PostgreSqlRepository
{
    public EmpresaSatDto? ObtenerEmpresaSat(long empresaId)
    {

        using var cn = new NpgsqlConnection(_connectionString);cn.Open();

        const string sql = """
            select *
            from empresas_sat
            where empresa_id = @empresa_id
            """;

        using var cmd = new NpgsqlCommand(sql, cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);

        using var dr = cmd.ExecuteReader();

        if (!dr.Read())
            return null;

        return new EmpresaSatDto
        {
            EmpresaId = dr.GetInt64(dr.GetOrdinal("empresa_id")),
            TipoAccesoSat = dr["tipo_acceso_sat"]?.ToString() ?? "EFIRMA",
            SatUsuario = dr["sat_usuario"]?.ToString(),
            SatPassword = dr["sat_password"]?.ToString(),
            RutaCer = dr["ruta_cer"]?.ToString(),
            RutaKey = dr["ruta_key"]?.ToString(),
            PasswordKey = dr["password_key"]?.ToString(),
            
            FechaVencimiento =
                dr["fecha_vencimiento"] switch
                {
                    DBNull => null,
                    DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                    DateTime dt => dt,
                    _ => null
                },


            
            UsaValidacionSat =
                Convert.ToBoolean(dr["usa_validacion_sat"]),
            UsaDescargaSat =
                Convert.ToBoolean(dr["usa_descarga_sat"]),
            UsaEfos =
                Convert.ToBoolean(dr["usa_efos"]),
            UsaXmlAzulejeras =
                Convert.ToBoolean(dr["usa_xml_azulejeras"]),

            DescargaAutomatica =
                Convert.ToBoolean(dr["descarga_automatica"]),
            DescargaEmitidos =
                Convert.ToBoolean(dr["descarga_emitidos"]),
            DescargaRecibidos =
                Convert.ToBoolean(dr["descarga_recibidos"]),
            DescargaMetadatos =
                Convert.ToBoolean(dr["descarga_metadatos"]),
            DescargaDesde =
                dr["descarga_desde"] switch
                {
                    DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                    DateTime dt => dt,
                    _ => null
                },

            CiecUltimaPrueba =
                dr["ciec_ultima_prueba"] is DateTime prueba ? prueba : null,
            CiecResultado =
                dr["ciec_resultado"] is string resultado ? resultado : null
        };
    }

    /// <summary>Columnas de la prueba de la CIEC. Idempotente; se llama al arrancar.</summary>
    public void AsegurarCiec()
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            ALTER TABLE empresas_sat ADD COLUMN IF NOT EXISTS ciec_ultima_prueba TIMESTAMP;
            ALTER TABLE empresas_sat ADD COLUMN IF NOT EXISTS ciec_resultado TEXT;", cn);

        cmd.ExecuteNonQuery();
    }

    /// <summary>Guarda el resultado de probar la CIEC sin tocar el resto del acceso SAT.</summary>
    public void RegistrarPruebaCiec(long empresaId, string resultado)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            UPDATE empresas_sat
            SET ciec_ultima_prueba = NOW(),
                ciec_resultado = @resultado
            WHERE empresa_id = @empresa_id;", cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("resultado", resultado);

        cmd.ExecuteNonQuery();
    }

    /// <summary>Cambia solo la ubicación de la e.firma (al pasarla a la carpeta por RFC).</summary>
    public void ActualizarRutasFiel(long empresaId, string rutaCer, string rutaKey)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            UPDATE empresas_sat
            SET ruta_cer = @ruta_cer,
                ruta_key = @ruta_key
            WHERE empresa_id = @empresa_id;", cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("ruta_cer", rutaCer);
        cmd.Parameters.AddWithValue("ruta_key", rutaKey);

        cmd.ExecuteNonQuery();
    }

    /// <summary>Cambia solo la contraseña de la e.firma (al cifrar las guardadas en claro).</summary>
    public void ActualizarPasswordFiel(long empresaId, string passwordKey)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            UPDATE empresas_sat
            SET password_key = @password_key
            WHERE empresa_id = @empresa_id;", cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("password_key", passwordKey);

        cmd.ExecuteNonQuery();
    }

    /// <summary>Columnas de la descarga automática del SAT. Idempotente; se llama al arrancar.</summary>
    public void AsegurarDescargaAutomatica()
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            ALTER TABLE empresas_sat ADD COLUMN IF NOT EXISTS descarga_automatica BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE empresas_sat ADD COLUMN IF NOT EXISTS descarga_emitidos BOOLEAN NOT NULL DEFAULT TRUE;
            ALTER TABLE empresas_sat ADD COLUMN IF NOT EXISTS descarga_recibidos BOOLEAN NOT NULL DEFAULT TRUE;
            ALTER TABLE empresas_sat ADD COLUMN IF NOT EXISTS descarga_metadatos BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE empresas_sat ADD COLUMN IF NOT EXISTS descarga_desde DATE;", cn);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Enciende o apaga la descarga automática sin tocar el resto del acceso SAT
    /// (switch de Administración de XML). Si no tenía fecha de inicio, usa <paramref name="desdeSiFalta"/>.
    /// </summary>
    public void CambiarDescargaAutomatica(long empresaId, bool activa, DateTime desdeSiFalta)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            UPDATE empresas_sat
            SET descarga_automatica = @activa,
                descarga_desde = COALESCE(descarga_desde, @desde),
                fecha_actualizacion = NOW()
            WHERE empresa_id = @empresa_id;", cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("activa", activa);
        cmd.Parameters.AddWithValue("desde", DateOnly.FromDateTime(desdeSiFalta));

        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("La empresa no tiene configurado su acceso SAT (Empresas → Acceso SAT).");
    }

public void GuardarEmpresaSat(EmpresaSatDto modelo)
{
    using var cn = new NpgsqlConnection(_connectionString);
    cn.Open();

    const string sql = """
        INSERT INTO empresas_sat
        (
            empresa_id,
            tipo_acceso_sat,
            sat_usuario,
            sat_password,
            ruta_cer,
            ruta_key,
            password_key,
            fecha_vencimiento,
            usa_validacion_sat,
            usa_descarga_sat,
            usa_efos,
            usa_xml_azulejeras,
            descarga_automatica,
            descarga_emitidos,
            descarga_recibidos,
            descarga_metadatos,
            descarga_desde
        )
        VALUES
        (
            @empresa_id,
            @tipo_acceso_sat,
            @sat_usuario,
            @sat_password,
            @ruta_cer,
            @ruta_key,
            @password_key,
            @fecha_vencimiento,
            @usa_validacion_sat,
            @usa_descarga_sat,
            @usa_efos,
            @usa_xml_azulejeras,
            @descarga_automatica,
            @descarga_emitidos,
            @descarga_recibidos,
            @descarga_metadatos,
            @descarga_desde
        )
        ON CONFLICT (empresa_id)
        DO UPDATE SET
            tipo_acceso_sat=@tipo_acceso_sat,
            sat_usuario=@sat_usuario,
            sat_password=@sat_password,
            ruta_cer=@ruta_cer,
            ruta_key=@ruta_key,
            password_key=@password_key,
            fecha_vencimiento=@fecha_vencimiento,
            usa_validacion_sat=@usa_validacion_sat,
            usa_descarga_sat=@usa_descarga_sat,
            usa_efos=@usa_efos,
            usa_xml_azulejeras=@usa_xml_azulejeras,
            descarga_automatica=@descarga_automatica,
            descarga_emitidos=@descarga_emitidos,
            descarga_recibidos=@descarga_recibidos,
            descarga_metadatos=@descarga_metadatos,
            descarga_desde=@descarga_desde,
            fecha_actualizacion=NOW()
        """;

    using var cmd = new NpgsqlCommand(sql, cn);

    cmd.Parameters.AddWithValue("empresa_id", modelo.EmpresaId);
    cmd.Parameters.AddWithValue("tipo_acceso_sat", modelo.TipoAccesoSat);

    cmd.Parameters.AddWithValue(
        "sat_usuario",
        (object?)modelo.SatUsuario ?? DBNull.Value);

    cmd.Parameters.AddWithValue(
        "sat_password",
        (object?)modelo.SatPassword ?? DBNull.Value);

    cmd.Parameters.AddWithValue(
        "ruta_cer",
        (object?)modelo.RutaCer ?? DBNull.Value);

    cmd.Parameters.AddWithValue(
        "ruta_key",
        (object?)modelo.RutaKey ?? DBNull.Value);

    cmd.Parameters.AddWithValue(
        "password_key",
        (object?)modelo.PasswordKey ?? DBNull.Value);

    cmd.Parameters.AddWithValue(
        "fecha_vencimiento",
        (object?)modelo.FechaVencimiento ?? DBNull.Value);

    cmd.Parameters.AddWithValue(
        "usa_validacion_sat",
        modelo.UsaValidacionSat);

    cmd.Parameters.AddWithValue(
        "usa_descarga_sat",
        modelo.UsaDescargaSat);

    cmd.Parameters.AddWithValue(
        "usa_efos",
        modelo.UsaEfos);

    cmd.Parameters.AddWithValue(
        "usa_xml_azulejeras",
        modelo.UsaXmlAzulejeras);

    cmd.Parameters.AddWithValue("descarga_automatica", modelo.DescargaAutomatica);
    cmd.Parameters.AddWithValue("descarga_emitidos", modelo.DescargaEmitidos);
    cmd.Parameters.AddWithValue("descarga_recibidos", modelo.DescargaRecibidos);
    cmd.Parameters.AddWithValue("descarga_metadatos", modelo.DescargaMetadatos);

    cmd.Parameters.AddWithValue(
        "descarga_desde",
        modelo.DescargaDesde is DateTime desde
            ? (object)DateOnly.FromDateTime(desde)
            : DBNull.Value);

    cmd.ExecuteNonQuery();
}

public EmpresaSatDto? ObtenerAccesoSat(long empresaId)
{
    using var conn = new NpgsqlConnection(_connectionString);

    conn.Open();

    const string sql = @"
        SELECT
            empresa_id,
            password_key,
            ruta_cer,
            ruta_key
        FROM empresas_sat
        WHERE empresa_id = @empresa_id
        LIMIT 1";

    using var cmd = new NpgsqlCommand(sql, conn);

    cmd.Parameters.AddWithValue(
        "empresa_id",
        empresaId);

    using var reader = cmd.ExecuteReader();

    if (!reader.Read())
        return null;

    return new EmpresaSatDto
    {
        EmpresaId = reader.GetInt64(0),
        PasswordKey = reader.IsDBNull(1)
            ? null
            : reader.GetString(1),
        RutaCer = reader.IsDBNull(2)
            ? null
            : reader.GetString(2),
        RutaKey = reader.IsDBNull(3)
            ? null
            : reader.GetString(3)
    };
}

}
