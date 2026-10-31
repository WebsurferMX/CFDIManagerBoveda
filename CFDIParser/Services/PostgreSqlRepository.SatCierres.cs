using CFDIParser.Models;
using Npgsql;

namespace CFDIParser.Services;

// Registro de los cierres de mes del SAT (tabla sat_cierres, creada en AsegurarSatMetadatos).
public partial class PostgreSqlRepository
{
    public void GuardarCierre(SatCierreDto c)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO sat_cierres (empresa_id, periodo, inicio, fin, estado, metadatos_emitidos, metadatos_recibidos,
                                     xml_importados, cancelados_sin_xml, otros_sin_xml, detalle, automatico)
            VALUES (@empresa_id, @periodo, @inicio, @fin, @estado, @metadatos_emitidos, @metadatos_recibidos,
                    @xml_importados, @cancelados_sin_xml, @otros_sin_xml, @detalle, @automatico)
            ON CONFLICT (empresa_id, periodo) DO UPDATE SET
                inicio = EXCLUDED.inicio, fin = EXCLUDED.fin, estado = EXCLUDED.estado,
                metadatos_emitidos = EXCLUDED.metadatos_emitidos, metadatos_recibidos = EXCLUDED.metadatos_recibidos,
                xml_importados = EXCLUDED.xml_importados, cancelados_sin_xml = EXCLUDED.cancelados_sin_xml,
                otros_sin_xml = EXCLUDED.otros_sin_xml, detalle = EXCLUDED.detalle, automatico = EXCLUDED.automatico", cn);

        cmd.Parameters.AddWithValue("empresa_id", c.EmpresaId);
        cmd.Parameters.AddWithValue("periodo", c.Periodo);
        cmd.Parameters.AddWithValue("inicio", c.Inicio);
        cmd.Parameters.AddWithValue("fin", (object?)c.Fin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("estado", c.Estado);
        cmd.Parameters.AddWithValue("metadatos_emitidos", c.MetadatosEmitidos);
        cmd.Parameters.AddWithValue("metadatos_recibidos", c.MetadatosRecibidos);
        cmd.Parameters.AddWithValue("xml_importados", c.XmlImportados);
        cmd.Parameters.AddWithValue("cancelados_sin_xml", c.CanceladosSinXml);
        cmd.Parameters.AddWithValue("otros_sin_xml", c.OtrosSinXml);
        cmd.Parameters.AddWithValue("detalle", c.Detalle);
        cmd.Parameters.AddWithValue("automatico", c.Automatico);
        cmd.ExecuteNonQuery();
    }

    public SatCierreDto? ObtenerCierre(long empresaId, int periodo) =>
        LeerCierres("WHERE empresa_id = @empresa_id AND periodo = @periodo", empresaId, periodo, 1).FirstOrDefault();

    public List<SatCierreDto> ObtenerCierres(long empresaId, int cantidad) =>
        LeerCierres("WHERE empresa_id = @empresa_id", empresaId, 0, cantidad);

    private List<SatCierreDto> LeerCierres(string condicion, long empresaId, int periodo, int cantidad)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand($"SELECT * FROM sat_cierres {condicion} ORDER BY periodo DESC LIMIT @cantidad", cn);
        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("periodo", periodo);
        cmd.Parameters.AddWithValue("cantidad", cantidad);

        using var dr = cmd.ExecuteReader();
        var resultado = new List<SatCierreDto>();

        while (dr.Read())
        {
            resultado.Add(new SatCierreDto
            {
                EmpresaId = empresaId,
                Periodo = Convert.ToInt32(dr["periodo"]),
                Inicio = Convert.ToDateTime(dr["inicio"]),
                Fin = dr["fin"] is DBNull ? null : Convert.ToDateTime(dr["fin"]),
                Estado = dr["estado"].ToString() ?? "",
                MetadatosEmitidos = Convert.ToInt32(dr["metadatos_emitidos"]),
                MetadatosRecibidos = Convert.ToInt32(dr["metadatos_recibidos"]),
                XmlImportados = Convert.ToInt32(dr["xml_importados"]),
                CanceladosSinXml = Convert.ToInt32(dr["cancelados_sin_xml"]),
                OtrosSinXml = Convert.ToInt32(dr["otros_sin_xml"]),
                Detalle = dr["detalle"].ToString() ?? "",
                Automatico = Convert.ToBoolean(dr["automatico"])
            });
        }

        return resultado;
    }
}
