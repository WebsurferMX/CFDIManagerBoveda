using CFDIParser.Models;
using Npgsql;

namespace CFDIParser.Services;

public partial class PostgreSqlRepository
{
    /// <summary>Tablas de Exportación a SINUBE. Idempotente; se llama al arrancar.</summary>
    public void AsegurarSinube()
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS empresas_sinube (
                empresa_id BIGINT PRIMARY KEY,
                empresa TEXT NOT NULL DEFAULT '',
                sucursal TEXT NOT NULL DEFAULT 'Matriz',
                usuario TEXT NOT NULL DEFAULT '',
                password TEXT,
                ambiente TEXT NOT NULL DEFAULT 'PRODUCCION',
                modo_producto TEXT NOT NULL DEFAULT 'NOIDENTIFICACION',
                serie_folio_de_folio BOOLEAN NOT NULL DEFAULT FALSE,
                agrupar_codigo_sat BOOLEAN NOT NULL DEFAULT FALSE,
                usar_producto_clientes BOOLEAN NOT NULL DEFAULT FALSE,
                validar_ieps BOOLEAN NOT NULL DEFAULT TRUE,
                incluir_uuid BOOLEAN NOT NULL DEFAULT FALSE,
                incluir_tipo BOOLEAN NOT NULL DEFAULT FALSE,
                emisor_receptor TEXT NOT NULL DEFAULT 'RFC',
                criterio_nombre INTEGER NOT NULL DEFAULT 1,
                ultima_prueba TIMESTAMP,
                resultado_prueba TEXT,
                fecha_actualizacion TIMESTAMP NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS sinube_envios (
                empresa_id BIGINT NOT NULL,
                tipo CHAR(1) NOT NULL,
                uuid TEXT NOT NULL,
                archivo TEXT NOT NULL DEFAULT '',
                serie TEXT NOT NULL DEFAULT '',
                folio TEXT NOT NULL DEFAULT '',
                rfc TEXT NOT NULL DEFAULT '',
                nombre TEXT NOT NULL DEFAULT '',
                tipo_comprobante TEXT NOT NULL DEFAULT '',
                fecha_cfdi TIMESTAMP,
                estatus TEXT NOT NULL,
                mensaje TEXT NOT NULL DEFAULT '',
                fecha_envio TIMESTAMP NOT NULL DEFAULT NOW(),
                PRIMARY KEY (empresa_id, tipo, uuid)
            );

            ALTER TABLE empresas_sinube ADD COLUMN IF NOT EXISTS foliador_llave TEXT NOT NULL DEFAULT '';
            ALTER TABLE empresas_sinube ADD COLUMN IF NOT EXISTS foliador_nombre TEXT NOT NULL DEFAULT '';
            ALTER TABLE empresas_sinube ADD COLUMN IF NOT EXISTS serie_alterna_pagos TEXT NOT NULL DEFAULT '';", cn);

        cmd.ExecuteNonQuery();
    }

    public SinubeConfiguracionDto? ObtenerConfiguracionSinube(long empresaId)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(
            "SELECT * FROM empresas_sinube WHERE empresa_id = @empresa_id", cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);

        using var dr = cmd.ExecuteReader();

        if (!dr.Read())
            return null;

