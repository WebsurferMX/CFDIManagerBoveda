using CFDIParser.Models;
using Npgsql;
using NpgsqlTypes;

namespace CFDIParser.Services;

/// <summary>
/// Lista del artículo 69-B del CFF (EFOS) y su cruce con los CFDI de la empresa.
/// </summary>
public partial class PostgreSqlRepository
{
    private bool _tablasEfosListas;

    /// <summary>Columnas de fechas y oficios del listado completo. Aditivo: no cambia lo existente.</summary>
    public void AsegurarTablasEfos()
    {
        if (_tablasEfosListas)
        {
            return;
        }

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            ALTER TABLE efos_sat ADD COLUMN IF NOT EXISTS oficio_presuncion TEXT;
            ALTER TABLE efos_sat ADD COLUMN IF NOT EXISTS pub_presunto_sat DATE;
            ALTER TABLE efos_sat ADD COLUMN IF NOT EXISTS pub_presunto_dof DATE;
            ALTER TABLE efos_sat ADD COLUMN IF NOT EXISTS pub_desvirtuado_dof DATE;
            ALTER TABLE efos_sat ADD COLUMN IF NOT EXISTS oficio_definitivo TEXT;
            ALTER TABLE efos_sat ADD COLUMN IF NOT EXISTS pub_definitivo_sat DATE;
            ALTER TABLE efos_sat ADD COLUMN IF NOT EXISTS pub_definitivo_dof DATE;
            ALTER TABLE efos_sat ADD COLUMN IF NOT EXISTS pub_favorable_dof DATE;
            ALTER TABLE efos_sat ADD COLUMN IF NOT EXISTS procedimientos INTEGER NOT NULL DEFAULT 1;
            CREATE INDEX IF NOT EXISTS efos_sat_rfc_upper ON efos_sat (UPPER(TRIM(rfc)));

            CREATE TABLE IF NOT EXISTS efos_sat_cargas (
                carga_id SERIAL PRIMARY KEY,
                fecha_corte DATE,
                fecha_carga TIMESTAMP NOT NULL DEFAULT NOW(),
                origen TEXT NOT NULL,
                registros INTEGER NOT NULL
            );", conn);

        cmd.ExecuteNonQuery();

        _tablasEfosListas = true;
    }

