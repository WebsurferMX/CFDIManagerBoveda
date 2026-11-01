using CFDIParser.Models;
using Npgsql;

namespace CFDIParser.Services;

/// <summary>Re-sincronizar con la bóveda (Herramientas): rehace lo que sale del XML sin tocar validaciones.</summary>
public partial class PostgreSqlRepository
{
    /// <summary>
    /// Reescribe con el XML los datos de un CFDI ya registrado: columnas, emisor/receptor, conceptos,
    /// impuestos y relacionados, y su ruta. Conserva estado SAT, validaciones, verificados y su cfdi_id.
    /// Devuelve false si el UUID no está registrado en la empresa.
    /// </summary>
    public bool RehacerDatosCfdi(long empresaId, CfdiDto cfdi, string clasificacion, string rutaXml)
    {
        ArgumentNullException.ThrowIfNull(cfdi);

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        using NpgsqlTransaction tx = conn.BeginTransaction();

        long cfdiId;

        using (var buscar = new NpgsqlCommand("SELECT cfdi_id FROM cfdi WHERE empresa_id = @empresa_id AND uuid = @uuid;", conn, tx))
        {
            buscar.Parameters.AddWithValue("@empresa_id", empresaId);
            buscar.Parameters.AddWithValue("@uuid", cfdi.UUID);

            if (buscar.ExecuteScalar() is not long id)
            {
                tx.Rollback();
                return false;
            }

            cfdiId = id;
        }

        long emisorId = ObtenerOCrearPersona(conn, tx, cfdi.RFCEmisor, cfdi.NombreEmisor, "EMISOR");
        long receptorId = ObtenerOCrearPersona(conn, tx, cfdi.RFCReceptor, cfdi.NombreReceptor, "RECEPTOR");

        using (var cmd = new NpgsqlCommand(@"
            UPDATE cfdi SET
                tipo_cfdi = @tipo_cfdi, emisor_id = @emisor_id, receptor_id = @receptor_id,
                serie = @serie, folio = @folio, fecha_emision = @fecha_emision, subtotal = @subtotal,
                descuento = @descuento, total = @total, moneda = @moneda, tipo_cambio = @tipo_cambio,
                clasificacion = @clasificacion, ruta_xml = @ruta_xml,
                monto_total_pagos = @monto_total_pagos, version_cfdi = @version_cfdi, forma_pago = @forma_pago,
                metodo_pago = @metodo_pago, condiciones_pago = @condiciones_pago, lugar_expedicion = @lugar_expedicion,
                exportacion = @exportacion, no_certificado = @no_certificado, certificado = @certificado,
                sello_cfdi = @sello_cfdi, confirmacion = @confirmacion, regimen_fiscal_emisor = @regimen_fiscal_emisor,
                fac_atr_adquirente = @fac_atr_adquirente, domicilio_fiscal_receptor = @domicilio_fiscal_receptor,
                regimen_fiscal_receptor = @regimen_fiscal_receptor, uso_cfdi = @uso_cfdi,
                residencia_fiscal = @residencia_fiscal, num_reg_id_trib = @num_reg_id_trib,
                total_impuestos_trasladados = @total_impuestos_trasladados,
                total_impuestos_retenidos = @total_impuestos_retenidos, fecha_timbrado = @fecha_timbrado,
                rfc_prov_certif = @rfc_prov_certif, no_certificado_sat = @no_certificado_sat,
                version_timbre = @version_timbre, sello_cfd = @sello_cfd, sello_sat = @sello_sat
            WHERE cfdi_id = @cfdi_id;", conn, tx))
        {
            cmd.Parameters.AddWithValue("@cfdi_id", cfdiId);
            cmd.Parameters.AddWithValue("@tipo_cfdi", cfdi.TipoComprobante);
            cmd.Parameters.AddWithValue("@emisor_id", emisorId);
            cmd.Parameters.AddWithValue("@receptor_id", receptorId);
            AgregarParametro(cmd, "@serie", cfdi.Serie);
            AgregarParametro(cmd, "@folio", cfdi.Folio);
            cmd.Parameters.AddWithValue("@fecha_emision", cfdi.FechaEmision);
            cmd.Parameters.AddWithValue("@subtotal", cfdi.SubTotal);
            AgregarParametro(cmd, "@descuento", cfdi.Descuento);
            cmd.Parameters.AddWithValue("@total", cfdi.Total);
            AgregarParametro(cmd, "@moneda", cfdi.Moneda);
            AgregarParametro(cmd, "@tipo_cambio", cfdi.TipoCambio);
            cmd.Parameters.AddWithValue("@clasificacion", clasificacion);
            cmd.Parameters.AddWithValue("@ruta_xml", rutaXml);
            AgregarParametro(cmd, "@monto_total_pagos", cfdi.MontoTotalPagos);
            AgregarParametro(cmd, "@version_cfdi", cfdi.Version);
            AgregarParametro(cmd, "@forma_pago", cfdi.FormaPago);
            AgregarParametro(cmd, "@metodo_pago", cfdi.MetodoPago);
            AgregarParametro(cmd, "@condiciones_pago", cfdi.CondicionesDePago);
            AgregarParametro(cmd, "@lugar_expedicion", cfdi.LugarExpedicion);
            AgregarParametro(cmd, "@exportacion", cfdi.Exportacion);
            AgregarParametro(cmd, "@no_certificado", cfdi.NoCertificado);
            AgregarParametro(cmd, "@certificado", cfdi.Certificado);
            AgregarParametro(cmd, "@sello_cfdi", cfdi.Sello);
            AgregarParametro(cmd, "@confirmacion", cfdi.Confirmacion);
            AgregarParametro(cmd, "@regimen_fiscal_emisor", cfdi.RegimenFiscalEmisor);
            AgregarParametro(cmd, "@fac_atr_adquirente", cfdi.FacAtrAdquirente);
            AgregarParametro(cmd, "@domicilio_fiscal_receptor", cfdi.DomicilioFiscalReceptor);
            AgregarParametro(cmd, "@regimen_fiscal_receptor", cfdi.RegimenFiscalReceptor);
            AgregarParametro(cmd, "@uso_cfdi", cfdi.UsoCfdi);
            AgregarParametro(cmd, "@residencia_fiscal", cfdi.ResidenciaFiscal);
            AgregarParametro(cmd, "@num_reg_id_trib", cfdi.NumRegIdTrib);
            AgregarParametro(cmd, "@total_impuestos_trasladados", cfdi.TotalImpuestosTrasladados);
            AgregarParametro(cmd, "@total_impuestos_retenidos", cfdi.TotalImpuestosRetenidos);
            AgregarParametro(cmd, "@fecha_timbrado", cfdi.FechaTimbrado);
            AgregarParametro(cmd, "@rfc_prov_certif", cfdi.RfcProvCertif);
            AgregarParametro(cmd, "@no_certificado_sat", cfdi.NoCertificadoSat);
            AgregarParametro(cmd, "@version_timbre", cfdi.VersionTimbre);
            AgregarParametro(cmd, "@sello_cfd", cfdi.SelloCfd);
            AgregarParametro(cmd, "@sello_sat", cfdi.SelloSat);
            cmd.ExecuteNonQuery();
        }

        // Lo que depende del XML se borra y se vuelve a insertar. cfdi_impuestos no cascada desde conceptos.
        using (var borrar = new NpgsqlCommand(@"
            DELETE FROM cfdi_impuestos WHERE concepto_id IN (SELECT concepto_id FROM cfdi_conceptos WHERE cfdi_id = @cfdi_id);
            DELETE FROM cfdi_conceptos WHERE cfdi_id = @cfdi_id;
            DELETE FROM cfdi_impuestos_globales WHERE cfdi_id = @cfdi_id;
            DELETE FROM cfdi_relacionados WHERE cfdi_id = @cfdi_id;", conn, tx))
        {
            borrar.Parameters.AddWithValue("@cfdi_id", cfdiId);
            borrar.ExecuteNonQuery();
        }

        InsertarConceptos(conn, tx, cfdiId, cfdi.Conceptos);
        GuardarImpuestosGlobales(conn, tx, cfdiId, cfdi.Impuestos);
        GuardarCfdiRelacionados(conn, tx, cfdiId, cfdi.Relacionados);

        tx.Commit();
        return true;
    }

    /// <summary>Id, UUID y ruta guardada de todos los CFDI de la empresa (para buscar los que no tienen XML).</summary>
    public List<(long CfdiId, string Uuid, string Ruta)> ObtenerRutasCfdi(long empresaId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand("SELECT cfdi_id, uuid, COALESCE(ruta_xml, '') FROM cfdi WHERE empresa_id = @empresa_id;", conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);

        var lista = new List<(long, string, string)>();
        using NpgsqlDataReader dr = cmd.ExecuteReader();

        while (dr.Read())
            lista.Add((dr.GetInt64(0), dr.GetString(1), dr.GetString(2)));

        return lista;
    }
}
