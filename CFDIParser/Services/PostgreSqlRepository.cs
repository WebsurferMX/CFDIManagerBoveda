using Npgsql;
using CFDIParser.Models;

namespace CFDIParser.Services;

public partial class PostgreSqlRepository
{
    private readonly string _connectionString;

    public PostgreSqlRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "La cadena de conexión no puede estar vacía.",
                nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public long GuardarCfdi(
        long empresaId,
        CfdiDto cfdi,
        string clasificacion,
        string rutaXml)
    {
        ArgumentNullException.ThrowIfNull(cfdi);

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        const string validarSql = @"
            SELECT cfdi_id
            FROM cfdi
            WHERE empresa_id = @empresa_id
              AND uuid = @uuid;";

        using var validarCmd = new NpgsqlCommand(validarSql, conn, tx);
        validarCmd.Parameters.AddWithValue("@empresa_id", empresaId);
        validarCmd.Parameters.AddWithValue("@uuid", cfdi.UUID);

        object? existente = validarCmd.ExecuteScalar();
        if (existente is not null)
        {
            tx.Commit();
            return Convert.ToInt64(existente);
        }

        long emisorId = ObtenerOCrearPersona(conn, tx,
            cfdi.RFCEmisor, cfdi.NombreEmisor, "EMISOR");
        long receptorId = ObtenerOCrearPersona(conn, tx,
            cfdi.RFCReceptor, cfdi.NombreReceptor, "RECEPTOR");

        const string sql = @"
            INSERT INTO cfdi
            (
                empresa_id, uuid, tipo_cfdi, emisor_id, receptor_id,
                serie, folio, fecha_emision, subtotal, descuento, total,
                moneda, tipo_cambio, clasificacion, ruta_xml,
                monto_total_pagos, version_cfdi, forma_pago, metodo_pago,
                condiciones_pago, lugar_expedicion, exportacion,
                no_certificado, certificado, sello_cfdi, confirmacion,
                regimen_fiscal_emisor, fac_atr_adquirente,
                domicilio_fiscal_receptor, regimen_fiscal_receptor,
                uso_cfdi, residencia_fiscal, num_reg_id_trib,
                total_impuestos_trasladados, total_impuestos_retenidos,
                fecha_timbrado, rfc_prov_certif, no_certificado_sat,
                version_timbre, sello_cfd, sello_sat
            )
            VALUES
            (
                @empresa_id, @uuid, @tipo_cfdi, @emisor_id, @receptor_id,
                @serie, @folio, @fecha_emision, @subtotal, @descuento, @total,
                @moneda, @tipo_cambio, @clasificacion, @ruta_xml,
                @monto_total_pagos, @version_cfdi, @forma_pago, @metodo_pago,
                @condiciones_pago, @lugar_expedicion, @exportacion,
                @no_certificado, @certificado, @sello_cfdi, @confirmacion,
                @regimen_fiscal_emisor, @fac_atr_adquirente,
                @domicilio_fiscal_receptor, @regimen_fiscal_receptor,
                @uso_cfdi, @residencia_fiscal, @num_reg_id_trib,
                @total_impuestos_trasladados, @total_impuestos_retenidos,
                @fecha_timbrado, @rfc_prov_certif, @no_certificado_sat,
                @version_timbre, @sello_cfd, @sello_sat
            )
            RETURNING cfdi_id;";

        using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@uuid", cfdi.UUID);
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

        long cfdiId = Convert.ToInt64(cmd.ExecuteScalar());
        GuardarImpuestosGlobales(conn, tx, cfdiId, cfdi.Impuestos);
        GuardarCfdiRelacionados(conn, tx, cfdiId, cfdi.Relacionados);
        tx.Commit();
        return cfdiId;
    }


    public void GuardarConceptos(
        long cfdiId,
        List<ConceptoDto> conceptos)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        const string validarSql = @"
            SELECT 1 FROM cfdi_conceptos
            WHERE cfdi_id = @cfdi_id LIMIT 1;";
        using var validarCmd = new NpgsqlCommand(validarSql, conn, tx);
        validarCmd.Parameters.AddWithValue("@cfdi_id", cfdiId);
        if (validarCmd.ExecuteScalar() is not null)
        {
            tx.Commit();
            return;
        }

        const string sql = @"
            INSERT INTO cfdi_conceptos
            (
                cfdi_id, clave_prod_serv, no_identificacion,
                descripcion, cantidad, clave_unidad, unidad,
                valor_unitario, importe, descuento, objeto_imp,
                cuenta_predial
            )
            VALUES
            (
                @cfdi_id, @clave_prod_serv, @no_identificacion,
                @descripcion, @cantidad, @clave_unidad, @unidad,
                @valor_unitario, @importe, @descuento, @objeto_imp,
                @cuenta_predial
            )
            RETURNING concepto_id;";

        foreach (ConceptoDto concepto in conceptos ?? new())
        {
            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@cfdi_id", cfdiId);
            AgregarParametro(cmd, "@clave_prod_serv", concepto.ClaveProdServ);
            AgregarParametro(cmd, "@no_identificacion", concepto.NoIdentificacion);
            AgregarParametro(cmd, "@descripcion", concepto.Descripcion);
            cmd.Parameters.AddWithValue("@cantidad", concepto.Cantidad);
            AgregarParametro(cmd, "@clave_unidad", concepto.ClaveUnidad);
            AgregarParametro(cmd, "@unidad", concepto.Unidad);
            cmd.Parameters.AddWithValue("@valor_unitario", concepto.ValorUnitario);
            cmd.Parameters.AddWithValue("@importe", concepto.Importe);
            AgregarParametro(cmd, "@descuento", concepto.Descuento);
            AgregarParametro(cmd, "@objeto_imp", concepto.ObjetoImp);
            AgregarParametro(cmd, "@cuenta_predial", concepto.CuentaPredial);
            long conceptoId = Convert.ToInt64(cmd.ExecuteScalar());
            GuardarImpuestos(conn, tx, conceptoId, concepto.Impuestos);
        }

