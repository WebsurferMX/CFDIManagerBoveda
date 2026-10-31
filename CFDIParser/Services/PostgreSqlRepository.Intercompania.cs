using Npgsql;

namespace CFDIParser.Services;

/// <summary>
/// CFDI entre empresas del mismo grupo: el mismo UUID se registra una vez por empresa
/// (emitido en una, recibido en la otra).
/// </summary>
public partial class PostgreSqlRepository
{
    /// <summary>
    /// Cambia la unicidad del UUID de global a por empresa. Idempotente: crea primero el
    /// índice único (empresa_id, uuid) y luego quita UNIQUE (uuid), en una transacción.
    /// </summary>
    public void AsegurarUuidPorEmpresa()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using NpgsqlTransaction tx = conn.BeginTransaction();

        using var cmd = new NpgsqlCommand(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ux_cfdi_empresa_uuid ON cfdi (empresa_id, uuid);
            CREATE INDEX IF NOT EXISTS ix_cfdi_uuid ON cfdi (uuid);
            ALTER TABLE cfdi DROP CONSTRAINT IF EXISTS cfdi_uuid_key;", conn, tx);

        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// CFDI registrados en una empresa cuyo emisor o receptor es otra empresa activa
    /// que todavía no lo tiene registrado.
    /// </summary>
    public List<(long EmpresaDestinoId, string RutaXml, string Uuid)> ObtenerIntercompaniasPendientes()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT ON (destino.empresa_id, UPPER(c.uuid))
                   destino.empresa_id, c.ruta_xml, c.uuid
            FROM cfdi c
            INNER JOIN personas_fiscales pe ON pe.persona_id = c.emisor_id
            INNER JOIN personas_fiscales pr ON pr.persona_id = c.receptor_id
            INNER JOIN empresas destino
                    ON destino.activa
                   AND destino.empresa_id <> c.empresa_id
                   AND UPPER(TRIM(destino.rfc)) IN (UPPER(TRIM(pe.rfc)), UPPER(TRIM(pr.rfc)))
            WHERE COALESCE(c.ruta_xml, '') <> ''
              AND NOT EXISTS (
                  SELECT 1 FROM cfdi ya
                  WHERE ya.empresa_id = destino.empresa_id AND UPPER(ya.uuid) = UPPER(c.uuid))
            ORDER BY destino.empresa_id, UPPER(c.uuid), c.cfdi_id;", conn);

        var lista = new List<(long, string, string)>();

        using NpgsqlDataReader dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            lista.Add((dr.GetInt64(0), RutaXmlLocal.Resolver(dr.GetString(1)), dr.GetString(2)));
        }

        return lista;
    }
}
