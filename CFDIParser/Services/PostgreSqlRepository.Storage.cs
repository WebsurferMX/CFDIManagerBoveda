using Npgsql;

namespace CFDIParser.Services;

/// <summary>
/// Seguimiento de los XML en la carpeta de cada empresa (Storage), para que la base
/// funcione como índice de esa carpeta: solo se procesan archivos nuevos o modificados.
/// </summary>
public partial class PostgreSqlRepository
{
    private bool _tablasStorageListas;

    public void AsegurarTablasStorage()
    {
        if (_tablasStorageListas)
        {
            return;
        }

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS storage_archivos (
                ruta TEXT PRIMARY KEY,
                empresa_id BIGINT NOT NULL REFERENCES empresas (empresa_id) ON DELETE CASCADE,
                uuid TEXT,
                modificado TIMESTAMP NOT NULL,
                tamano BIGINT NOT NULL,
                estatus TEXT NOT NULL,
                detalle TEXT,
                revisado TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS storage_archivos_empresa ON storage_archivos (empresa_id);
            CREATE INDEX IF NOT EXISTS storage_archivos_uuid ON storage_archivos (empresa_id, uuid);", conn);

        cmd.ExecuteNonQuery();

        _tablasStorageListas = true;
    }

    /// <summary>Archivos ya revisados de la empresa: ruta → (fecha de modificación, tamaño).</summary>
    public Dictionary<string, (DateTime Modificado, long Tamano)> ObtenerArchivosStorage(long empresaId)
    {
        AsegurarTablasStorage();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand("SELECT ruta, modificado, tamano FROM storage_archivos WHERE empresa_id = @empresa_id;", conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);

        var archivos = new Dictionary<string, (DateTime, long)>(StringComparer.OrdinalIgnoreCase);

        using NpgsqlDataReader dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            archivos[dr.GetString(0)] = (dr.GetDateTime(1), dr.GetInt64(2));
        }

        return archivos;
    }

    public void RegistrarArchivoStorage(
        long empresaId,
        string ruta,
        string? uuid,
        DateTime modificado,
        long tamano,
        string estatus,
        string? detalle)
    {
        AsegurarTablasStorage();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO storage_archivos (ruta, empresa_id, uuid, modificado, tamano, estatus, detalle, revisado)
            VALUES (@ruta, @empresa_id, @uuid, @modificado, @tamano, @estatus, @detalle, NOW())
            ON CONFLICT (ruta) DO UPDATE SET
                empresa_id = EXCLUDED.empresa_id, uuid = EXCLUDED.uuid, modificado = EXCLUDED.modificado,
                tamano = EXCLUDED.tamano, estatus = EXCLUDED.estatus, detalle = EXCLUDED.detalle, revisado = NOW();", conn);

        cmd.Parameters.AddWithValue("@ruta", ruta);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@uuid", string.IsNullOrWhiteSpace(uuid) ? DBNull.Value : uuid.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@modificado", modificado);
        cmd.Parameters.AddWithValue("@tamano", tamano);
        cmd.Parameters.AddWithValue("@estatus", estatus);
        cmd.Parameters.AddWithValue("@detalle", (object?)detalle ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void QuitarArchivosStorage(long empresaId, IReadOnlyCollection<string> rutas)
    {
        if (rutas.Count == 0)
        {
            return;
        }

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand("DELETE FROM storage_archivos WHERE empresa_id = @empresa_id AND ruta = ANY(@rutas);", conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@rutas", rutas.ToArray());
        cmd.ExecuteNonQuery();
    }

    /// <summary>Otra copia registrada del mismo UUID en la carpeta de la empresa, si existe.</summary>
    public string? BuscarOtraCopiaStorage(long empresaId, string uuid, string rutaExcluida)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT ruta FROM storage_archivos
            WHERE empresa_id = @empresa_id AND uuid = UPPER(@uuid) AND ruta <> @ruta
              AND estatus IN ('CORRECTO', 'EXISTENTE', 'ACTUALIZADO')
            ORDER BY ruta
            LIMIT 1;", conn);

        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@uuid", uuid);
        cmd.Parameters.AddWithValue("@ruta", rutaExcluida);

        return cmd.ExecuteScalar() as string;
    }

    /// <summary>CFDI de la empresa cuyo XML está (o estaba) dentro de la carpeta indicada.</summary>
    public List<(long CfdiId, string Uuid, string Clasificacion, string Ruta)> ObtenerCfdiEnCarpeta(long empresaId, string carpeta)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        string prefijo = carpeta.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

        using var cmd = new NpgsqlCommand(@"
            SELECT cfdi_id, uuid, COALESCE(clasificacion, ''), ruta_xml
            FROM cfdi
            WHERE empresa_id = @empresa_id
              AND LOWER(ruta_xml) LIKE LOWER(@prefijo) || '%';", conn);

        // LIKE: escapar comodines que pueda traer la ruta.
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@prefijo", prefijo.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_"));

        var lista = new List<(long, string, string, string)>();

        using NpgsqlDataReader dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            lista.Add((dr.GetInt64(0), dr.GetString(1), dr.GetString(2), dr.GetString(3)));
        }

        return lista;
    }

    /// <summary>
    /// Elimina un CFDI cuyo XML ya no existe. cfdi_impuestos no borra en cascada desde
    /// cfdi_conceptos, así que se borra primero; el resto de tablas hijas sí cascada.
    /// </summary>
    public void EliminarCfdi(long empresaId, long cfdiId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using NpgsqlTransaction tx = conn.BeginTransaction();

        using (var cmd = new NpgsqlCommand(@"
            DELETE FROM cfdi_impuestos
            WHERE concepto_id IN (
                SELECT k.concepto_id FROM cfdi_conceptos k
                INNER JOIN cfdi c ON c.cfdi_id = k.cfdi_id
                WHERE c.cfdi_id = @cfdi_id AND c.empresa_id = @empresa_id);", conn, tx))
        {
            cmd.Parameters.AddWithValue("@cfdi_id", cfdiId);
            cmd.Parameters.AddWithValue("@empresa_id", empresaId);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new NpgsqlCommand("DELETE FROM cfdi WHERE cfdi_id = @cfdi_id AND empresa_id = @empresa_id;", conn, tx))
        {
            cmd.Parameters.AddWithValue("@cfdi_id", cfdiId);
            cmd.Parameters.AddWithValue("@empresa_id", empresaId);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }
}