        return new SinubeConfiguracionDto
        {
            EmpresaId = empresaId,
            Empresa = (string)dr["empresa"],
            Sucursal = (string)dr["sucursal"],
            Usuario = (string)dr["usuario"],
            Password = dr["password"] as string,
            Ambiente = (string)dr["ambiente"],
            ModoProducto = (string)dr["modo_producto"],
            SerieFolioDeFolio = (bool)dr["serie_folio_de_folio"],
            AgruparCodigoSat = (bool)dr["agrupar_codigo_sat"],
            UsarProductoClientes = (bool)dr["usar_producto_clientes"],
            ValidarIeps = (bool)dr["validar_ieps"],
            IncluirUuid = (bool)dr["incluir_uuid"],
            IncluirTipo = (bool)dr["incluir_tipo"],
            EmisorReceptor = (string)dr["emisor_receptor"],
            CriterioNombre = (int)dr["criterio_nombre"],
            FoliadorLlave = (string)dr["foliador_llave"],
            FoliadorNombre = (string)dr["foliador_nombre"],
            SerieAlternaPagos = (string)dr["serie_alterna_pagos"],
            UltimaPrueba = dr["ultima_prueba"] as DateTime?,
            ResultadoPrueba = dr["resultado_prueba"] as string
        };
    }

    /// <summary>Guarda la configuración; la contraseña ya debe venir cifrada.</summary>
    public void GuardarConfiguracionSinube(SinubeConfiguracionDto modelo)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO empresas_sinube (
                empresa_id, empresa, sucursal, usuario, password, ambiente,
                modo_producto, serie_folio_de_folio, agrupar_codigo_sat, usar_producto_clientes, validar_ieps,
                incluir_uuid, incluir_tipo, emisor_receptor, criterio_nombre, foliador_llave, foliador_nombre,
                serie_alterna_pagos)
            VALUES (
                @empresa_id, @empresa, @sucursal, @usuario, @password, @ambiente,
                @modo_producto, @serie_folio_de_folio, @agrupar_codigo_sat, @usar_producto_clientes, @validar_ieps,
                @incluir_uuid, @incluir_tipo, @emisor_receptor, @criterio_nombre, @foliador_llave, @foliador_nombre,
                @serie_alterna_pagos)
            ON CONFLICT (empresa_id) DO UPDATE SET
                empresa = @empresa,
                sucursal = @sucursal,
                usuario = @usuario,
                password = @password,
                ambiente = @ambiente,
                modo_producto = @modo_producto,
                serie_folio_de_folio = @serie_folio_de_folio,
                agrupar_codigo_sat = @agrupar_codigo_sat,
                usar_producto_clientes = @usar_producto_clientes,
                validar_ieps = @validar_ieps,
                incluir_uuid = @incluir_uuid,
                incluir_tipo = @incluir_tipo,
                emisor_receptor = @emisor_receptor,
                criterio_nombre = @criterio_nombre,
                foliador_llave = @foliador_llave,
                foliador_nombre = @foliador_nombre,
                serie_alterna_pagos = @serie_alterna_pagos,
                fecha_actualizacion = NOW();", cn);

        cmd.Parameters.AddWithValue("empresa_id", modelo.EmpresaId);
        cmd.Parameters.AddWithValue("empresa", modelo.Empresa.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("sucursal", modelo.Sucursal.Trim());
        cmd.Parameters.AddWithValue("usuario", modelo.Usuario.Trim());
        cmd.Parameters.AddWithValue("password", (object?)modelo.Password ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ambiente", modelo.Ambiente);
        cmd.Parameters.AddWithValue("modo_producto", modelo.ModoProducto);
        cmd.Parameters.AddWithValue("serie_folio_de_folio", modelo.SerieFolioDeFolio);
        cmd.Parameters.AddWithValue("agrupar_codigo_sat", modelo.AgruparCodigoSat);
        cmd.Parameters.AddWithValue("usar_producto_clientes", modelo.UsarProductoClientes);
        cmd.Parameters.AddWithValue("validar_ieps", modelo.ValidarIeps);
        cmd.Parameters.AddWithValue("incluir_uuid", modelo.IncluirUuid);
        cmd.Parameters.AddWithValue("incluir_tipo", modelo.IncluirTipo);
        cmd.Parameters.AddWithValue("emisor_receptor", modelo.EmisorReceptor);
        cmd.Parameters.AddWithValue("criterio_nombre", modelo.CriterioNombre);
        cmd.Parameters.AddWithValue("foliador_llave", modelo.FoliadorLlave);
        cmd.Parameters.AddWithValue("foliador_nombre", modelo.FoliadorNombre);
        cmd.Parameters.AddWithValue("serie_alterna_pagos", string.Join(",", modelo.SeriesAlternasPagos));

        cmd.ExecuteNonQuery();
    }

    public void RegistrarPruebaSinube(long empresaId, string resultado)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            UPDATE empresas_sinube
            SET ultima_prueba = NOW(),
                resultado_prueba = @resultado
            WHERE empresa_id = @empresa_id;", cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("resultado", resultado);

        cmd.ExecuteNonQuery();
    }

    /// <summary>UUID que SINUBE ya aceptó (OK o EXISTE) para no reenviarlos.</summary>
    public HashSet<string> ObtenerUuidsEnviadosSinube(long empresaId, string tipo)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT uuid FROM sinube_envios
            WHERE empresa_id = @empresa_id
              AND tipo = @tipo
              AND estatus IN ('OK', 'EXISTE');", cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("tipo", tipo);

        var uuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var dr = cmd.ExecuteReader();

        while (dr.Read())
            uuids.Add(dr.GetString(0));

        return uuids;
    }

    public void RegistrarEnvioSinube(SinubeEnvioDto envio)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO sinube_envios (
                empresa_id, tipo, uuid, archivo, serie, folio, rfc, nombre,
                tipo_comprobante, fecha_cfdi, estatus, mensaje, fecha_envio)
            VALUES (
                @empresa_id, @tipo, @uuid, @archivo, @serie, @folio, @rfc, @nombre,
                @tipo_comprobante, @fecha_cfdi, @estatus, @mensaje, NOW())
            ON CONFLICT (empresa_id, tipo, uuid) DO UPDATE SET
                archivo = @archivo,
                serie = @serie,
                folio = @folio,
                rfc = @rfc,
                nombre = @nombre,
                tipo_comprobante = @tipo_comprobante,
                fecha_cfdi = @fecha_cfdi,
                estatus = @estatus,
                mensaje = @mensaje,
                fecha_envio = NOW();", cn);

        cmd.Parameters.AddWithValue("empresa_id", envio.EmpresaId);
        cmd.Parameters.AddWithValue("tipo", envio.Tipo);
        cmd.Parameters.AddWithValue("uuid", envio.Uuid);
        cmd.Parameters.AddWithValue("archivo", envio.Archivo);
        cmd.Parameters.AddWithValue("serie", envio.Serie);
        cmd.Parameters.AddWithValue("folio", envio.Folio);
        cmd.Parameters.AddWithValue("rfc", envio.Rfc);
        cmd.Parameters.AddWithValue("nombre", envio.Nombre);
        cmd.Parameters.AddWithValue("tipo_comprobante", envio.TipoComprobante);
        cmd.Parameters.AddWithValue("fecha_cfdi", (object?)envio.FechaCfdi ?? DBNull.Value);
        cmd.Parameters.AddWithValue("estatus", envio.Estatus);
        cmd.Parameters.AddWithValue("mensaje", envio.Mensaje);

        cmd.ExecuteNonQuery();
    }

    public List<SinubeEnvioDto> ObtenerHistorialSinube(long empresaId, int limite)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT * FROM sinube_envios
            WHERE empresa_id = @empresa_id
            ORDER BY fecha_envio DESC
            LIMIT @limite;", cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("limite", limite);

        var historial = new List<SinubeEnvioDto>();

        using var dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            historial.Add(new SinubeEnvioDto
            {
                EmpresaId = empresaId,
                Tipo = ((string)dr["tipo"]).Trim(),
                Uuid = (string)dr["uuid"],
                Archivo = (string)dr["archivo"],
                Serie = (string)dr["serie"],
                Folio = (string)dr["folio"],
                Rfc = (string)dr["rfc"],
                Nombre = (string)dr["nombre"],
                TipoComprobante = (string)dr["tipo_comprobante"],
                FechaCfdi = dr["fecha_cfdi"] as DateTime?,
                Estatus = (string)dr["estatus"],
                Mensaje = (string)dr["mensaje"],
                FechaEnvio = (DateTime)dr["fecha_envio"]
            });
        }

        return historial;
    }

    /// <summary>Olvida los envíos de la empresa para poder reenviarlos.</summary>
    public int BorrarHistorialSinube(long empresaId, string? tipo)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            DELETE FROM sinube_envios
            WHERE empresa_id = @empresa_id
              AND (@tipo = '' OR tipo = @tipo);", cn);

        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        cmd.Parameters.AddWithValue("tipo", tipo ?? "");

        return cmd.ExecuteNonQuery();
    }
}
