using CFDIParser.Models;
using Npgsql;

namespace CFDIParser.Services;

// Lado SAT de la conciliación SINUBE vs SAT: los CFDI de la bóveda local.
public partial class PostgreSqlRepository
{
    /// <summary>UUID (en mayúsculas) de todos los CFDI de la empresa; para omitir de golpe lo que ya está en la bóveda.</summary>
    public HashSet<string> ObtenerUuidsEmpresa(long empresaId)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand("SELECT UPPER(uuid) FROM cfdi WHERE empresa_id = @empresa_id", cn);
        cmd.Parameters.AddWithValue("empresa_id", empresaId);

        using var dr = cmd.ExecuteReader();
        var resultado = new HashSet<string>(StringComparer.Ordinal);
        while (dr.Read()) resultado.Add(dr.GetString(0));
        return resultado;
    }

    /// <summary>
    /// CFDI de la empresa con fecha de emisión en [desde, hasta). clasificacion: RECIBIDO (la contraparte
    /// es el emisor/proveedor) o EMITIDO (la contraparte es el receptor/cliente).
    /// </summary>
    public List<CfdiConciliacionDto> ObtenerCfdiConciliacion(long empresaId, string clasificacion, DateTime desde, DateTime hasta)
    {
        string contraparte = clasificacion == "EMITIDO" ? "c.receptor_id" : "c.emisor_id";

        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand($@"
            SELECT c.cfdi_id, c.uuid, COALESCE(c.tipo_cfdi, '') AS tipo_cfdi, c.fecha_emision,
                   COALESCE(c.serie, '') AS serie, COALESCE(c.folio, '') AS folio,
                   COALESCE(c.total, 0) AS total, COALESCE(c.moneda, '') AS moneda,
                   COALESCE(c.estado_sat, '') AS estado_sat, COALESCE(c.rfc_prov_certif, '') AS rfc_pac,
                   p.rfc AS rfc_contraparte, COALESCE(p.nombre, '') AS nombre_contraparte
            FROM cfdi c
            INNER JOIN personas_fiscales p ON p.persona_id = {contraparte}
            WHERE c.empresa_id = @empresa_id
              AND c.clasificacion = @clasificacion
              AND c.fecha_emision >= @desde
              AND c.fecha_emision < @hasta
            ORDER BY c.fecha_emision", cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("clasificacion", clasificacion);
        cmd.Parameters.AddWithValue("desde", desde);
        cmd.Parameters.AddWithValue("hasta", hasta);

        using var dr = cmd.ExecuteReader();
        var resultado = new List<CfdiConciliacionDto>();

        while (dr.Read())
        {
            resultado.Add(new CfdiConciliacionDto
            {
                CfdiId = dr.GetInt64(0),
                Uuid = dr.GetString(1),
                TipoCfdi = dr.GetString(2).Trim(),
                FechaEmision = dr.GetDateTime(3),
                Serie = dr.GetString(4),
                Folio = dr.GetString(5),
                Total = dr.GetDecimal(6),
                Moneda = dr.GetString(7),
                EstadoSat = dr.GetString(8),
                RfcPac = dr.GetString(9),
                RfcContraparte = dr.GetString(10),
                NombreContraparte = dr.GetString(11)
            });
        }

        return resultado;
    }
}
