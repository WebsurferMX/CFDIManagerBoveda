using CFDIParser.Models;
using Npgsql;

namespace CFDIParser.Services;

public partial class PostgreSqlRepository
{
    /// <summary>Recibos de nómina emitidos por la empresa con fecha de emisión en [desde, hasta).</summary>
    public List<NominaCfdiDto> ObtenerNominasEmitidas(long empresaId, DateTime desde, DateTime hasta)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT cfdi_id, uuid, fecha_emision, COALESCE(estado_sat, ''), COALESCE(ruta_xml, '')
            FROM cfdi
            WHERE empresa_id = @empresa AND clasificacion = 'EMITIDO' AND tipo_cfdi = 'N'
              AND fecha_emision >= @desde AND fecha_emision < @hasta
            ORDER BY fecha_emision;", conn);
        cmd.Parameters.AddWithValue("empresa", empresaId);
        cmd.Parameters.AddWithValue("desde", desde);
        cmd.Parameters.AddWithValue("hasta", hasta);

        var lista = new List<NominaCfdiDto>();
        using var dr = cmd.ExecuteReader();
        while (dr.Read())
        {
            lista.Add(new NominaCfdiDto
            {
                CfdiId = dr.GetInt64(0),
                Uuid = dr.GetString(1).ToUpperInvariant(),
                FechaEmision = dr.GetDateTime(2),
                EstadoSat = dr.GetString(3),
                RutaXml = dr.GetString(4)
            });
        }

        return lista;
    }

    /// <summary>Años con recibos de nómina emitidos (por fecha de emisión), del más reciente al más antiguo.</summary>
    public List<int> ObtenerAniosNomina(long empresaId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT EXTRACT(YEAR FROM fecha_emision)::int
            FROM cfdi
            WHERE empresa_id = @empresa AND clasificacion = 'EMITIDO' AND tipo_cfdi = 'N'
            ORDER BY 1 DESC;", conn);
        cmd.Parameters.AddWithValue("empresa", empresaId);

        var anios = new List<int>();
        using var dr = cmd.ExecuteReader();
        while (dr.Read())
            anios.Add(dr.GetInt32(0));
        return anios;
    }
}
