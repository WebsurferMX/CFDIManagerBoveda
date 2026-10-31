using CFDIParser.Models;
using Npgsql;
using NpgsqlTypes;

namespace CFDIParser.Services;

// Metadatos de la descarga masiva del SAT por mes: el lado "SAT" de la conciliación SINUBE vs SAT.
public partial class PostgreSqlRepository
{
    /// <summary>Tablas de metadatos del SAT. Idempotente; se llama al arrancar.</summary>
    public void AsegurarSatMetadatos()
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS sat_metadatos (
                empresa_id BIGINT NOT NULL REFERENCES empresas (empresa_id) ON DELETE CASCADE,
                recibido BOOLEAN NOT NULL,
                uuid TEXT NOT NULL,
                rfc_emisor TEXT NOT NULL DEFAULT '',
                nombre_emisor TEXT NOT NULL DEFAULT '',
                rfc_receptor TEXT NOT NULL DEFAULT '',
                nombre_receptor TEXT NOT NULL DEFAULT '',
                fecha_emision TIMESTAMP NOT NULL,
                monto NUMERIC(18,2) NOT NULL DEFAULT 0,
                efecto TEXT NOT NULL DEFAULT '',
                estatus TEXT NOT NULL DEFAULT '',
                fecha_cancelacion TIMESTAMP,
                PRIMARY KEY (empresa_id, recibido, uuid)
            );

            CREATE INDEX IF NOT EXISTS ix_sat_metadatos_fecha ON sat_metadatos (empresa_id, recibido, fecha_emision);

            CREATE TABLE IF NOT EXISTS sat_metadatos_periodos (
                empresa_id BIGINT NOT NULL REFERENCES empresas (empresa_id) ON DELETE CASCADE,
                recibido BOOLEAN NOT NULL,
                periodo INTEGER NOT NULL,
                solicitud_id TEXT NOT NULL,
                registros INTEGER NOT NULL,
                cubre_hasta TIMESTAMP NOT NULL,
                consultado TIMESTAMP NOT NULL DEFAULT NOW(),
                PRIMARY KEY (empresa_id, recibido, periodo)
            );