        tx.Commit();
    }


    public void GuardarImpuestos(
        long conceptoId,
        List<ImpuestoDto> impuestos)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        GuardarImpuestos(conn, tx, conceptoId, impuestos);
        tx.Commit();
    }


    private static void GuardarImpuestos(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long conceptoId,
        IEnumerable<ImpuestoDto>? impuestos)
    {
        const string sql = @"
            INSERT INTO cfdi_impuestos
            (concepto_id, tipo, impuesto, base, tipo_factor, tasa, importe)
            VALUES
            (@concepto_id, @tipo, @impuesto, @base, @tipo_factor, @tasa, @importe);";

        foreach (ImpuestoDto impuesto in impuestos ?? Array.Empty<ImpuestoDto>())
        {
            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@concepto_id", conceptoId);
            AgregarParametro(cmd, "@tipo", impuesto.Tipo);
            AgregarParametro(cmd, "@impuesto", impuesto.Impuesto);
            cmd.Parameters.AddWithValue("@base", impuesto.Base);
            AgregarParametro(cmd, "@tipo_factor", impuesto.TipoFactor);
            cmd.Parameters.AddWithValue("@tasa", impuesto.Tasa);
            cmd.Parameters.AddWithValue("@importe", impuesto.Importe);
            cmd.ExecuteNonQuery();
        }
    }

    private static void GuardarImpuestosGlobales(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long cfdiId,
        IEnumerable<ImpuestoDto>? impuestos)
    {
        const string sql = @"
            INSERT INTO cfdi_impuestos_globales
            (cfdi_id, tipo, impuesto, base, tipo_factor, tasa, importe)
            VALUES
            (@cfdi_id, @tipo, @impuesto, @base, @tipo_factor, @tasa, @importe)
            ON CONFLICT DO NOTHING;";

        foreach (ImpuestoDto impuesto in impuestos ?? Array.Empty<ImpuestoDto>())
        {
            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@cfdi_id", cfdiId);
            AgregarParametro(cmd, "@tipo", impuesto.Tipo);
            AgregarParametro(cmd, "@impuesto", impuesto.Impuesto);
            cmd.Parameters.AddWithValue("@base", impuesto.Base);
            AgregarParametro(cmd, "@tipo_factor", impuesto.TipoFactor);
            cmd.Parameters.AddWithValue("@tasa", impuesto.Tasa);
            cmd.Parameters.AddWithValue("@importe", impuesto.Importe);
            cmd.ExecuteNonQuery();
        }
    }

    private static void GuardarCfdiRelacionados(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long cfdiId,
        IEnumerable<CfdiRelacionadoDto>? relacionados)
    {
        const string sql = @"
            INSERT INTO cfdi_relacionados
            (cfdi_id, tipo_relacion, uuid_relacionado)
            VALUES (@cfdi_id, @tipo_relacion, @uuid_relacionado)
            ON CONFLICT DO NOTHING;";

        foreach (CfdiRelacionadoDto relacionado in relacionados ?? Array.Empty<CfdiRelacionadoDto>())
        {
            if (string.IsNullOrWhiteSpace(relacionado.UUID)) continue;
            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@cfdi_id", cfdiId);
            AgregarParametro(cmd, "@tipo_relacion", relacionado.TipoRelacion);
            cmd.Parameters.AddWithValue("@uuid_relacionado", relacionado.UUID);
            cmd.ExecuteNonQuery();
        }
    }

    public void GuardarImportacion(
        string archivo,
        string uuid,
        string estatus,
        string? mensajeError)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            INSERT INTO cfdi_importaciones
            (
                archivo,
                uuid,
                fecha_importacion,
                estatus,
                mensaje_error
            )
            VALUES
            (
                @archivo,
                @uuid,
                NOW(),
                @estatus,
                @mensaje_error
            );";

        using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.AddWithValue("@archivo", archivo);
        cmd.Parameters.AddWithValue("@uuid", uuid);
        cmd.Parameters.AddWithValue("@estatus", estatus);
        cmd.Parameters.AddWithValue(
            "@mensaje_error",
            (object?)mensajeError ?? DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    public bool ExisteCfdi(long empresaId, string uuid)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            SELECT 1
            FROM cfdi
            WHERE empresa_id = @empresa_id
              AND uuid = @uuid
            LIMIT 1;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@uuid", uuid);

        return cmd.ExecuteScalar() != null;
    }

    private static long ObtenerOCrearPersona(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string rfc,
        string nombre,
        string tipo)
    {
        const string buscarSql = @"
            SELECT persona_id
            FROM personas_fiscales
            WHERE rfc = @rfc
            LIMIT 1;";
        using (var buscar = new NpgsqlCommand(buscarSql, conn, tx))
        {
            buscar.Parameters.AddWithValue("@rfc", rfc);
            object? existente = buscar.ExecuteScalar();
            if (existente is not null) return Convert.ToInt64(existente);
        }

        const string insertarSql = @"
            INSERT INTO personas_fiscales (rfc, nombre, tipo)
            VALUES (@rfc, @nombre, @tipo)
            RETURNING persona_id;";
        using var cmd = new NpgsqlCommand(insertarSql, conn, tx);
        cmd.Parameters.AddWithValue("@rfc", rfc);
        cmd.Parameters.AddWithValue("@nombre", nombre ?? "");
        cmd.Parameters.AddWithValue("@tipo", tipo);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public long ObtenerOCrearPersona(
        string rfc,
        string nombre,
        string tipo)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string validarSql = @"
            SELECT persona_id
            FROM personas_fiscales
            WHERE rfc = @rfc;";

        using var validarCmd = new NpgsqlCommand(validarSql, conn);
        validarCmd.Parameters.AddWithValue("@rfc", rfc);

        var existe = validarCmd.ExecuteScalar();

        if (existe != null)
        {
            return Convert.ToInt64(existe);
        }

        const string sql = @"
            INSERT INTO personas_fiscales
            (
                rfc,
                nombre,
                tipo
            )
            VALUES
            (
                @rfc,
                @nombre,
                @tipo
            )
            RETURNING persona_id;";

        using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.AddWithValue("@rfc", rfc);
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@tipo", tipo);

        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public List<ClienteFacturacionDto> ObtenerFacturacionClientes(
        long empresaId,
        DateTime fechaInicio,
        DateTime fechaFin)
    {
        var resultado = new List<ClienteFacturacionDto>();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            SELECT
                p.rfc,
                p.nombre,
                COUNT(*) AS cantidad_cfdi,
                COALESCE(SUM(c.total), 0) AS total_facturado,
                MIN(c.fecha_emision) AS primera_factura,
                MAX(c.fecha_emision) AS ultima_factura
            FROM cfdi c
            INNER JOIN personas_fiscales p
                ON c.receptor_id = p.persona_id
            WHERE c.empresa_id = @empresa_id
              AND c.fecha_emision >= @fecha_inicio
              AND c.fecha_emision < @fecha_fin_exclusiva
            GROUP BY
                p.persona_id,
                p.rfc,
                p.nombre
            ORDER BY
                total_facturado DESC;";

        using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@fecha_inicio", fechaInicio.Date);
        cmd.Parameters.AddWithValue("@fecha_fin_exclusiva", fechaFin.Date.AddDays(1));

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new ClienteFacturacionDto
                {
                    RFC = reader["rfc"]?.ToString() ?? "",
                    Nombre = reader["nombre"]?.ToString() ?? "",
                    CantidadCfdi = Convert.ToInt64(reader["cantidad_cfdi"]),
                    TotalFacturado = Convert.ToDecimal(reader["total_facturado"]),
                    PrimeraFactura = Convert.ToDateTime(reader["primera_factura"]),
                    UltimaFactura = Convert.ToDateTime(reader["ultima_factura"])
                });
        }

        return resultado;
    }

    public List<ResumenImportacionDto> ObtenerResumenImportaciones()
    {
        var resultado = new List<ResumenImportacionDto>();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            SELECT
                estatus,
                COUNT(*) AS cantidad
            FROM cfdi_importaciones
            GROUP BY estatus
            ORDER BY estatus;";

        using var cmd = new NpgsqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new ResumenImportacionDto
                {
                    Estatus = reader["estatus"]?.ToString() ?? "",
                    Cantidad = Convert.ToInt64(reader["cantidad"])
                });
        }

        return resultado;
    }

    public List<ImportacionDto> ObtenerUltimasImportaciones(int cantidad)
    {
        var resultado = new List<ImportacionDto>();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            SELECT
                importacion_id,
                archivo,
                uuid,
                fecha_importacion,
                estatus,
                mensaje_error
            FROM cfdi_importaciones
            ORDER BY importacion_id DESC
            LIMIT @cantidad;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@cantidad", cantidad);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new ImportacionDto
                {
                    ImportacionId = Convert.ToInt64(reader["importacion_id"]),
                    Archivo = reader["archivo"]?.ToString() ?? "",
                    UUID = reader["uuid"]?.ToString() ?? "",
                    FechaImportacion = Convert.ToDateTime(reader["fecha_importacion"]),
                    Estatus = reader["estatus"]?.ToString() ?? "",
                    MensajeError = reader["mensaje_error"] == DBNull.Value
                        ? ""
                        : reader["mensaje_error"]?.ToString() ?? ""
                });
        }

        return resultado;
    }

    public void ActualizarUbicacionCfdi(
        long empresaId,
        string uuid,
        string clasificacion,
        string rutaXml)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            UPDATE cfdi
            SET
                clasificacion = @clasificacion,
                ruta_xml = @ruta_xml
            WHERE empresa_id = @empresa_id
              AND uuid = @uuid;";

        using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@uuid", uuid);
        cmd.Parameters.AddWithValue("@clasificacion", clasificacion);
        cmd.Parameters.AddWithValue("@ruta_xml", rutaXml);

        cmd.ExecuteNonQuery();
    }

    public DashboardDto ObtenerDashboard(
        long empresaId,
        DateTime fechaInicio,
        DateTime fechaFin)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            SELECT
                COUNT(*) FILTER (WHERE clasificacion = 'EMITIDO') AS cfdi_emitidos,
                COUNT(*) FILTER (WHERE clasificacion = 'RECIBIDO') AS cfdi_recibidos,

                COUNT(*) FILTER (WHERE clasificacion = 'EMITIDO' AND tipo_cfdi = 'I') AS ingresos_emitidos,
                COUNT(*) FILTER (WHERE clasificacion = 'EMITIDO' AND tipo_cfdi = 'E') AS egresos_emitidos,
                COUNT(*) FILTER (WHERE clasificacion = 'EMITIDO' AND tipo_cfdi = 'N') AS nominas_emitidas,
                COUNT(*) FILTER (WHERE clasificacion = 'EMITIDO' AND tipo_cfdi = 'P') AS pagos_emitidos,
                COUNT(*) FILTER (WHERE clasificacion = 'EMITIDO' AND tipo_cfdi = 'T') AS traslados_emitidos,

                COALESCE(SUM(monto_total_pagos) FILTER (
                    WHERE clasificacion = 'EMITIDO' AND tipo_cfdi = 'P'), 0) AS total_pagos_emitidos,

                COALESCE(SUM(monto_total_pagos) FILTER (
                    WHERE clasificacion = 'RECIBIDO' AND tipo_cfdi = 'P'), 0) AS total_pagos_recibidos,

                COUNT(*) FILTER (WHERE clasificacion = 'RECIBIDO' AND tipo_cfdi = 'I') AS ingresos_recibidos,
                COUNT(*) FILTER (WHERE clasificacion = 'RECIBIDO' AND tipo_cfdi = 'E') AS egresos_recibidos,
                COUNT(*) FILTER (WHERE clasificacion = 'RECIBIDO' AND tipo_cfdi = 'N') AS nominas_recibidas,
                COUNT(*) FILTER (WHERE clasificacion = 'RECIBIDO' AND tipo_cfdi = 'P') AS pagos_recibidos,
                COUNT(*) FILTER (WHERE clasificacion = 'RECIBIDO' AND tipo_cfdi = 'T') AS traslados_recibidos,

                COALESCE(SUM(total) FILTER (
                    WHERE clasificacion = 'EMITIDO' AND tipo_cfdi = 'I'), 0) AS total_ingresos_emitidos,

                COALESCE(SUM(total) FILTER (
                    WHERE clasificacion = 'EMITIDO' AND tipo_cfdi = 'E'), 0) AS total_egresos_emitidos,

                COALESCE(SUM(total) FILTER (
                    WHERE clasificacion = 'EMITIDO' AND tipo_cfdi = 'N'), 0) AS total_nominas_emitidas,

                COALESCE(SUM(CASE
                    WHEN clasificacion = 'EMITIDO' AND tipo_cfdi = 'I' THEN total
                    WHEN clasificacion = 'EMITIDO' AND tipo_cfdi = 'E' THEN -total
                    ELSE 0
                END), 0) AS facturacion_neta,

                COALESCE(SUM(total) FILTER (
                    WHERE clasificacion = 'RECIBIDO' AND tipo_cfdi = 'I'), 0) AS total_ingresos_recibidos,

                COALESCE(SUM(total) FILTER (
                    WHERE clasificacion = 'RECIBIDO' AND tipo_cfdi = 'E'), 0) AS total_egresos_recibidos,

                COALESCE(SUM(total) FILTER (
                    WHERE clasificacion = 'RECIBIDO' AND tipo_cfdi = 'N'), 0) AS total_nominas_recibidas,

                COALESCE(SUM(CASE
                    WHEN clasificacion = 'RECIBIDO' AND tipo_cfdi = 'I' THEN total
                    WHEN clasificacion = 'RECIBIDO' AND tipo_cfdi = 'E' THEN -total
                    ELSE 0
                END), 0) AS compras_netas
            FROM cfdi c
            WHERE c.empresa_id = @empresa_id
              AND c.fecha_emision >= @fecha_inicio
              AND c.fecha_emision < @fecha_fin_exclusiva;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@fecha_inicio", fechaInicio.Date);
        cmd.Parameters.AddWithValue("@fecha_fin_exclusiva", fechaFin.Date.AddDays(1));

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return new DashboardDto();
        }

        return new DashboardDto
        {
            TotalPagosEmitidos = Convert.ToDecimal(reader["total_pagos_emitidos"]),
            TotalPagosRecibidos = Convert.ToDecimal(reader["total_pagos_recibidos"]),
            CfdiEmitidos = Convert.ToInt64(reader["cfdi_emitidos"]),
            IngresosEmitidos = Convert.ToInt64(reader["ingresos_emitidos"]),
            EgresosEmitidos = Convert.ToInt64(reader["egresos_emitidos"]),
            NominasEmitidas = Convert.ToInt64(reader["nominas_emitidas"]),
            PagosEmitidos = Convert.ToInt64(reader["pagos_emitidos"]),
            TrasladosEmitidos = Convert.ToInt64(reader["traslados_emitidos"]),
            TotalIngresosEmitidos = Convert.ToDecimal(reader["total_ingresos_emitidos"]),
            TotalEgresosEmitidos = Convert.ToDecimal(reader["total_egresos_emitidos"]),
            TotalNominasEmitidas = Convert.ToDecimal(reader["total_nominas_emitidas"]),
            FacturacionNeta = Convert.ToDecimal(reader["facturacion_neta"]),

            CfdiRecibidos = Convert.ToInt64(reader["cfdi_recibidos"]),
            IngresosRecibidos = Convert.ToInt64(reader["ingresos_recibidos"]),
            EgresosRecibidos = Convert.ToInt64(reader["egresos_recibidos"]),
            NominasRecibidas = Convert.ToInt64(reader["nominas_recibidas"]),
            PagosRecibidos = Convert.ToInt64(reader["pagos_recibidos"]),
            TrasladosRecibidos = Convert.ToInt64(reader["traslados_recibidos"]),
            TotalIngresosRecibidos = Convert.ToDecimal(reader["total_ingresos_recibidos"]),
            TotalEgresosRecibidos = Convert.ToDecimal(reader["total_egresos_recibidos"]),
            TotalNominasRecibidas = Convert.ToDecimal(reader["total_nominas_recibidas"]),
            ComprasNetas = Convert.ToDecimal(reader["compras_netas"])
        };
    }

    public ResultadoPaginadoDto<CfdiConsultaDto> BuscarCfdi(
        CfdiFiltroDto filtro)
    {
        ValidarFiltroCfdi(filtro);

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        int desplazamiento =
            (filtro.Pagina - 1) * filtro.RegistrosPorPagina;

        string campoOrden = ObtenerCampoOrdenCfdi(filtro.OrdenarPor);
        string direccionOrden = filtro.OrdenDescendente ? "DESC" : "ASC";
        string condiciones = ConstruirCondicionesCfdi();

        string sqlConteo = $@"
            SELECT COUNT(*)
            FROM cfdi c
            INNER JOIN personas_fiscales emisor
                ON emisor.persona_id = c.emisor_id
            INNER JOIN personas_fiscales receptor
                ON receptor.persona_id = c.receptor_id
            WHERE {condiciones};";

        long totalRegistros;

        using (var cmdConteo = new NpgsqlCommand(sqlConteo, conn))
        {
            AgregarParametrosFiltroCfdi(cmdConteo, filtro);
            totalRegistros = Convert.ToInt64(cmdConteo.ExecuteScalar());
        }

        string sqlConsulta = $@"
            SELECT
                c.cfdi_id,
                c.uuid,
                COALESCE(c.clasificacion, '') AS clasificacion,
                COALESCE(c.tipo_cfdi, '') AS tipo_cfdi,
                c.fecha_emision,
                c.serie,
                c.folio,
                c.subtotal,
                c.total,
                c.moneda,
                COALESCE(
                    c.estado_sat,
                    'NO VALIDADO'
                ) AS estado_sat,
                c.fecha_validacion_sat,
                COALESCE(c.origen_validacion_sat, '') AS origen_validacion_sat,
                COALESCE(c.ruta_xml, '') AS ruta_xml,
                emisor.rfc AS rfc_emisor,
                emisor.nombre AS nombre_emisor,
                receptor.rfc AS rfc_receptor,
                receptor.nombre AS nombre_receptor,
                CASE
                    WHEN c.clasificacion = 'EMITIDO' THEN receptor.rfc
                    WHEN c.clasificacion = 'RECIBIDO' THEN emisor.rfc
                    ELSE receptor.rfc
                END AS rfc_contraparte,
                CASE
                    WHEN c.clasificacion = 'EMITIDO' THEN receptor.nombre
                    WHEN c.clasificacion = 'RECIBIDO' THEN emisor.nombre
                    ELSE receptor.nombre
                END AS nombre_contraparte
            FROM cfdi c
            INNER JOIN personas_fiscales emisor
                ON emisor.persona_id = c.emisor_id
            INNER JOIN personas_fiscales receptor
                ON receptor.persona_id = c.receptor_id
            WHERE {condiciones}
            ORDER BY {campoOrden} {direccionOrden}, c.cfdi_id DESC
            LIMIT @limite
            OFFSET @desplazamiento;";

        var registros = new List<CfdiConsultaDto>();

        using var cmdConsulta = new NpgsqlCommand(sqlConsulta, conn);
        AgregarParametrosFiltroCfdi(cmdConsulta, filtro);
        cmdConsulta.Parameters.AddWithValue("@limite", filtro.RegistrosPorPagina);
        cmdConsulta.Parameters.AddWithValue("@desplazamiento", desplazamiento);

        using var reader = cmdConsulta.ExecuteReader();

        while (reader.Read())
        {
            registros.Add(new CfdiConsultaDto
            {
                CfdiId = Convert.ToInt64(reader["cfdi_id"]),
                UUID = reader["uuid"]?.ToString() ?? "",
                Clasificacion = reader["clasificacion"]?.ToString() ?? "",
                TipoCfdi = reader["tipo_cfdi"]?.ToString() ?? "",
                FechaEmision = Convert.ToDateTime(reader["fecha_emision"]),
                Serie = reader["serie"] == DBNull.Value ? "" : reader["serie"]?.ToString() ?? "",
                Folio = reader["folio"] == DBNull.Value ? "" : reader["folio"]?.ToString() ?? "",
                RfcEmisor = reader["rfc_emisor"]?.ToString() ?? "",
                NombreEmisor = reader["nombre_emisor"]?.ToString() ?? "",
                RfcReceptor = reader["rfc_receptor"]?.ToString() ?? "",
                NombreReceptor = reader["nombre_receptor"]?.ToString() ?? "",
                RfcContraparte = reader["rfc_contraparte"]?.ToString() ?? "",
                NombreContraparte = reader["nombre_contraparte"]?.ToString() ?? "",
                Subtotal = Convert.ToDecimal(reader["subtotal"]),
                Total = Convert.ToDecimal(reader["total"]),
                Moneda = reader["moneda"] == DBNull.Value ? "" : reader["moneda"]?.ToString() ?? "",
                RutaXml = RutaXmlLocal.Resolver(reader["ruta_xml"]?.ToString()),
                EstadoSat = reader["estado_sat"]?.ToString()?? "NO VALIDADO",
                FechaValidacionSat = reader["fecha_validacion_sat"] as DateTime?,
                OrigenValidacionSat = reader["origen_validacion_sat"]?.ToString() ?? ""
            });
        }

        return new ResultadoPaginadoDto<CfdiConsultaDto>
        {
            Registros = registros,
            TotalRegistros = totalRegistros,
            PaginaActual = filtro.Pagina,
            RegistrosPorPagina = filtro.RegistrosPorPagina
        };
    }

    public List<CfdiConsultaDto> ObtenerCfdiParaExportar(
        CfdiFiltroDto filtro)
    {
        ValidarFiltroCfdi(filtro);
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        string condiciones = ConstruirCondicionesCfdi();
        string campoOrden = ObtenerCampoOrdenCfdi(filtro.OrdenarPor);
        string direccion = filtro.OrdenDescendente ? "DESC" : "ASC";
        string sql = $@"
            SELECT
                c.cfdi_id, c.uuid,
                COALESCE(c.clasificacion, '') AS clasificacion,
                COALESCE(c.tipo_cfdi, '') AS tipo_cfdi,
                c.fecha_emision, c.serie, c.folio,
                COALESCE(c.subtotal, 0) AS subtotal,
                COALESCE(c.total, 0) AS total,
                COALESCE(c.moneda, '') AS moneda,
                COALESCE(c.ruta_xml, '') AS ruta_xml,
                emisor.rfc AS rfc_emisor,
                emisor.nombre AS nombre_emisor,
                receptor.rfc AS rfc_receptor,
                receptor.nombre AS nombre_receptor,
                CASE WHEN c.clasificacion = 'EMITIDO' THEN receptor.rfc
                     WHEN c.clasificacion = 'RECIBIDO' THEN emisor.rfc
                     ELSE receptor.rfc END AS rfc_contraparte,
                CASE WHEN c.clasificacion = 'EMITIDO' THEN receptor.nombre
                     WHEN c.clasificacion = 'RECIBIDO' THEN emisor.nombre
                     ELSE receptor.nombre END AS nombre_contraparte
            FROM cfdi c
            INNER JOIN personas_fiscales emisor ON emisor.persona_id = c.emisor_id
            INNER JOIN personas_fiscales receptor ON receptor.persona_id = c.receptor_id
            WHERE {condiciones}
            ORDER BY {campoOrden} {direccion}, c.cfdi_id DESC;";
        using var cmd = new NpgsqlCommand(sql, conn);
        AgregarParametrosFiltroCfdi(cmd, filtro);
        using var reader = cmd.ExecuteReader();
        var resultado = new List<CfdiConsultaDto>();
        while (reader.Read()) resultado.Add(MapearCfdiConsulta(reader));
        return resultado;
    }

    public CfdiResumenConsultaDto ObtenerResumenCfdi(
        CfdiFiltroDto filtro)
    {
        ValidarFiltroCfdi(filtro);

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        string condiciones = ConstruirCondicionesCfdi();

        string sql = $@"
            SELECT
                COUNT(*) AS total_cfdi,
                COALESCE(
                    SUM(
                        CASE
                            WHEN c.tipo_cfdi = 'E' THEN -c.total
                            ELSE c.total
                        END
                    ),
                    0
                ) AS importe_total,
                COUNT(*) FILTER (WHERE c.tipo_cfdi = 'I') AS cantidad_facturas,
                COALESCE(SUM(c.total) FILTER (WHERE c.tipo_cfdi = 'I'), 0) AS total_facturas,
                COUNT(*) FILTER (WHERE c.tipo_cfdi = 'N') AS cantidad_nominas,
                COALESCE(SUM(c.total) FILTER (WHERE c.tipo_cfdi = 'N'), 0) AS total_nominas,
                COUNT(*) FILTER (WHERE c.tipo_cfdi = 'E') AS cantidad_notas_credito,
                COALESCE(SUM(c.total) FILTER (WHERE c.tipo_cfdi = 'E'), 0) AS total_notas_credito,
                COUNT(*) FILTER (WHERE c.tipo_cfdi = 'T') AS cantidad_traslados,
                COALESCE(SUM(c.total) FILTER (WHERE c.tipo_cfdi = 'T'), 0) AS total_traslados,
                COUNT(*) FILTER (WHERE c.tipo_cfdi = 'P') AS cantidad_pagos,
                COALESCE(
                    SUM(
                        CASE
                            WHEN c.tipo_cfdi = 'P'
                                THEN COALESCE(c.monto_total_pagos, c.total, 0)
                            ELSE 0
                        END
                    ),
                    0
                ) AS total_pagos
            FROM cfdi c
            INNER JOIN personas_fiscales emisor
                ON emisor.persona_id = c.emisor_id
            INNER JOIN personas_fiscales receptor
                ON receptor.persona_id = c.receptor_id
            WHERE {condiciones};";

        using var cmd = new NpgsqlCommand(sql, conn);
        AgregarParametrosFiltroCfdi(cmd, filtro);

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return new CfdiResumenConsultaDto();
        }

        return new CfdiResumenConsultaDto
        {
            TotalCfdi = Convert.ToInt64(reader["total_cfdi"]),
            ImporteTotal = Convert.ToDecimal(reader["importe_total"]),
            CantidadFacturas = Convert.ToInt64(reader["cantidad_facturas"]),
            TotalFacturas = Convert.ToDecimal(reader["total_facturas"]),
            CantidadNominas = Convert.ToInt64(reader["cantidad_nominas"]),
            TotalNominas = Convert.ToDecimal(reader["total_nominas"]),
            CantidadNotasCredito = Convert.ToInt64(reader["cantidad_notas_credito"]),
            TotalNotasCredito = Convert.ToDecimal(reader["total_notas_credito"]),
            CantidadTraslados = Convert.ToInt64(reader["cantidad_traslados"]),
            TotalTraslados = Convert.ToDecimal(reader["total_traslados"]),
            CantidadPagos = Convert.ToInt64(reader["cantidad_pagos"]),
            TotalPagos = Convert.ToDecimal(reader["total_pagos"])
        };
    }

    private static void ValidarFiltroCfdi(CfdiFiltroDto filtro)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        if (filtro.EmpresaId <= 0)
        {
            throw new InvalidOperationException(
                "No se ha seleccionado una empresa.");
        }

        if (filtro.FechaFin.Date < filtro.FechaInicio.Date)
        {
            throw new InvalidOperationException(
                "La fecha final no puede ser menor que la fecha inicial.");
        }

        if (filtro.Pagina <= 0)
        {
            filtro.Pagina = 1;
        }

        int[] cantidadesPermitidas = { 25, 50, 100 };

        if (!cantidadesPermitidas.Contains(filtro.RegistrosPorPagina))
        {
            filtro.RegistrosPorPagina = 25;
        }

        filtro.Clasificacion = (filtro.Clasificacion ?? "").Trim().ToUpperInvariant();
        filtro.TipoCfdi = (filtro.TipoCfdi ?? "").Trim().ToUpperInvariant();
        filtro.Busqueda = (filtro.Busqueda ?? "").Trim();
        filtro.OrdenarPor = (filtro.OrdenarPor ?? "fecha_emision").Trim();
    }

    private static string ConstruirCondicionesCfdi()
    {
        return @"
            c.empresa_id = @empresa_id
            AND c.fecha_emision >= @fecha_inicio
            AND c.fecha_emision < @fecha_fin_exclusiva
            AND (@clasificacion = '' OR c.clasificacion = @clasificacion)
            AND (@tipo_cfdi = '' OR c.tipo_cfdi = @tipo_cfdi)
            AND (
                @busqueda = ''
                OR c.uuid ILIKE '%' || @busqueda || '%'
                OR COALESCE(c.serie, '') ILIKE '%' || @busqueda || '%'
                OR COALESCE(c.folio, '') ILIKE '%' || @busqueda || '%'
                OR emisor.rfc ILIKE '%' || @busqueda || '%'
                OR emisor.nombre ILIKE '%' || @busqueda || '%'
                OR receptor.rfc ILIKE '%' || @busqueda || '%'
                OR receptor.nombre ILIKE '%' || @busqueda || '%'
            )";
    }

    private static void AgregarParametrosFiltroCfdi(
        NpgsqlCommand cmd,
        CfdiFiltroDto filtro)
    {
        cmd.Parameters.AddWithValue("@empresa_id", filtro.EmpresaId);
        cmd.Parameters.AddWithValue("@fecha_inicio", filtro.FechaInicio.Date);
        cmd.Parameters.AddWithValue("@fecha_fin_exclusiva", filtro.FechaFin.Date.AddDays(1));
        cmd.Parameters.AddWithValue("@clasificacion", filtro.Clasificacion);
        cmd.Parameters.AddWithValue("@tipo_cfdi", filtro.TipoCfdi);
        cmd.Parameters.AddWithValue("@busqueda", filtro.Busqueda);
    }

    private static string ObtenerCampoOrdenCfdi(string ordenarPor)
    {
        return (ordenarPor ?? "")
            .Trim()
            .ToLowerInvariant() switch
        {
            "total" => "c.total",
            "serie" => "c.serie",
            "folio" => "c.folio",
            "tipo_cfdi" => "c.tipo_cfdi",
            "clasificacion" => "c.clasificacion",
            "rfc" => "rfc_contraparte",
            "nombre" => "nombre_contraparte",
            _ => "c.fecha_emision"
        };
    }

    public void ActualizarMontoTotalPagos(
        long empresaId,
        string uuid,
        decimal? montoTotalPagos)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            UPDATE cfdi
            SET monto_total_pagos = @monto_total_pagos
            WHERE empresa_id = @empresa_id
              AND uuid = @uuid;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@uuid", uuid);
        cmd.Parameters.AddWithValue(
            "@monto_total_pagos",
            (object?)montoTotalPagos ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public CfdiConsultaDto? ObtenerCfdiPorId(
        long empresaId,
        long cfdiId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            SELECT
                c.cfdi_id,
                c.uuid,
                COALESCE(c.clasificacion, '') AS clasificacion,
                COALESCE(c.tipo_cfdi, '') AS tipo_cfdi,
                c.fecha_emision,
                c.serie,
                c.folio,
                c.subtotal,
                c.total,
                c.moneda,
                COALESCE(c.ruta_xml, '') AS ruta_xml,
                emisor.rfc AS rfc_emisor,
                emisor.nombre AS nombre_emisor,
                receptor.rfc AS rfc_receptor,
                receptor.nombre AS nombre_receptor,
                CASE
                    WHEN c.clasificacion = 'EMITIDO' THEN receptor.rfc
                    WHEN c.clasificacion = 'RECIBIDO' THEN emisor.rfc
                    ELSE receptor.rfc
                END AS rfc_contraparte,
                CASE
                    WHEN c.clasificacion = 'EMITIDO' THEN receptor.nombre
                    WHEN c.clasificacion = 'RECIBIDO' THEN emisor.nombre
                    ELSE receptor.nombre
                END AS nombre_contraparte
            FROM cfdi c
            INNER JOIN personas_fiscales emisor
                ON emisor.persona_id = c.emisor_id
            INNER JOIN personas_fiscales receptor
                ON receptor.persona_id = c.receptor_id
            WHERE c.empresa_id = @empresa_id
              AND c.cfdi_id = @cfdi_id;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@cfdi_id", cfdiId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new CfdiConsultaDto
        {
            CfdiId = Convert.ToInt64(reader["cfdi_id"]),
            UUID = reader["uuid"]?.ToString() ?? "",
            Clasificacion = reader["clasificacion"]?.ToString() ?? "",
            TipoCfdi = reader["tipo_cfdi"]?.ToString() ?? "",
            FechaEmision = Convert.ToDateTime(reader["fecha_emision"]),
            Serie = reader["serie"] == DBNull.Value ? "" : reader["serie"]?.ToString() ?? "",
            Folio = reader["folio"] == DBNull.Value ? "" : reader["folio"]?.ToString() ?? "",
            RfcEmisor = reader["rfc_emisor"]?.ToString() ?? "",
            NombreEmisor = reader["nombre_emisor"]?.ToString() ?? "",
            RfcReceptor = reader["rfc_receptor"]?.ToString() ?? "",
            NombreReceptor = reader["nombre_receptor"]?.ToString() ?? "",
            RfcContraparte = reader["rfc_contraparte"]?.ToString() ?? "",
            NombreContraparte = reader["nombre_contraparte"]?.ToString() ?? "",
            Subtotal = Convert.ToDecimal(reader["subtotal"]),
            Total = Convert.ToDecimal(reader["total"]),
            Moneda = reader["moneda"] == DBNull.Value ? "" : reader["moneda"]?.ToString() ?? "",
            RutaXml = RutaXmlLocal.Resolver(reader["ruta_xml"]?.ToString()),
            EstadoSat = "NO VERIFICADO"
        };
    }

public List<EmpresaDto> ObtenerEmpresasActivas()
{
    var resultado =
        new List<EmpresaDto>();

    using var conn =
        new NpgsqlConnection(
            _connectionString);

    conn.Open();

    const string sql = @"
        SELECT
            empresa_id,
            rfc,
            razon_social,
            COALESCE(
                nombre_corto,
                ''
            ) AS nombre_corto,
            COALESCE(
                ruta_xml,
                ''
            ) AS ruta_xml,
            activa
        FROM empresas
        WHERE activa = TRUE
        ORDER BY
            COALESCE(
                nombre_corto,
                razon_social
            ),
            rfc;";

    using var cmd =
        new NpgsqlCommand(
            sql,
            conn);

    using var reader =
        cmd.ExecuteReader();

    while (reader.Read())
    {
        resultado.Add(
            new EmpresaDto
            {
                EmpresaId =
                    Convert.ToInt64(
                        reader["empresa_id"]),

                Rfc =
                    reader["rfc"]?.ToString()
                    ?? "",

                RazonSocial =
                    reader["razon_social"]?.ToString()
                    ?? "",

                NombreCorto =
                    reader["nombre_corto"]?.ToString()
                    ?? "",

                RutaXml =
                    reader["ruta_xml"]?.ToString()
                    ?? "",

                Activa =
                    Convert.ToBoolean(
                        reader["activa"])
            });
    }

    return resultado;
}

    public EmpresaDto? ObtenerEmpresaPorId(long empresaId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            SELECT
                empresa_id,
                rfc,
                razon_social,
                COALESCE(nombre_corto, '') AS nombre_corto,
                COALESCE(ruta_xml, '') AS ruta_xml,
                activa
            FROM empresas
            WHERE empresa_id = @empresa_id
              AND activa = TRUE;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new EmpresaDto
        {
            EmpresaId = Convert.ToInt64(reader["empresa_id"]),
            Rfc = reader["rfc"]?.ToString() ?? "",
            RazonSocial = reader["razon_social"]?.ToString() ?? "",
            NombreCorto = reader["nombre_corto"]?.ToString() ?? "",
            RutaXml = reader["ruta_xml"]?.ToString() ?? "",
            Activa = Convert.ToBoolean(reader["activa"])
        };
    }
 

    public bool ExisteEmpresaPorRfc(string rfc, long? empresaIdExcluir = null)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        const string sql = @"
            SELECT EXISTS
            (
                SELECT 1 FROM empresas
                WHERE UPPER(TRIM(rfc)) = UPPER(TRIM(@rfc))
                  AND (@empresa_id_excluir IS NULL
                       OR empresa_id <> @empresa_id_excluir)
            );";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@rfc", rfc);
        AgregarParametro(cmd, "@empresa_id_excluir", empresaIdExcluir);
        return Convert.ToBoolean(cmd.ExecuteScalar());
    }

    public long GuardarEmpresa(EmpresaDto empresa)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        const string sql = @"
            INSERT INTO empresas
            (rfc, razon_social, nombre_corto, ruta_xml, activa)
            VALUES
            (@rfc, @razon_social, @nombre_corto, @ruta_xml, @activa)
            RETURNING empresa_id;";
        using var cmd = new NpgsqlCommand(sql, conn);
        AgregarParametrosEmpresa(cmd, empresa);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void ActualizarEmpresa(EmpresaDto empresa)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        if (empresa.EmpresaId <= 0)
            throw new InvalidOperationException("La empresa no es válida.");
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        const string sql = @"
            UPDATE empresas SET
                rfc = @rfc,
                razon_social = @razon_social,
                nombre_corto = @nombre_corto,
                ruta_xml = @ruta_xml,
                activa = @activa
            WHERE empresa_id = @empresa_id;";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresa.EmpresaId);
        AgregarParametrosEmpresa(cmd, empresa);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("La empresa ya no existe.");
    }

    public void CambiarEstadoEmpresa(long empresaId, bool activa)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        const string sql = @"
            UPDATE empresas SET activa = @activa
            WHERE empresa_id = @empresa_id;";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@activa", activa);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("La empresa ya no existe.");
    }

    private static void AgregarParametrosEmpresa(
        NpgsqlCommand cmd,
        EmpresaDto empresa)
    {
        cmd.Parameters.AddWithValue("@rfc", empresa.Rfc);
        cmd.Parameters.AddWithValue("@razon_social", empresa.RazonSocial);
        AgregarParametro(cmd, "@nombre_corto", empresa.NombreCorto);
        AgregarParametro(cmd, "@ruta_xml", empresa.RutaXml);
        cmd.Parameters.AddWithValue("@activa", empresa.Activa);
    }