    /// <summary>
    /// Sustituye la lista completa en una sola transacción: el listado del SAT es
    /// completo, así que lo que no venga en él ya no está en la lista.
    /// </summary>
    public void ReemplazarListadoEfos(
        IReadOnlyCollection<EfosRegistroDto> registros,
        DateTime? fechaCorte,
        string origen)
    {
        AsegurarTablasEfos();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using NpgsqlTransaction tx = conn.BeginTransaction();

        using (var borrar = new NpgsqlCommand("DELETE FROM efos_sat;", conn, tx))
        {
            borrar.ExecuteNonQuery();
        }

        DateTime ahora = DateTime.Now;

        using (NpgsqlBinaryImporter copia = conn.BeginBinaryImport(@"
            COPY efos_sat (rfc, razon_social, situacion, fecha_publicacion, fecha_importacion,
                           oficio_presuncion, pub_presunto_sat, pub_presunto_dof, pub_desvirtuado_dof,
                           oficio_definitivo, pub_definitivo_sat, pub_definitivo_dof, pub_favorable_dof, procedimientos)
            FROM STDIN (FORMAT BINARY)"))
        {
            foreach (EfosRegistroDto r in registros)
            {
                // fecha_publicacion: la publicación en el DOF de la situación vigente.
                DateTime? publicacion = r.Situacion switch
                {
                    "DEFINITIVO" => r.PublicacionDefinitivoDof,
                    "DESVIRTUADO" => r.PublicacionDesvirtuadoDof,
                    "FAVORABLE" => r.PublicacionFavorableDof,
                    _ => r.PublicacionPresuntoDof
                };

                copia.StartRow();
                copia.Write(r.Rfc, NpgsqlDbType.Varchar);
                copia.Write(r.RazonSocial, NpgsqlDbType.Text);
                copia.Write(r.Situacion, NpgsqlDbType.Varchar);
                EscribirFecha(copia, publicacion);
                copia.Write(ahora, NpgsqlDbType.Timestamp);
                copia.Write(r.OficioPresuncion, NpgsqlDbType.Text);
                EscribirFecha(copia, r.PublicacionPresuntoSat);
                EscribirFecha(copia, r.PublicacionPresuntoDof);
                EscribirFecha(copia, r.PublicacionDesvirtuadoDof);
                copia.Write(r.OficioDefinitivo, NpgsqlDbType.Text);
                EscribirFecha(copia, r.PublicacionDefinitivoSat);
                EscribirFecha(copia, r.PublicacionDefinitivoDof);
                EscribirFecha(copia, r.PublicacionFavorableDof);
                copia.Write(r.Procedimientos, NpgsqlDbType.Integer);
            }

            copia.Complete();
        }

        using (var carga = new NpgsqlCommand(
                   "INSERT INTO efos_sat_cargas (fecha_corte, origen, registros) VALUES (@corte, @origen, @registros);", conn, tx))
        {
            carga.Parameters.AddWithValue("@corte", fechaCorte.HasValue ? fechaCorte.Value.Date : DBNull.Value);
            carga.Parameters.AddWithValue("@origen", origen);
            carga.Parameters.AddWithValue("@registros", registros.Count);
            carga.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static void EscribirFecha(NpgsqlBinaryImporter copia, DateTime? fecha)
    {
        if (fecha is DateTime f)
        {
            copia.Write(f.Date, NpgsqlDbType.Date);
        }
        else
        {
            copia.WriteNull();
        }
    }

    public EfosListadoEstadoDto ObtenerEstadoListadoEfos()
    {
        AsegurarTablasEfos();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        var estado = new EfosListadoEstadoDto();

        using (var cmd = new NpgsqlCommand($"WITH {ListaEfectivaEfos} SELECT situacion, COUNT(*) FROM lista GROUP BY situacion;", conn))
        using (NpgsqlDataReader dr = cmd.ExecuteReader())
        {
            while (dr.Read())
            {
                estado.PorSituacion[dr.GetString(0)] = Convert.ToInt32(dr.GetValue(1));
            }
        }

        using (var cmd = new NpgsqlCommand(
                   "SELECT fecha_corte, fecha_carga, origen FROM efos_sat_cargas ORDER BY carga_id DESC LIMIT 1;", conn))
        using (NpgsqlDataReader dr = cmd.ExecuteReader())
        {
            if (dr.Read())
            {
                estado.FechaCorte = dr.IsDBNull(0) ? null : dr.GetDateTime(0);
                estado.FechaCarga = dr.GetDateTime(1);
                estado.Origen = dr.GetString(2);
            }
        }

        return estado;
    }

    // La carga guarda un registro por RFC con su procedimiento más grave (efos_sat tiene RFC único).
    // DISTINCT ON protege igual si hubiera registros repetidos de cargas anteriores.
    private const string ListaEfectivaEfos = @"
        lista AS (
            SELECT DISTINCT ON (UPPER(TRIM(rfc)))
                   UPPER(TRIM(rfc)) AS rfc, razon_social, situacion,
                   pub_presunto_dof, pub_definitivo_dof, pub_definitivo_sat, pub_desvirtuado_dof, pub_favorable_dof,
                   COALESCE(procedimientos, 1) AS procedimientos
            FROM efos_sat
            ORDER BY UPPER(TRIM(rfc)),
                     CASE situacion WHEN 'DEFINITIVO' THEN 0 WHEN 'PRESUNTO' THEN 1 WHEN 'DESVIRTUADO' THEN 2 ELSE 3 END,
                     COALESCE(pub_definitivo_dof, pub_definitivo_sat, pub_favorable_dof, pub_desvirtuado_dof, pub_presunto_dof, pub_presunto_sat) DESC NULLS LAST
        )";

    // Lista efectiva y CFDI de la empresa con su contraparte (emisor si se recibió, receptor si se emitió).
    private static readonly string BaseCruceEfos = $@"
        WITH {ListaEfectivaEfos},
        movimientos AS (
            SELECT
                c.cfdi_id,
                c.clasificacion,
                c.tipo_cfdi,
                c.fecha_emision,
                c.uuid,
                COALESCE(c.serie, '') AS serie,
                COALESCE(c.folio, '') AS folio,
                COALESCE(c.estado_sat, '') AS estado_sat,
                COALESCE(c.ruta_xml, '') AS ruta_xml,
                COALESCE(c.total, 0) AS total,
                COALESCE(c.subtotal, 0) - COALESCE(c.descuento, 0) AS base,
                {ImpuestoGlobal("TRASLADO", "002")} AS iva,
                CASE WHEN c.tipo_cfdi = 'I' THEN 1 WHEN c.tipo_cfdi = 'E' THEN -1 ELSE 0 END AS signo,
                LOWER(COALESCE(c.estado_sat, '')) = 'cancelado' AS cancelado,
                UPPER(TRIM(CASE WHEN c.clasificacion = 'RECIBIDO' THEN emisor.rfc ELSE receptor.rfc END)) AS rfc_contraparte,
                CASE WHEN c.clasificacion = 'RECIBIDO' THEN emisor.nombre ELSE receptor.nombre END AS nombre_contraparte
            FROM cfdi c
            INNER JOIN personas_fiscales emisor ON emisor.persona_id = c.emisor_id
            INNER JOIN personas_fiscales receptor ON receptor.persona_id = c.receptor_id
            WHERE c.empresa_id = @empresa_id
              AND c.clasificacion IN ('EMITIDO', 'RECIBIDO')
        )";

    /// <summary>Coincidencias por RFC listado y por clasificación (recibidos = riesgo EDOS).</summary>
    public List<EfosCoincidenciaDto> ObtenerCoincidenciasEfos(long empresaId)
    {
        AsegurarTablasEfos();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(BaseCruceEfos + @"
            SELECT
                l.rfc,
                COALESCE(MAX(m.nombre_contraparte), MAX(l.razon_social)) AS nombre,
                l.situacion,
                m.clasificacion,
                l.pub_presunto_dof, l.pub_definitivo_dof, l.pub_definitivo_sat, l.pub_desvirtuado_dof, l.pub_favorable_dof,
                l.procedimientos,
                COUNT(*) AS cfdi,
                COUNT(*) FILTER (WHERE m.cancelado) AS cancelados,
                COALESCE(SUM(m.signo * m.total) FILTER (WHERE NOT m.cancelado), 0) AS importe,
                COALESCE(SUM(m.signo * m.base) FILTER (WHERE NOT m.cancelado), 0) AS base,
                COALESCE(SUM(m.signo * m.iva) FILTER (WHERE NOT m.cancelado), 0) AS iva,
                MIN(m.fecha_emision) AS primero,
                MAX(m.fecha_emision) AS ultimo,
                COUNT(*) FILTER (WHERE NOT m.cancelado
                                   AND l.situacion = 'DEFINITIVO'
                                   AND m.fecha_emision::date > COALESCE(l.pub_definitivo_dof, l.pub_definitivo_sat)) AS posteriores
            FROM movimientos m
            INNER JOIN lista l ON l.rfc = m.rfc_contraparte
            GROUP BY l.rfc, l.situacion, m.clasificacion, l.procedimientos,
                     l.pub_presunto_dof, l.pub_definitivo_dof, l.pub_definitivo_sat, l.pub_desvirtuado_dof, l.pub_favorable_dof
            ORDER BY
                CASE l.situacion WHEN 'DEFINITIVO' THEN 0 WHEN 'PRESUNTO' THEN 1 ELSE 2 END,
                m.clasificacion DESC,
                importe DESC;", conn);

        cmd.Parameters.AddWithValue("@empresa_id", empresaId);

        var lista = new List<EfosCoincidenciaDto>();

        using NpgsqlDataReader dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            DateTime? Fecha(string c) => dr.IsDBNull(dr.GetOrdinal(c)) ? null : dr.GetDateTime(dr.GetOrdinal(c));

            lista.Add(new EfosCoincidenciaDto
            {
                Rfc = Texto(dr, "rfc"),
                Nombre = Texto(dr, "nombre"),
                Situacion = Texto(dr, "situacion"),
                Clasificacion = Texto(dr, "clasificacion"),
                PublicacionPresuntoDof = Fecha("pub_presunto_dof"),
                PublicacionDefinitivoDof = Fecha("pub_definitivo_dof"),
                PublicacionDefinitivoSat = Fecha("pub_definitivo_sat"),
                Procedimientos = Convert.ToInt32(dr["procedimientos"]),
                PublicacionDesvirtuadoDof = Fecha("pub_desvirtuado_dof"),
                PublicacionFavorableDof = Fecha("pub_favorable_dof"),
                Cfdi = Convert.ToInt32(dr["cfdi"]),
                Cancelados = Convert.ToInt32(dr["cancelados"]),
                ImporteNeto = Decimal(dr, "importe"),
                BaseNeta = Decimal(dr, "base"),
                IvaNeto = Decimal(dr, "iva"),
                PrimerCfdi = dr.GetDateTime(dr.GetOrdinal("primero")),
                UltimoCfdi = dr.GetDateTime(dr.GetOrdinal("ultimo")),
                PosterioresAlDefinitivo = Convert.ToInt32(dr["posteriores"])
            });
        }

        return lista;
    }

    public int ContarCfdiEmpresa(long empresaId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM cfdi WHERE empresa_id = @empresa_id;", conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>CFDI de la empresa con un RFC de la lista (como emisor o como receptor).</summary>
    public List<EfosCfdiDto> ObtenerCfdiEfos(long empresaId, string rfc, string clasificacion)
    {
        AsegurarTablasEfos();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(BaseCruceEfos + @"
            SELECT m.*
            FROM movimientos m
            WHERE m.rfc_contraparte = UPPER(TRIM(@rfc))
              AND m.clasificacion = @clasificacion
            ORDER BY m.fecha_emision DESC;", conn);

        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@rfc", rfc);
        cmd.Parameters.AddWithValue("@clasificacion", clasificacion);

        var lista = new List<EfosCfdiDto>();

        using NpgsqlDataReader dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            lista.Add(new EfosCfdiDto
            {
                CfdiId = dr.GetInt64(dr.GetOrdinal("cfdi_id")),
                Clasificacion = Texto(dr, "clasificacion"),
                FechaEmision = dr.GetDateTime(dr.GetOrdinal("fecha_emision")),
                UUID = Texto(dr, "uuid"),
                TipoCfdi = Texto(dr, "tipo_cfdi"),
                Serie = Texto(dr, "serie"),
                Folio = Texto(dr, "folio"),
                Base = Decimal(dr, "base"),
                Iva = Decimal(dr, "iva"),
                Total = Decimal(dr, "total"),
                EstadoSat = Texto(dr, "estado_sat"),
                RutaXml = RutaXmlLocal.Resolver(Texto(dr, "ruta_xml"))
            });
        }

        return lista;
    }
}
