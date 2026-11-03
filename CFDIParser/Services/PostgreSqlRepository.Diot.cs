using CFDIParser.Models;
using Npgsql;

namespace CFDIParser.Services;

public partial class PostgreSqlRepository
{
    /// <summary>
    /// CFDI recibidos no cancelados para calcular la DIOT de [desde, hasta): facturas que no son PPD y notas de crédito
    /// emitidas en el periodo, y complementos de pago emitidos desde el inicio del periodo hasta
    /// <paramref name="hastaPagos"/> (el complemento puede llegar meses después del pago; la fecha de pago se lee del XML).
    /// </summary>
    public List<DiotComprobanteDto> ObtenerComprobantesDiot(long empresaId, DateTime desde, DateTime hasta, DateTime hastaPagos)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT c.cfdi_id, c.uuid, c.tipo_cfdi, COALESCE(c.serie, ''), COALESCE(c.folio, ''), c.fecha_emision,
                   emisor.rfc, emisor.nombre, COALESCE(c.metodo_pago, ''), COALESCE(c.moneda, ''), c.tipo_cambio,
                   COALESCE(iva.base, 0), COALESCE(iva.importe, 0), COALESCE(c.ruta_xml, '')
            FROM cfdi c
            INNER JOIN personas_fiscales emisor ON emisor.persona_id = c.emisor_id
            LEFT JOIN LATERAL (
                SELECT SUM(g.base) AS base, SUM(g.importe) AS importe
                FROM cfdi_impuestos_globales g
                WHERE g.cfdi_id = c.cfdi_id AND g.tipo = 'TRASLADO' AND g.impuesto = '002' AND g.tasa = 0.16
            ) iva ON TRUE
            WHERE c.empresa_id = @empresa
              AND c.clasificacion = 'RECIBIDO'
              AND COALESCE(c.estado_sat, '') NOT ILIKE 'cancel%'
              AND (
                    (c.tipo_cfdi = 'I' AND COALESCE(c.metodo_pago, '') <> 'PPD' AND c.fecha_emision >= @desde AND c.fecha_emision < @hasta)
                 OR (c.tipo_cfdi = 'E' AND c.fecha_emision >= @desde AND c.fecha_emision < @hasta)
                 OR (c.tipo_cfdi = 'P' AND c.fecha_emision >= @desde AND c.fecha_emision < @hasta_pagos)
              );", conn);
        cmd.Parameters.AddWithValue("empresa", empresaId);
        cmd.Parameters.AddWithValue("desde", desde);
        cmd.Parameters.AddWithValue("hasta", hasta);
        cmd.Parameters.AddWithValue("hasta_pagos", hastaPagos);

        var lista = new List<DiotComprobanteDto>();
        using var dr = cmd.ExecuteReader();
        while (dr.Read())
        {
            lista.Add(new DiotComprobanteDto
            {
                CfdiId = dr.GetInt64(0),
                Uuid = dr.GetString(1).ToUpperInvariant(),
                Tipo = dr.GetString(2).Trim(),
                Serie = dr.GetString(3),
                Folio = dr.GetString(4),
                FechaEmision = dr.GetDateTime(5),
                RfcEmisor = dr.GetString(6).Trim().ToUpperInvariant(),
                NombreEmisor = dr.GetString(7),
                MetodoPago = dr.GetString(8),
                Moneda = dr.GetString(9),
                TipoCambio = dr.IsDBNull(10) ? null : dr.GetDecimal(10),
                Base16 = dr.GetDecimal(11),
                Iva16 = dr.GetDecimal(12),
                RutaXml = dr.GetString(13)
            });
        }

        return lista;
    }

    /// <summary>CFDI recibidos (de cualquier tipo) emitidos en [desde, hasta): para saber si la bóveda tiene el mes.</summary>
    public int ContarRecibidos(long empresaId, DateTime desde, DateTime hasta)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM cfdi
            WHERE empresa_id = @empresa AND clasificacion = 'RECIBIDO' AND fecha_emision >= @desde AND fecha_emision < @hasta;", conn);
        cmd.Parameters.AddWithValue("empresa", empresaId);
        cmd.Parameters.AddWithValue("desde", desde);
        cmd.Parameters.AddWithValue("hasta", hasta);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