private static EmpresaDto MapearEmpresa(NpgsqlDataReader reader)
{
    return new EmpresaDto
    {
        EmpresaId = Convert.ToInt64(reader["empresa_id"]),
        Rfc = reader["rfc"]?.ToString() ?? "",
        RazonSocial = reader["razon_social"]?.ToString() ?? "",
        NombreCorto = reader["nombre_corto"]?.ToString() ?? "",
        RutaXml = reader["ruta_xml"]?.ToString() ?? "",
        Activa = Convert.ToBoolean(reader["activa"]),

        TieneFiel =
            reader["tiene_fiel"] != DBNull.Value &&
            Convert.ToBoolean(reader["tiene_fiel"]),

        FechaVencimientoFiel =
            reader["fecha_vencimiento"] switch
            {
                DBNull => null,
                DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                DateTime dt => dt,
                _ => null
            }
    };
}


    private static CfdiConsultaDto MapearCfdiConsulta(NpgsqlDataReader reader)
    {
        return new CfdiConsultaDto
        {
            CfdiId = Convert.ToInt64(reader["cfdi_id"]),
            UUID = reader["uuid"]?.ToString() ?? "",
            Clasificacion = reader["clasificacion"]?.ToString() ?? "",
            TipoCfdi = reader["tipo_cfdi"]?.ToString() ?? "",
            FechaEmision = Convert.ToDateTime(reader["fecha_emision"]),
            Serie = reader["serie"] == DBNull.Value ? "" : reader["serie"]?.ToString() ?? "",
            Folio = reader["folio"] == DBNull.Value ? "" : reader["folio"]?.ToString() ?? "",
            RfcEmisor = reader["rfc_emisor"]?.ToString() ?? "",
            NombreEmisor = reader["nombre_emisor"]?.ToString() ?? "",
            RfcReceptor = reader["rfc_receptor"]?.ToString() ?? "",
            NombreReceptor = reader["nombre_receptor"]?.ToString() ?? "",
            RfcContraparte = reader["rfc_contraparte"]?.ToString() ?? "",
            NombreContraparte = reader["nombre_contraparte"]?.ToString() ?? "",
            Subtotal = Convert.ToDecimal(reader["subtotal"]),
            Total = Convert.ToDecimal(reader["total"]),
            Moneda = reader["moneda"]?.ToString() ?? "",
            RutaXml = reader["ruta_xml"]?.ToString() ?? "",
            EstadoSat = "NO VERIFICADO"
        };
    }

    private static void AgregarParametro(NpgsqlCommand cmd, string nombre, object? valor)
    {
        if (valor is string texto)
            cmd.Parameters.AddWithValue(nombre, string.IsNullOrWhiteSpace(texto) ? DBNull.Value : texto);
        else
            cmd.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
    }


    public List<CfdiExportacionDto> ObtenerCfdiParaExportacionDetallada(
        CfdiFiltroDto filtro)
    {
        ValidarFiltroCfdi(filtro);

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        string condiciones = ConstruirCondicionesCfdi();
        string campoOrden = ObtenerCampoOrdenCfdi(filtro.OrdenarPor);
        string direccionOrden = filtro.OrdenDescendente ? "DESC" : "ASC";

        string sql = $@"
            WITH conceptos_agrupados AS
            (
                SELECT
                    cc.cfdi_id,
                    STRING_AGG(
                        CONCAT(
                            COALESCE(cc.clave_prod_serv, ''),
                            CASE
                                WHEN COALESCE(cc.clave_prod_serv, '') <> ''
                                THEN ' - '
                                ELSE ''
                            END,
                            COALESCE(cc.descripcion, ''),
                            CASE
                                WHEN cc.cantidad IS NOT NULL
                                THEN CONCAT(' | Cantidad: ', cc.cantidad)
                                ELSE ''
                            END,
                            CASE
                                WHEN cc.importe IS NOT NULL
                                THEN CONCAT(' | Importe: ', cc.importe)
                                ELSE ''
                            END
                        ),
                        ' || ' ORDER BY cc.concepto_id
                    ) AS conceptos
                FROM cfdi_conceptos cc
                GROUP BY cc.cfdi_id
            ),
            relacionados_agrupados AS
            (
                SELECT
                    cr.cfdi_id,
                    STRING_AGG(DISTINCT cr.uuid_relacionado, ' | ')
                        AS uuid_relacion,
                    STRING_AGG(DISTINCT cr.tipo_relacion, ' | ')
                        AS tipo_relacion
                FROM cfdi_relacionados cr
                GROUP BY cr.cfdi_id
            ),
            impuestos_agrupados AS
            (
                SELECT
                    ig.cfdi_id,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                    ) AS total_ieps,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '002'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.160000
                    ) AS iva_16,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '002'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.080000
                    ) AS iva_8,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'RETENCION'
                          AND ig.impuesto = '002'
                    ) AS retenido_iva,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'RETENCION'
                          AND ig.impuesto = '001'
                    ) AS retenido_isr,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'RETENCION'
                          AND ig.impuesto = '002'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.060000
                    ) AS iva_retenido_6,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.030000
                    ) AS ieps_3,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.060000
                    ) AS ieps_6,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.070000
                    ) AS ieps_7,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.080000
                    ) AS ieps_8,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.090000
                    ) AS ieps_9,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.265000
                    ) AS ieps_265,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.300000
                    ) AS ieps_30,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.304000
                    ) AS ieps_304,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 0.530000
                    ) AS ieps_53,
                    SUM(ig.importe) FILTER
                    (
                        WHERE ig.tipo = 'TRASLADO'
                          AND ig.impuesto = '003'
                          AND ROUND(COALESCE(ig.tasa, 0), 6) = 1.600000
                    ) AS ieps_160
                FROM cfdi_impuestos_globales ig
                GROUP BY ig.cfdi_id
            )
            SELECT
                c.cfdi_id,
                c.empresa_id,
                COALESCE(c.clasificacion, '') AS clasificacion,
                NULL::text AS verificado_asociado,
                c.estado_sat,
                c.es_cancelable,
                c.estatus_cancelacion,
                c.validacion_efos,
                c.fecha_validacion_sat,
                COALESCE(c.tipo_cfdi, '') AS tipo,
                c.fecha_emision,
                c.fecha_timbrado,
                c.serie,
                c.folio,
                c.uuid,
                ra.uuid_relacion,
                ra.tipo_relacion,
                emisor.rfc AS rfc_emisor,
                emisor.nombre AS nombre_emisor,
                c.lugar_expedicion,
                c.regimen_fiscal_emisor,
                receptor.rfc AS rfc_receptor,
                receptor.nombre AS nombre_receptor,
                c.residencia_fiscal,
                c.num_reg_id_trib,
                c.uso_cfdi,
                c.regimen_fiscal_receptor,
                c.domicilio_fiscal_receptor,
                COALESCE(c.subtotal, 0) AS subtotal,
                c.descuento,
                COALESCE(c.total, 0) AS total,
                c.total AS total_original,
                ia.total_ieps,
                ia.iva_16,
                ia.iva_8,
                ia.retenido_iva,
                ia.retenido_isr,
                ia.iva_retenido_6,
                c.total_impuestos_trasladados AS total_trasladados,
                c.total_impuestos_retenidos AS total_retenidos,
                NULL::numeric AS ish,
                NULL::numeric AS total_local_trasladado,
                NULL::numeric AS total_local_retenido,
                CASE
                    WHEN c.tipo_cfdi = 'P' THEN 'Pagos'
                    WHEN c.tipo_cfdi = 'N' THEN 'Nómina'
                    ELSE NULL
                END AS complemento,
                c.monto_total_pagos,
                COALESCE(c.moneda, '') AS moneda,
                c.tipo_cambio,
                c.forma_pago,
                c.metodo_pago,
                NULL::text AS num_cta_pago,
                c.condiciones_pago AS condicion_pago,
                ca.conceptos,
                NULL::text AS combustible,
                ia.ieps_3,
                ia.ieps_6,
                ia.ieps_7,
                ia.ieps_8,
                ia.ieps_9,
                ia.ieps_265,
                ia.ieps_30,
                ia.ieps_304,
                ia.ieps_53,
                ia.ieps_160,
                COALESCE(c.ruta_xml, '') AS archivo_xml,
                NULL::text AS direccion_emisor,
                NULL::text AS localidad_emisor,
                NULL::text AS direccion_receptor,
                NULL::text AS localidad_receptor,
                c.exportacion,
                c.version_cfdi,
                c.no_certificado,
                c.no_certificado_sat,
                c.rfc_prov_certif,
                CASE
                    WHEN ra.uuid_relacion IS NOT NULL THEN 'ASOC.'
                    ELSE NULL
                END AS etiqueta_asociado,
                CASE
                    WHEN c.clasificacion = 'EMITIDO' THEN receptor.rfc
                    WHEN c.clasificacion = 'RECIBIDO' THEN emisor.rfc
                    ELSE receptor.rfc
                END AS rfc_contraparte,
                CASE
                    WHEN c.clasificacion = 'EMITIDO' THEN receptor.nombre
                    WHEN c.clasificacion = 'RECIBIDO' THEN emisor.nombre
                    ELSE receptor.nombre
                END AS nombre_contraparte
            FROM cfdi c
            INNER JOIN personas_fiscales emisor
                ON emisor.persona_id = c.emisor_id
            INNER JOIN personas_fiscales receptor
                ON receptor.persona_id = c.receptor_id
            LEFT JOIN conceptos_agrupados ca
                ON ca.cfdi_id = c.cfdi_id
            LEFT JOIN relacionados_agrupados ra
                ON ra.cfdi_id = c.cfdi_id
            LEFT JOIN impuestos_agrupados ia
                ON ia.cfdi_id = c.cfdi_id
            WHERE {condiciones}
            ORDER BY {campoOrden} {direccionOrden}, c.cfdi_id DESC;";

        using var cmd = new NpgsqlCommand(sql, conn);
        AgregarParametrosFiltroCfdi(cmd, filtro);

        using var reader = cmd.ExecuteReader();
        var resultado = new List<CfdiExportacionDto>();

        while (reader.Read())
        {
            resultado.Add(MapearCfdiExportacion(reader));
        }

        return resultado;
    }

    private static CfdiExportacionDto MapearCfdiExportacion(
        NpgsqlDataReader reader)
    {
        return new CfdiExportacionDto
        {
            CfdiId = Convert.ToInt64(reader["cfdi_id"]),
            EmpresaId = Convert.ToInt64(reader["empresa_id"]),
            Clasificacion = LeerTextoObligatorio(reader, "clasificacion"),
            VerificadoAsociado =
                LeerTextoNullable(reader, "verificado_asociado")
                ?? LeerTextoNullable(reader, "etiqueta_asociado"),
            EstadoSat = LeerTextoNullable(reader, "estado_sat"),
            EsCancelable =
                LeerTextoNullable(
                    reader,
                    "es_cancelable"),

            EstatusCancelacion =
                LeerTextoNullable(
                    reader,
                    "estatus_cancelacion"),

            ValidacionEfos =
                LeerTextoNullable(
                    reader,
                    "validacion_efos"),

            FechaValidacionSat =
                LeerFechaNullable(
                    reader,
                    "fecha_validacion_sat"),
            Tipo = LeerTextoObligatorio(reader, "tipo"),
            FechaEmision = Convert.ToDateTime(reader["fecha_emision"]),
            FechaTimbrado = LeerFechaNullable(reader, "fecha_timbrado"),
            Serie = LeerTextoNullable(reader, "serie"),
            Folio = LeerTextoNullable(reader, "folio"),
            UUID = LeerTextoObligatorio(reader, "uuid"),
            UUIDRelacion = LeerTextoNullable(reader, "uuid_relacion"),
            TipoRelacion = LeerTextoNullable(reader, "tipo_relacion"),
            RfcEmisor = LeerTextoObligatorio(reader, "rfc_emisor"),
            NombreEmisor = LeerTextoNullable(reader, "nombre_emisor"),
            LugarExpedicion = LeerTextoNullable(reader, "lugar_expedicion"),
            RegimenFiscalEmisor =
                LeerTextoNullable(reader, "regimen_fiscal_emisor"),
            RfcReceptor = LeerTextoObligatorio(reader, "rfc_receptor"),
            NombreReceptor = LeerTextoNullable(reader, "nombre_receptor"),
            ResidenciaFiscal = LeerTextoNullable(reader, "residencia_fiscal"),
            NumRegIdTrib = LeerTextoNullable(reader, "num_reg_id_trib"),
            UsoCfdi = LeerTextoNullable(reader, "uso_cfdi"),
            RegimenFiscalReceptor =
                LeerTextoNullable(reader, "regimen_fiscal_receptor"),
            DomicilioFiscalReceptor =
                LeerTextoNullable(reader, "domicilio_fiscal_receptor"),
            SubTotal = Convert.ToDecimal(reader["subtotal"]),
            Descuento = LeerDecimalNullable(reader, "descuento"),
            Total = Convert.ToDecimal(reader["total"]),
            TotalOriginal = LeerDecimalNullable(reader, "total_original"),
            TotalIeps = LeerDecimalNullable(reader, "total_ieps"),
            Iva16 = LeerDecimalNullable(reader, "iva_16"),
            Iva8 = LeerDecimalNullable(reader, "iva_8"),
            RetenidoIva = LeerDecimalNullable(reader, "retenido_iva"),
            RetenidoIsr = LeerDecimalNullable(reader, "retenido_isr"),
            IvaRetenido6 = LeerDecimalNullable(reader, "iva_retenido_6"),
            TotalTrasladados = LeerDecimalNullable(reader, "total_trasladados"),
            TotalRetenidos = LeerDecimalNullable(reader, "total_retenidos"),
            Ish = LeerDecimalNullable(reader, "ish"),
            TotalLocalTrasladado =
                LeerDecimalNullable(reader, "total_local_trasladado"),
            TotalLocalRetenido =
                LeerDecimalNullable(reader, "total_local_retenido"),
            Complemento = LeerTextoNullable(reader, "complemento"),
            MontoTotalPagos = LeerDecimalNullable(reader, "monto_total_pagos"),
            Moneda = LeerTextoObligatorio(reader, "moneda"),
            TipoCambio = LeerDecimalNullable(reader, "tipo_cambio"),
            FormaPago = LeerTextoNullable(reader, "forma_pago"),
            MetodoPago = LeerTextoNullable(reader, "metodo_pago"),
            NumCtaPago = LeerTextoNullable(reader, "num_cta_pago"),
            CondicionPago = LeerTextoNullable(reader, "condicion_pago"),
            Conceptos = LeerTextoNullable(reader, "conceptos"),
            Combustible = LeerTextoNullable(reader, "combustible"),
            Ieps3 = LeerDecimalNullable(reader, "ieps_3"),
            Ieps6 = LeerDecimalNullable(reader, "ieps_6"),
            Ieps7 = LeerDecimalNullable(reader, "ieps_7"),
            Ieps8 = LeerDecimalNullable(reader, "ieps_8"),
            Ieps9 = LeerDecimalNullable(reader, "ieps_9"),
            Ieps265 = LeerDecimalNullable(reader, "ieps_265"),
            Ieps30 = LeerDecimalNullable(reader, "ieps_30"),
            Ieps304 = LeerDecimalNullable(reader, "ieps_304"),
            Ieps53 = LeerDecimalNullable(reader, "ieps_53"),
            Ieps160 = LeerDecimalNullable(reader, "ieps_160"),
            ArchivoXml = RutaXmlLocal.Resolver(LeerTextoObligatorio(reader, "archivo_xml")),
            DireccionEmisor = LeerTextoNullable(reader, "direccion_emisor"),
            LocalidadEmisor = LeerTextoNullable(reader, "localidad_emisor"),
            DireccionReceptor = LeerTextoNullable(reader, "direccion_receptor"),
            LocalidadReceptor = LeerTextoNullable(reader, "localidad_receptor"),
            Exportacion = LeerTextoNullable(reader, "exportacion"),
            VersionCfdi = LeerTextoNullable(reader, "version_cfdi"),
            NoCertificado = LeerTextoNullable(reader, "no_certificado"),
            NoCertificadoSat = LeerTextoNullable(reader, "no_certificado_sat"),
            RfcProvCertif = LeerTextoNullable(reader, "rfc_prov_certif")
        };
    }

    private static string LeerTextoObligatorio(
        NpgsqlDataReader reader,
        string columna)
    {
        int ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal)
            ? ""
            : reader.GetValue(ordinal).ToString()?.Trim() ?? "";
    }

    private static string? LeerTextoNullable(
        NpgsqlDataReader reader,
        string columna)
    {
        int ordinal = reader.GetOrdinal(columna);
        if (reader.IsDBNull(ordinal)) return null;
        string? valor = reader.GetValue(ordinal).ToString()?.Trim();
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    private static decimal? LeerDecimalNullable(
        NpgsqlDataReader reader,
        string columna)
    {
        int ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static DateTime? LeerFechaNullable(
        NpgsqlDataReader reader,
        string columna)
    {
        int ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDateTime(reader.GetValue(ordinal));
    }


    public List<ConceptoExportacionDto> ObtenerConceptosParaExportacion(
        CfdiFiltroDto filtro)
    {
        ValidarFiltroCfdi(filtro);

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = @"
            WITH impuestos_concepto AS
            (
                SELECT
                    ci.concepto_id,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '002'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.160000
                    ) AS iva_16,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '002'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.080000
                    ) AS iva_8,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '002'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.000000
                          AND UPPER(COALESCE(ci.tipo_factor, '')) <> 'EXENTO'
                    ) AS iva_cero,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '002'
                          AND UPPER(COALESCE(ci.tipo_factor, '')) = 'EXENTO'
                    ) AS iva_exento,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) IN ('RETENCION', 'RETENCIÓN')
                          AND ci.impuesto = '002'
                    ) AS iva_retenido,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) IN ('RETENCION', 'RETENCIÓN')
                          AND ci.impuesto = '002'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.060000
                    ) AS iva_retenido_6,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) IN ('RETENCION', 'RETENCIÓN')
                          AND ci.impuesto = '001'
                    ) AS isr_retenido,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                    ) AS ieps,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.030000
                    ) AS ieps_3,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.060000
                    ) AS ieps_6,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.070000
                    ) AS ieps_7,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.080000
                    ) AS ieps_8,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.090000
                    ) AS ieps_9,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.265000
                    ) AS ieps_265,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.300000
                    ) AS ieps_30,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.304000
                    ) AS ieps_304,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 0.530000
                    ) AS ieps_53,
                    SUM(ci.importe) FILTER
                    (
                        WHERE UPPER(ci.tipo) = 'TRASLADO'
                          AND ci.impuesto = '003'
                          AND ROUND(COALESCE(ci.tasa, 0), 6) = 1.600000
                    ) AS ieps_160,
                    SUM(ci.importe) AS total_impuestos
                FROM cfdi_impuestos ci
                GROUP BY ci.concepto_id
            )
            SELECT
                c.cfdi_id,
                cc.concepto_id,
                c.empresa_id,
                COALESCE(c.clasificacion, '') AS clasificacion,
                COALESCE(c.tipo_cfdi, '') AS tipo_cfdi,
                c.fecha_emision,
                c.fecha_timbrado,
                c.serie,
                c.folio,
                COALESCE(c.uuid, '') AS uuid,
                COALESCE(emisor.rfc, '') AS rfc_emisor,
                emisor.nombre AS nombre_emisor,
                COALESCE(receptor.rfc, '') AS rfc_receptor,
                receptor.nombre AS nombre_receptor,
                cc.clave_prod_serv,
                cc.no_identificacion,
                cc.descripcion,
                COALESCE(cc.cantidad, 0) AS cantidad,
                cc.clave_unidad,
                cc.unidad,
                COALESCE(cc.valor_unitario, 0) AS valor_unitario,
                COALESCE(cc.importe, 0) AS importe,
                cc.descuento,
                cc.objeto_imp,
                cc.cuenta_predial,
                impuestos.iva_16,
                impuestos.iva_8,
                impuestos.iva_cero,
                impuestos.iva_exento,
                impuestos.iva_retenido,
                impuestos.iva_retenido_6,
                impuestos.isr_retenido,
                impuestos.ieps,
                impuestos.ieps_3,
                impuestos.ieps_6,
                impuestos.ieps_7,
                impuestos.ieps_8,
                impuestos.ieps_9,
                impuestos.ieps_265,
                impuestos.ieps_30,
                impuestos.ieps_304,
                impuestos.ieps_53,
                impuestos.ieps_160,
                impuestos.total_impuestos,
                COALESCE(c.ruta_xml, '') AS archivo_xml
            FROM cfdi c
            INNER JOIN personas_fiscales emisor
                ON emisor.persona_id = c.emisor_id
            INNER JOIN personas_fiscales receptor
                ON receptor.persona_id = c.receptor_id
            INNER JOIN cfdi_conceptos cc
                ON cc.cfdi_id = c.cfdi_id
            LEFT JOIN impuestos_concepto impuestos
                ON impuestos.concepto_id = cc.concepto_id
            WHERE c.empresa_id = @empresa_id
              AND c.fecha_emision >= @fecha_inicio
              AND c.fecha_emision < @fecha_fin_exclusiva
              AND (@clasificacion = '' OR c.clasificacion = @clasificacion)
              AND (@tipo_cfdi = '' OR c.tipo_cfdi = @tipo_cfdi)
              AND
              (
                  @busqueda = ''
                  OR c.uuid ILIKE '%' || @busqueda || '%'
                  OR COALESCE(c.serie, '') ILIKE '%' || @busqueda || '%'
                  OR COALESCE(c.folio, '') ILIKE '%' || @busqueda || '%'
                  OR COALESCE(cc.clave_prod_serv, '') ILIKE '%' || @busqueda || '%'
                  OR COALESCE(cc.no_identificacion, '') ILIKE '%' || @busqueda || '%'
                  OR COALESCE(cc.descripcion, '') ILIKE '%' || @busqueda || '%'
                  OR COALESCE(emisor.rfc, '') ILIKE '%' || @busqueda || '%'
                  OR COALESCE(receptor.rfc, '') ILIKE '%' || @busqueda || '%'
              )
            ORDER BY c.fecha_emision, c.cfdi_id, cc.concepto_id;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@empresa_id", filtro.EmpresaId);
        cmd.Parameters.AddWithValue("@fecha_inicio", filtro.FechaInicio.Date);
        cmd.Parameters.AddWithValue(
            "@fecha_fin_exclusiva",
            filtro.FechaFin.Date.AddDays(1));
        cmd.Parameters.AddWithValue(
            "@clasificacion",
            filtro.Clasificacion ?? "");
        cmd.Parameters.AddWithValue("@tipo_cfdi", filtro.TipoCfdi ?? "");
        cmd.Parameters.AddWithValue("@busqueda", filtro.Busqueda ?? "");

        using var reader = cmd.ExecuteReader();
        var resultado = new List<ConceptoExportacionDto>();

        while (reader.Read())
        {
            resultado.Add(MapearConceptoExportacion(reader));
        }

        return resultado;
    }

    private static ConceptoExportacionDto MapearConceptoExportacion(
        NpgsqlDataReader reader)
    {
        return new ConceptoExportacionDto
        {
            CfdiId = Convert.ToInt64(reader["cfdi_id"]),
            ConceptoId = Convert.ToInt64(reader["concepto_id"]),
            EmpresaId = Convert.ToInt64(reader["empresa_id"]),
            Clasificacion = LeerTextoObligatorio(reader, "clasificacion"),
            TipoCfdi = LeerTextoObligatorio(reader, "tipo_cfdi"),
            FechaEmision = Convert.ToDateTime(reader["fecha_emision"]),
            FechaTimbrado = LeerFechaNullable(reader, "fecha_timbrado"),
            Serie = LeerTextoNullable(reader, "serie"),
            Folio = LeerTextoNullable(reader, "folio"),
            UUID = LeerTextoObligatorio(reader, "uuid"),
            RfcEmisor = LeerTextoObligatorio(reader, "rfc_emisor"),
            NombreEmisor = LeerTextoNullable(reader, "nombre_emisor"),
            RfcReceptor = LeerTextoObligatorio(reader, "rfc_receptor"),
            NombreReceptor = LeerTextoNullable(reader, "nombre_receptor"),
            ClaveProdServ = LeerTextoNullable(reader, "clave_prod_serv"),
            NoIdentificacion = LeerTextoNullable(reader, "no_identificacion"),
            Descripcion = LeerTextoNullable(reader, "descripcion"),
            Cantidad = Convert.ToDecimal(reader["cantidad"]),
            ClaveUnidad = LeerTextoNullable(reader, "clave_unidad"),
            Unidad = LeerTextoNullable(reader, "unidad"),
            ValorUnitario = Convert.ToDecimal(reader["valor_unitario"]),
            Importe = Convert.ToDecimal(reader["importe"]),
            Descuento = LeerDecimalNullable(reader, "descuento"),
            ObjetoImp = LeerTextoNullable(reader, "objeto_imp"),
            CuentaPredial = LeerTextoNullable(reader, "cuenta_predial"),
            Iva16 = LeerDecimalNullable(reader, "iva_16"),
            Iva8 = LeerDecimalNullable(reader, "iva_8"),
            IvaCero = LeerDecimalNullable(reader, "iva_cero"),
            IvaExento = LeerDecimalNullable(reader, "iva_exento"),
            IvaRetenido = LeerDecimalNullable(reader, "iva_retenido"),
            IvaRetenido6 = LeerDecimalNullable(reader, "iva_retenido_6"),
            IsrRetenido = LeerDecimalNullable(reader, "isr_retenido"),
            Ieps = LeerDecimalNullable(reader, "ieps"),
            Ieps3 = LeerDecimalNullable(reader, "ieps_3"),
            Ieps6 = LeerDecimalNullable(reader, "ieps_6"),
            Ieps7 = LeerDecimalNullable(reader, "ieps_7"),
            Ieps8 = LeerDecimalNullable(reader, "ieps_8"),
            Ieps9 = LeerDecimalNullable(reader, "ieps_9"),
            Ieps265 = LeerDecimalNullable(reader, "ieps_265"),
            Ieps30 = LeerDecimalNullable(reader, "ieps_30"),
            Ieps304 = LeerDecimalNullable(reader, "ieps_304"),
            Ieps53 = LeerDecimalNullable(reader, "ieps_53"),
            Ieps160 = LeerDecimalNullable(reader, "ieps_160"),
            TotalImpuestos = LeerDecimalNullable(reader, "total_impuestos"),
            ArchivoXml = RutaXmlLocal.Resolver(LeerTextoObligatorio(reader, "archivo_xml"))
        };
    }