            CREATE TABLE IF NOT EXISTS sat_cierres (
                empresa_id BIGINT NOT NULL REFERENCES empresas (empresa_id) ON DELETE CASCADE,
                periodo INTEGER NOT NULL,
                inicio TIMESTAMP NOT NULL,
                fin TIMESTAMP,
                estado TEXT NOT NULL,
                metadatos_emitidos INTEGER NOT NULL DEFAULT 0,
                metadatos_recibidos INTEGER NOT NULL DEFAULT 0,
                xml_importados INTEGER NOT NULL DEFAULT 0,
                cancelados_sin_xml INTEGER NOT NULL DEFAULT 0,
                otros_sin_xml INTEGER NOT NULL DEFAULT 0,
                detalle TEXT NOT NULL DEFAULT '',
                automatico BOOLEAN NOT NULL DEFAULT FALSE,
                PRIMARY KEY (empresa_id, periodo)
            );", cn);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Reemplaza los metadatos del mes con los de una solicitud (foto completa: así se reflejan
    /// cancelaciones y CFDI que ya no aparecen). Carga masiva con COPY binario.
    /// </summary>
    public void GuardarMetadatosPeriodo(
        long empresaId, bool recibido, int periodo, string solicitudId, DateTime cubreHasta, IReadOnlyCollection<SatMetadatoDto> metadatos)
    {
        var desde = new DateTime(periodo / 100, periodo % 100, 1);

        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();
        using var tx = cn.BeginTransaction();

        using (var borrar = new NpgsqlCommand(@"
            DELETE FROM sat_metadatos
            WHERE empresa_id = @empresa_id AND recibido = @recibido
              AND fecha_emision >= @desde AND fecha_emision < @hasta", cn, tx))
        {
            borrar.Parameters.AddWithValue("empresa_id", empresaId);
            borrar.Parameters.AddWithValue("recibido", recibido);
            borrar.Parameters.AddWithValue("desde", desde);
            borrar.Parameters.AddWithValue("hasta", desde.AddMonths(1));
            borrar.ExecuteNonQuery();
        }

        // Un UUID repetido en dos paquetes rompería la llave primaria del COPY.
        IEnumerable<SatMetadatoDto> unicos = metadatos
            .Where(m => m.FechaEmision >= desde && m.FechaEmision < desde.AddMonths(1))
            .GroupBy(m => m.Uuid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last());

        int registros = 0;

        using (NpgsqlBinaryImporter copia = cn.BeginBinaryImport(@"
            COPY sat_metadatos (empresa_id, recibido, uuid, rfc_emisor, nombre_emisor, rfc_receptor, nombre_receptor,
                                fecha_emision, monto, efecto, estatus, fecha_cancelacion) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (SatMetadatoDto m in unicos)
            {
                copia.StartRow();
                copia.Write(empresaId, NpgsqlDbType.Bigint);
                copia.Write(recibido, NpgsqlDbType.Boolean);
                copia.Write(m.Uuid.ToUpperInvariant(), NpgsqlDbType.Text);
                copia.Write(m.RfcEmisor, NpgsqlDbType.Text);
                copia.Write(m.NombreEmisor, NpgsqlDbType.Text);
                copia.Write(m.RfcReceptor, NpgsqlDbType.Text);
                copia.Write(m.NombreReceptor, NpgsqlDbType.Text);
                copia.Write(m.FechaEmision, NpgsqlDbType.Timestamp);
                copia.Write(m.Monto, NpgsqlDbType.Numeric);
                copia.Write(m.Efecto, NpgsqlDbType.Text);
                copia.Write(m.Estatus, NpgsqlDbType.Text);

                if (m.FechaCancelacion is DateTime fc)
                    copia.Write(fc, NpgsqlDbType.Timestamp);
                else
                    copia.WriteNull();

                registros++;
            }

            copia.Complete();
        }

        using (var periodoCmd = new NpgsqlCommand(@"
            INSERT INTO sat_metadatos_periodos (empresa_id, recibido, periodo, solicitud_id, registros, cubre_hasta, consultado)
            VALUES (@empresa_id, @recibido, @periodo, @solicitud_id, @registros, @cubre_hasta, NOW())
            ON CONFLICT (empresa_id, recibido, periodo) DO UPDATE SET
                solicitud_id = EXCLUDED.solicitud_id, registros = EXCLUDED.registros,
                cubre_hasta = EXCLUDED.cubre_hasta, consultado = NOW()", cn, tx))
        {
            periodoCmd.Parameters.AddWithValue("empresa_id", empresaId);
            periodoCmd.Parameters.AddWithValue("recibido", recibido);
            periodoCmd.Parameters.AddWithValue("periodo", periodo);
            periodoCmd.Parameters.AddWithValue("solicitud_id", solicitudId);
            periodoCmd.Parameters.AddWithValue("registros", registros);
            periodoCmd.Parameters.AddWithValue("cubre_hasta", cubreHasta);
            periodoCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public SatMetadatosPeriodoDto? ObtenerMetadatosPeriodo(long empresaId, bool recibido, int periodo)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT solicitud_id, registros, cubre_hasta, consultado FROM sat_metadatos_periodos
            WHERE empresa_id = @empresa_id AND recibido = @recibido AND periodo = @periodo", cn);
        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("recibido", recibido);
        cmd.Parameters.AddWithValue("periodo", periodo);

        using var dr = cmd.ExecuteReader();

        return dr.Read()
            ? new SatMetadatosPeriodoDto
            {
                EmpresaId = empresaId,
                Recibidos = recibido,
                Periodo = periodo,
                SolicitudId = dr.GetString(0),
                Registros = dr.GetInt32(1),
                CubreHasta = dr.GetDateTime(2),
                Consultado = dr.GetDateTime(3)
            }
            : null;
    }

    public List<SatMetadatoDto> ObtenerMetadatos(long empresaId, bool recibido, DateTime desde, DateTime hasta)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT uuid, rfc_emisor, nombre_emisor, rfc_receptor, nombre_receptor, fecha_emision,
                   monto, efecto, estatus, fecha_cancelacion
            FROM sat_metadatos
            WHERE empresa_id = @empresa_id AND recibido = @recibido
              AND fecha_emision >= @desde AND fecha_emision < @hasta", cn);
        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("recibido", recibido);
        cmd.Parameters.AddWithValue("desde", desde);
        cmd.Parameters.AddWithValue("hasta", hasta);

        using var dr = cmd.ExecuteReader();
        var resultado = new List<SatMetadatoDto>();

        while (dr.Read())
        {
            resultado.Add(new SatMetadatoDto
            {
                Uuid = dr.GetString(0),
                RfcEmisor = dr.GetString(1),
                NombreEmisor = dr.GetString(2),
                RfcReceptor = dr.GetString(3),
                NombreReceptor = dr.GetString(4),
                FechaEmision = dr.GetDateTime(5),
                Monto = dr.GetDecimal(6),
                Efecto = dr.GetString(7),
                Estatus = dr.GetString(8),
                FechaCancelacion = dr.IsDBNull(9) ? null : dr.GetDateTime(9)
            });
        }

        return resultado;
    }
}