public void ActualizarEstadoSat(
    long empresaId,
    long cfdiId,
    EstadoSatDto estadoSat)
{
    ArgumentNullException.ThrowIfNull(
        estadoSat);

    using var conn =
        new NpgsqlConnection(
            _connectionString);

    conn.Open();

    const string sql = @"
        UPDATE cfdi
        SET
            estado_sat =
                @estado_sat,

            es_cancelable =
                @es_cancelable,

            estatus_cancelacion =
                @estatus_cancelacion,

            validacion_efos =
                @validacion_efos,

            codigo_estatus_sat =
                @codigo_estatus_sat,

            fecha_validacion_sat =
                @fecha_validacion_sat,

            origen_validacion_sat =
                'Consulta SAT'

        WHERE empresa_id =
              @empresa_id

          AND cfdi_id =
              @cfdi_id;";

    using var cmd =
        new NpgsqlCommand(
            sql,
            conn);

    cmd.Parameters.AddWithValue(
        "@empresa_id",
        empresaId);

    cmd.Parameters.AddWithValue(
        "@cfdi_id",
        cfdiId);

    AgregarParametro(
        cmd,
        "@estado_sat",
        estadoSat.Estado);

    AgregarParametro(
        cmd,
        "@es_cancelable",
        estadoSat.EsCancelable);

    AgregarParametro(
        cmd,
        "@estatus_cancelacion",
        estadoSat.EstatusCancelacion);

    AgregarParametro(
        cmd,
        "@validacion_efos",
        estadoSat.ValidacionEfos);

    AgregarParametro(
        cmd,
        "@codigo_estatus_sat",
        estadoSat.CodigoEstatus);

    cmd.Parameters.AddWithValue(
        "@fecha_validacion_sat",
        estadoSat.FechaConsulta);

    int afectados =
        cmd.ExecuteNonQuery();

    if (afectados == 0)
    {
        throw new InvalidOperationException(
            "No se encontró el CFDI que se intentó actualizar.");
    }
}

public List<CfdiValidacionSatDto>
    ObtenerCfdiPendientesValidacionSat(
        long empresaId,
        int cantidad = 100)
{
    using var conn =
        new NpgsqlConnection(
            _connectionString);

    conn.Open();

    const string sql = @"
        SELECT
            c.cfdi_id,
            c.empresa_id,
            c.uuid,
            emisor.rfc AS rfc_emisor,
            receptor.rfc AS rfc_receptor,
            c.total,
            c.estado_sat,
            c.fecha_validacion_sat

        FROM cfdi c

        INNER JOIN personas_fiscales emisor
            ON emisor.persona_id =
               c.emisor_id

        INNER JOIN personas_fiscales receptor
            ON receptor.persona_id =
               c.receptor_id

        WHERE c.empresa_id =
              @empresa_id

          AND
          (
              c.fecha_validacion_sat IS NULL

              OR c.fecha_validacion_sat <
                 NOW() - INTERVAL '24 hours'

              OR COALESCE(
                    c.estado_sat,
                    ''
                 ) IN
                 (
                     '',
                     'ERROR',
                     'Desconocido'
                 )
          )

        ORDER BY
            c.fecha_validacion_sat NULLS FIRST,
            c.fecha_emision DESC

        LIMIT @cantidad;";

    using var cmd =
        new NpgsqlCommand(
            sql,
            conn);

    cmd.Parameters.AddWithValue(
        "@empresa_id",
        empresaId);

    cmd.Parameters.AddWithValue(
        "@cantidad",
        cantidad);

    using var reader =
        cmd.ExecuteReader();

    var resultado =
        new List<CfdiValidacionSatDto>();

    while (reader.Read())
    {
        resultado.Add(
            new CfdiValidacionSatDto
            {
                CfdiId =
                    Convert.ToInt64(
                        reader["cfdi_id"]),

                EmpresaId =
                    Convert.ToInt64(
                        reader["empresa_id"]),

                UUID =
                    reader["uuid"]?.ToString()
                    ?? "",

                RfcEmisor =
                    reader["rfc_emisor"]?.ToString()
                    ?? "",

                RfcReceptor =
                    reader["rfc_receptor"]?.ToString()
                    ?? "",

                Total =
                    Convert.ToDecimal(
                        reader["total"]),

                EstadoSat =
                    reader["estado_sat"] ==
                    DBNull.Value
                        ? null
                        : reader["estado_sat"]
                            ?.ToString(),

                FechaValidacionSat =
                    reader["fecha_validacion_sat"] ==
                    DBNull.Value
                        ? null
                        : Convert.ToDateTime(
                            reader["fecha_validacion_sat"])
            });
    }

    return resultado;
}

public List<XmlAzulejeraDto> ObtenerXmlAzulejeras(
    long empresaId,
    DateTime fechaInicio,
    DateTime fechaFin)
{
    using var conn =
        new NpgsqlConnection(_connectionString);

    conn.Open();

    const string sql = @"
        SELECT
            c.cfdi_id,
            COALESCE(c.uuid, '') AS uuid,
            COALESCE(c.serie, '') AS serie,
            COALESCE(c.ruta_xml, '') AS ruta_xml,
            COALESCE(receptor.rfc, '') AS rfc_receptor,
            COALESCE(receptor.nombre, '') AS nombre_receptor
        FROM cfdi c
        INNER JOIN personas_fiscales receptor
            ON receptor.persona_id = c.receptor_id
        WHERE
            c.empresa_id = @empresa_id
            AND c.clasificacion = 'EMITIDO'
            AND c.fecha_emision >= @fecha_inicio
            AND c.fecha_emision < @fecha_fin
            AND receptor.rfc = 'XAXX010101000'
        ORDER BY
            c.fecha_emision,
            c.cfdi_id;";

    using var cmd =
        new NpgsqlCommand(sql, conn);

    cmd.Parameters.AddWithValue(
        "empresa_id",
        empresaId);

    cmd.Parameters.AddWithValue(
        "fecha_inicio",
        fechaInicio.Date);

    cmd.Parameters.AddWithValue(
        "fecha_fin",
        fechaFin.Date.AddDays(1));

    using var reader =
        cmd.ExecuteReader();

    var resultado =
        new List<XmlAzulejeraDto>();

    while (reader.Read())
    {
        resultado.Add(
            new XmlAzulejeraDto
            {
                CfdiId =
                    reader.GetInt64(
                        reader.GetOrdinal("cfdi_id")),

                UUID =
                    reader.GetString(
                        reader.GetOrdinal("uuid")),

                Serie =
                    reader.GetString(
                        reader.GetOrdinal("serie")),

                RutaXml =
                    RutaXmlLocal.Resolver(reader.GetString(
                        reader.GetOrdinal("ruta_xml"))),

                RfcReceptor =
                    reader.GetString(
                        reader.GetOrdinal("rfc_receptor")),

                NombreReceptor =
                    reader.GetString(
                        reader.GetOrdinal("nombre_receptor"))
            });
    }

    return resultado;
}


public List<EmpresaDto> ObtenerEmpresas()
    {
        var resultado = new List<EmpresaDto>();
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        const string sql = @"
        SELECT
            e.empresa_id,
            e.rfc,
            e.razon_social,
            COALESCE(e.nombre_corto, '') AS nombre_corto,
            COALESCE(e.ruta_xml, '') AS ruta_xml,
            e.activa,

            CASE
                WHEN s.ruta_cer IS NOT NULL
                AND s.ruta_key IS NOT NULL
                AND s.password_key IS NOT NULL
                THEN TRUE
                ELSE FALSE
            END AS tiene_fiel,

            s.fecha_vencimiento

        FROM empresas e
        LEFT JOIN empresas_sat s
            ON e.empresa_id = s.empresa_id
        ORDER BY e.empresa_id;";
        using var cmd = new NpgsqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) resultado.Add(MapearEmpresa(reader));
        return resultado;
    }


}
