using System.Text;
using CFDIParser.Models;
using Npgsql;

namespace CFDIParser.Services;

/// <summary>Consultas de la pantalla Administración de XML.</summary>
public partial class PostgreSqlRepository
{
    private static readonly Dictionary<string, string> PestanasAdminXml =
        new()
        {
            ["facturas"] = "c.tipo_cfdi = 'I'",
            ["notascredito"] = "c.tipo_cfdi = 'E'",
            ["traslados"] = "c.tipo_cfdi = 'T'",
            ["pagos"] = "c.tipo_cfdi = 'P'",
            ["nomina"] = "c.tipo_cfdi = 'N'",
            // El complemento de nómina 1.1 solo existió con CFDI 3.2; con 3.3 y 4.0 es 1.2.
            ["nomina11"] = $"c.tipo_cfdi = 'N' AND {EsCfdi32}",
            ["nomina12"] = $"c.tipo_cfdi = 'N' AND NOT {EsCfdi32}"
        };

    private const string EsCfdi32 = "COALESCE(c.version_cfdi, '') LIKE '3.2%'";

    private static readonly Dictionary<string, string> FiltrosAdminXml =
        new()
        {
            ["vigentes"] = "LOWER(COALESCE(c.estado_sat, '')) = 'vigente'",
            ["cancelados"] = "LOWER(COALESCE(c.estado_sat, '')) = 'cancelado'",
            // Al importar se guarda PENDIENTE; versiones anteriores dejaban NULL o NO VALIDADO.
            ["sinvalidar"] = "LOWER(COALESCE(c.estado_sat, '')) IN ('', 'pendiente', 'no validado')",
            ["verificados"] = "v.cfdi_id IS NOT NULL",
            ["noverificados"] = "v.cfdi_id IS NULL",
            // El mismo XML (UUID) copiado en más de un archivo de la carpeta de la empresa.
            ["duplicados"] = @"(SELECT COUNT(*) FROM storage_archivos s
                                WHERE s.empresa_id = c.empresa_id AND s.uuid = UPPER(c.uuid)) > 1",
            ["ppd"] = "UPPER(COALESCE(c.metodo_pago, '')) = 'PPD'"
        };

    private static readonly Dictionary<string, (string Clave, string Nombre)> AgrupacionesAdminXml =
        new()
        {
            ["emisor"] = ("emisor.rfc", "MAX(emisor.nombre)"),
            ["receptor"] = ("receptor.rfc", "MAX(receptor.nombre)"),
            ["mes"] = ("TO_CHAR(c.fecha_emision, 'YYYY-MM')", "''"),
            ["uso"] = ("COALESCE(c.uso_cfdi, '')", "''"),
            ["metodo"] = ("COALESCE(c.metodo_pago, '')", "''"),
            ["tipo"] = ("c.tipo_cfdi", "''")
        };

    private const string DesdeAdminXml = @"
        cfdi c
        INNER JOIN personas_fiscales emisor
            ON emisor.persona_id = c.emisor_id
        INNER JOIN personas_fiscales receptor
            ON receptor.persona_id = c.receptor_id
        LEFT JOIN cfdi_verificados v
            ON v.cfdi_id = c.cfdi_id";

    private static string ImpuestoGlobal(string tipo, string impuesto) => $@"
        COALESCE((
            SELECT SUM(g.importe)
            FROM cfdi_impuestos_globales g
            WHERE g.cfdi_id = c.cfdi_id
              AND g.tipo = '{tipo}'
              AND g.impuesto = '{impuesto}'), 0)";

    private bool _tablasAdminXmlListas;

    /// <summary>
    /// Crea la tabla de marcas "Verificado ó Asoc." si no existe.
    /// Es aditiva: no modifica ninguna tabla existente.
    /// </summary>
    public void AsegurarTablasAdministracionXml()
    {
        if (_tablasAdminXmlListas)
        {
            return;
        }

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS cfdi_verificados (
                cfdi_id BIGINT PRIMARY KEY
                    REFERENCES cfdi (cfdi_id) ON DELETE CASCADE,
                fecha_marca TIMESTAMP NOT NULL DEFAULT NOW()
            );", conn);

        cmd.ExecuteNonQuery();

        _tablasAdminXmlListas = true;
    }

    public List<int> ObtenerAniosAdministracionXml(
        long empresaId,
        string clasificacion)
    {
        return LeerEnteros(@"
            SELECT DISTINCT EXTRACT(YEAR FROM fecha_emision)::int AS valor
            FROM cfdi
            WHERE empresa_id = @empresa_id
              AND clasificacion = @clasificacion
            ORDER BY valor DESC;",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@empresa_id", empresaId);
                cmd.Parameters.AddWithValue("@clasificacion", clasificacion);
            });
    }

    public List<int> ObtenerMesesAdministracionXml(
        long empresaId,
        string clasificacion,
        int anio)
    {
        return LeerEnteros(@"
            SELECT DISTINCT EXTRACT(MONTH FROM fecha_emision)::int AS valor
            FROM cfdi
            WHERE empresa_id = @empresa_id
              AND clasificacion = @clasificacion
              AND fecha_emision >= @inicio
              AND fecha_emision < @fin
            ORDER BY valor;",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@empresa_id", empresaId);
                cmd.Parameters.AddWithValue("@clasificacion", clasificacion);
                cmd.Parameters.AddWithValue("@inicio", new DateTime(anio, 1, 1));
                cmd.Parameters.AddWithValue("@fin", new DateTime(anio + 1, 1, 1));
            });
    }

    /// <summary>
    /// Días del mes del filtro que tienen comprobantes, con cuántos coinciden con el filtro completo
    /// (pestaña, búsqueda, "Filtrar"). Un día sin coincidencias aparece con 0. Ignora filtro.Dias.
    /// </summary>
    public List<(int Dia, int Cantidad)> ObtenerDiasAdministracionXml(AdminXmlFiltroDto filtro)
    {
        if (filtro.Anio is not int anio || filtro.Meses.Count != 1)
            return new();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        var dias = new SortedDictionary<int, int>();

        using (var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT EXTRACT(DAY FROM fecha_emision)::int
            FROM cfdi
            WHERE empresa_id = @empresa_id
              AND clasificacion = @clasificacion
              AND fecha_emision >= @inicio
              AND fecha_emision < @fin;", conn))
        {
            cmd.Parameters.AddWithValue("@empresa_id", filtro.EmpresaId);
            cmd.Parameters.AddWithValue("@clasificacion", filtro.Clasificacion);
            cmd.Parameters.AddWithValue("@inicio", new DateTime(anio, filtro.Meses[0], 1));
            cmd.Parameters.AddWithValue("@fin", new DateTime(anio, filtro.Meses[0], 1).AddMonths(1));

            using var dr = cmd.ExecuteReader();
            while (dr.Read())
                dias[dr.GetInt32(0)] = 0;
        }

        using (var cmd = new NpgsqlCommand { Connection = conn })
        {
            var mes = new AdminXmlFiltroDto
            {
                EmpresaId = filtro.EmpresaId,
                Clasificacion = filtro.Clasificacion,
                Anio = filtro.Anio,
                Meses = filtro.Meses,
                Pestana = filtro.Pestana,
                Busqueda = filtro.Busqueda,
                Filtrar = filtro.Filtrar
            };

            string condiciones = CondicionesAdminXml(cmd, mes, aplicarPestana: true);
            cmd.CommandText = $@"
                SELECT EXTRACT(DAY FROM c.fecha_emision)::int AS dia, COUNT(*)::int
                FROM {DesdeAdminXml}
                WHERE {condiciones}
                GROUP BY dia;";

            using var dr = cmd.ExecuteReader();
            while (dr.Read())
                dias[dr.GetInt32(0)] = dr.GetInt32(1);
        }

        return dias.Select(d => (d.Key, d.Value)).ToList();
    }

    public List<AdminXmlFilaDto> BuscarAdministracionXml(
        AdminXmlFiltroDto filtro,
        int pagina,
        int tamano)
    {
        AsegurarTablasAdministracionXml();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand();
        cmd.Connection = conn;

        string condiciones = CondicionesAdminXml(cmd, filtro, aplicarPestana: true);

        cmd.CommandText = $@"
            SELECT
                c.cfdi_id,
                c.uuid,
                COALESCE(c.clasificacion, '') AS clasificacion,
                COALESCE(c.tipo_cfdi, '') AS tipo_cfdi,
                COALESCE(c.version_cfdi, '') AS version_cfdi,
                c.fecha_emision,
                c.fecha_timbrado,
                COALESCE(c.serie, '') AS serie,
                COALESCE(c.folio, '') AS folio,
                emisor.rfc AS rfc_emisor,
                emisor.nombre AS nombre_emisor,
                receptor.rfc AS rfc_receptor,
                receptor.nombre AS nombre_receptor,
                COALESCE(c.uso_cfdi, '') AS uso_cfdi,
                COALESCE(c.metodo_pago, '') AS metodo_pago,
                COALESCE(c.forma_pago, '') AS forma_pago,
                COALESCE(c.moneda, '') AS moneda,
                c.tipo_cambio,
                COALESCE(c.subtotal, 0) AS subtotal,
                COALESCE(c.descuento, 0) AS descuento,
                {ImpuestoGlobal("TRASLADO", "002")} AS iva,
                {ImpuestoGlobal("TRASLADO", "003")} AS ieps,
                {ImpuestoGlobal("RETENCION", "002")} AS ret_iva,
                {ImpuestoGlobal("RETENCION", "001")} AS ret_isr,
                COALESCE(c.total, 0) AS total,
                c.monto_total_pagos,
                COALESCE(c.estado_sat, '') AS estado_sat,
                c.fecha_validacion_sat,
                COALESCE(c.origen_validacion_sat, '') AS origen_validacion_sat,
                v.cfdi_id IS NOT NULL AS verificado,
                COALESCE(c.ruta_xml, '') AS ruta_xml
            FROM {DesdeAdminXml}
            WHERE {condiciones}
            ORDER BY {OrdenAdminXml(filtro)}
            LIMIT @limite
            OFFSET @desplazamiento;";

        cmd.Parameters.AddWithValue("@limite", tamano);
        cmd.Parameters.AddWithValue("@desplazamiento", pagina * tamano);

        var filas = new List<AdminXmlFilaDto>();

        using var dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            filas.Add(new AdminXmlFilaDto
            {
                CfdiId = dr.GetInt64(dr.GetOrdinal("cfdi_id")),
                Uuid = Texto(dr, "uuid"),
                Clasificacion = Texto(dr, "clasificacion"),
                TipoCfdi = Texto(dr, "tipo_cfdi"),
                Version = Texto(dr, "version_cfdi"),
                FechaEmision = dr.GetDateTime(dr.GetOrdinal("fecha_emision")),
                FechaTimbrado = dr.IsDBNull(dr.GetOrdinal("fecha_timbrado")) ? null : dr.GetDateTime(dr.GetOrdinal("fecha_timbrado")),
                Serie = Texto(dr, "serie"),
                Folio = Texto(dr, "folio"),
                RfcEmisor = Texto(dr, "rfc_emisor"),
                NombreEmisor = Texto(dr, "nombre_emisor"),
                RfcReceptor = Texto(dr, "rfc_receptor"),
                NombreReceptor = Texto(dr, "nombre_receptor"),
                UsoCfdi = Texto(dr, "uso_cfdi"),
                MetodoPago = Texto(dr, "metodo_pago"),
                FormaPago = Texto(dr, "forma_pago"),
                Moneda = Texto(dr, "moneda"),
                TipoCambio = dr.IsDBNull(dr.GetOrdinal("tipo_cambio")) ? null : dr.GetDecimal(dr.GetOrdinal("tipo_cambio")),
                Subtotal = dr.GetDecimal(dr.GetOrdinal("subtotal")),
                Descuento = dr.GetDecimal(dr.GetOrdinal("descuento")),
                Iva = dr.GetDecimal(dr.GetOrdinal("iva")),
                Ieps = dr.GetDecimal(dr.GetOrdinal("ieps")),
                RetIva = dr.GetDecimal(dr.GetOrdinal("ret_iva")),
                RetIsr = dr.GetDecimal(dr.GetOrdinal("ret_isr")),
                Total = dr.GetDecimal(dr.GetOrdinal("total")),
                MontoTotalPagos = dr.IsDBNull(dr.GetOrdinal("monto_total_pagos")) ? null : dr.GetDecimal(dr.GetOrdinal("monto_total_pagos")),
                EstadoSat = Texto(dr, "estado_sat"),
                FechaValidacionSat = dr.IsDBNull(dr.GetOrdinal("fecha_validacion_sat")) ? null : dr.GetDateTime(dr.GetOrdinal("fecha_validacion_sat")),
                OrigenValidacionSat = Texto(dr, "origen_validacion_sat"),
                Verificado = dr.GetBoolean(dr.GetOrdinal("verificado")),
                RutaXml = RutaXmlLocal.Resolver(Texto(dr, "ruta_xml"))
            });
        }

        return filas;
    }

    // Solo claves conocidas llegan al SQL. Son alias del SELECT, salvo folio, que se ordena por su número.
    private static readonly Dictionary<string, string> ColumnasOrdenAdminXml = new()
    {
        ["verificado"] = "verificado",
        ["estado_sat"] = "estado_sat",
        ["validacion"] = "fecha_validacion_sat",
        ["version"] = "version_cfdi",
        ["tipo"] = "tipo_cfdi",
        ["fecha"] = "fecha_emision",
        ["timbrado"] = "fecha_timbrado",
        ["serie"] = "serie",
        ["folio"] = "NULLIF(regexp_replace(COALESCE(c.folio, ''), '\\D', '', 'g'), '')::numeric {0}, COALESCE(c.folio, '')",
        ["uuid"] = "uuid",
        ["rfc_emisor"] = "rfc_emisor",
        ["nombre_emisor"] = "nombre_emisor",
        ["rfc_receptor"] = "rfc_receptor",
        ["nombre_receptor"] = "nombre_receptor",
        ["uso"] = "uso_cfdi",
        ["metodo"] = "metodo_pago",
        ["forma"] = "forma_pago",
        ["moneda"] = "moneda",
        ["subtotal"] = "subtotal",
        ["descuento"] = "descuento",
        ["iva"] = "iva",
        ["ieps"] = "ieps",
        ["ret_iva"] = "ret_iva",
        ["ret_isr"] = "ret_isr",
        ["total"] = "total",
        ["pagos"] = "monto_total_pagos"
    };

    private static string OrdenAdminXml(AdminXmlFiltroDto filtro)
    {
        if (!ColumnasOrdenAdminXml.TryGetValue(filtro.Orden, out string? columna))
        {
            return "c.fecha_emision DESC, c.cfdi_id DESC";
        }

        string sentido = filtro.Descendente ? "DESC" : "ASC";

        // {0}: sentido de la primera expresión cuando la columna ordena por dos.
        string expresion = string.Format(columna, $"{sentido} NULLS LAST");

        return $"{expresion} {sentido} NULLS LAST, c.cfdi_id {sentido}";
    }

    /// <summary>Todas las filas del filtro (sin paginar), para procesos masivos y reportes.</summary>
    public List<AdminXmlFilaDto> ObtenerSeleccionAdministracionXml(
        AdminXmlFiltroDto filtro)
    {
        return BuscarAdministracionXml(filtro, 0, int.MaxValue);
    }

    /// <summary>Conteos del panel de totales; no aplica la pestaña salvo en EnPestana.</summary>
    public AdminXmlTotalesDto ObtenerTotalesAdministracionXml(
        AdminXmlFiltroDto filtro)
    {
        AsegurarTablasAdministracionXml();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        var totales = new AdminXmlTotalesDto();

        using (var cmd = new NpgsqlCommand { Connection = conn })
        {
            string condiciones = CondicionesAdminXml(cmd, filtro, aplicarPestana: false);

            cmd.CommandText = $@"
                SELECT
                    COUNT(*) FILTER (WHERE c.tipo_cfdi = 'I') AS fi,
                    COALESCE(SUM(c.total) FILTER (WHERE c.tipo_cfdi = 'I'), 0) AS ti,
                    COUNT(*) FILTER (WHERE c.tipo_cfdi = 'E') AS fe,
                    COALESCE(SUM(c.total) FILTER (WHERE c.tipo_cfdi = 'E'), 0) AS te,
                    COUNT(*) FILTER (WHERE c.tipo_cfdi = 'T') AS ft,
                    COALESCE(SUM(c.total) FILTER (WHERE c.tipo_cfdi = 'T'), 0) AS tt,
                    COUNT(*) FILTER (WHERE c.tipo_cfdi = 'P') AS fp,
                    COALESCE(SUM(COALESCE(c.monto_total_pagos, 0)) FILTER (WHERE c.tipo_cfdi = 'P'), 0) AS tp,
                    COUNT(*) FILTER (WHERE c.tipo_cfdi = 'N') AS fn,
                    COALESCE(SUM(c.total) FILTER (WHERE c.tipo_cfdi = 'N'), 0) AS tn,
                    COUNT(*) FILTER (WHERE c.tipo_cfdi = 'N' AND {EsCfdi32}) AS fn11,
                    COALESCE(SUM(c.total) FILTER (WHERE c.tipo_cfdi = 'N' AND {EsCfdi32}), 0) AS tn11,
                    COUNT(*) AS archivos,
                    COALESCE(SUM(c.total) FILTER (WHERE LOWER(COALESCE(c.estado_sat, '')) = 'vigente'), 0) AS vigente,
                    COALESCE(SUM(c.total) FILTER (WHERE LOWER(COALESCE(c.estado_sat, '')) = 'cancelado'), 0) AS cancelado
                FROM {DesdeAdminXml}
                WHERE {condiciones};";

            using var dr = cmd.ExecuteReader();
            dr.Read();

            AdminXmlTotalDto Par(string n, string t) =>
                new() { Cantidad = Convert.ToInt32(dr[n]), Total = Convert.ToDecimal(dr[t]) };

            totales.Facturas = Par("fi", "ti");
            totales.NotasCredito = Par("fe", "te");
            totales.Traslados = Par("ft", "tt");
            totales.Pagos = Par("fp", "tp");
            totales.Nominas = Par("fn", "tn");
            totales.Nominas11 = Par("fn11", "tn11");
            totales.Nominas12 = new AdminXmlTotalDto
            {
                Cantidad = totales.Nominas.Cantidad - totales.Nominas11.Cantidad,
                Total = totales.Nominas.Total - totales.Nominas11.Total
            };
            totales.Archivos = Convert.ToInt32(dr["archivos"]);
            totales.Vigente = Convert.ToDecimal(dr["vigente"]);
            totales.Cancelado = Convert.ToDecimal(dr["cancelado"]);
        }

        if (string.IsNullOrEmpty(filtro.Pestana))
        {
            totales.EnPestana = totales.Archivos;
        }
        else
        {
            using var cmd = new NpgsqlCommand { Connection = conn };
            string condiciones = CondicionesAdminXml(cmd, filtro, aplicarPestana: true);
            cmd.CommandText = $"SELECT COUNT(*) FROM {DesdeAdminXml} WHERE {condiciones};";
            totales.EnPestana = Convert.ToInt32(cmd.ExecuteScalar());
        }

        return totales;
    }

    /// <summary>Conceptos de un CFDI con sus impuestos, tal como se guardaron al importar.</summary>
    public List<ConceptoDto> ObtenerConceptosCfdi(
        long empresaId,
        long cfdiId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        var conceptos = new Dictionary<long, ConceptoDto>();

        using (var cmd = new NpgsqlCommand(@"
            SELECT k.concepto_id, k.clave_prod_serv, k.no_identificacion, k.descripcion, k.cantidad,
                   k.clave_unidad, k.unidad, k.valor_unitario, k.importe, k.descuento, k.objeto_imp
            FROM cfdi_conceptos k
            INNER JOIN cfdi c ON c.cfdi_id = k.cfdi_id
            WHERE c.empresa_id = @empresa_id AND k.cfdi_id = @cfdi_id
            ORDER BY k.concepto_id;", conn))
        {
            cmd.Parameters.AddWithValue("@empresa_id", empresaId);
            cmd.Parameters.AddWithValue("@cfdi_id", cfdiId);

            using var dr = cmd.ExecuteReader();

            while (dr.Read())
            {
                conceptos[dr.GetInt64(0)] = new ConceptoDto
                {
                    ClaveProdServ = Texto(dr, "clave_prod_serv"),
                    NoIdentificacion = Texto(dr, "no_identificacion"),
                    Descripcion = Texto(dr, "descripcion"),
                    Cantidad = Decimal(dr, "cantidad"),
                    ClaveUnidad = Texto(dr, "clave_unidad"),
                    Unidad = Texto(dr, "unidad"),
                    ValorUnitario = Decimal(dr, "valor_unitario"),
                    Importe = Decimal(dr, "importe"),
                    Descuento = dr.IsDBNull(dr.GetOrdinal("descuento")) ? null : dr.GetDecimal(dr.GetOrdinal("descuento")),
                    ObjetoImp = Texto(dr, "objeto_imp")
                };
            }
        }

        if (conceptos.Count == 0)
        {
            return new();
        }

        using (var cmd = new NpgsqlCommand(@"
            SELECT concepto_id, tipo, impuesto, base, tipo_factor, tasa, importe
            FROM cfdi_impuestos
            WHERE concepto_id = ANY(@ids)
            ORDER BY concepto_id;", conn))
        {
            cmd.Parameters.AddWithValue("@ids", conceptos.Keys.ToArray());

            using var dr = cmd.ExecuteReader();

            while (dr.Read())
            {
                conceptos[dr.GetInt64(0)].Impuestos.Add(new ImpuestoDto
                {
                    Tipo = Texto(dr, "tipo"),
                    Impuesto = Texto(dr, "impuesto"),
                    Base = Decimal(dr, "base"),
                    TipoFactor = Texto(dr, "tipo_factor"),
                    Tasa = Decimal(dr, "tasa"),
                    Importe = Decimal(dr, "importe")
                });
            }
        }

        return conceptos.Values.ToList();
    }

    public List<CfdiRelacionadoDto> ObtenerRelacionadosCfdi(
        long empresaId,
        long cfdiId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT r.tipo_relacion, r.uuid_relacionado
            FROM cfdi_relacionados r
            INNER JOIN cfdi c ON c.cfdi_id = r.cfdi_id
            WHERE c.empresa_id = @empresa_id AND r.cfdi_id = @cfdi_id;", conn);

        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@cfdi_id", cfdiId);

        var lista = new List<CfdiRelacionadoDto>();

        using var dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            lista.Add(new CfdiRelacionadoDto
            {
                TipoRelacion = Texto(dr, "tipo_relacion"),
                UUID = Texto(dr, "uuid_relacionado")
            });
        }

        return lista;
    }

    /// <summary>Marca o desmarca "Verificado ó Asoc." en los CFDI de la empresa.</summary>
    public int MarcarVerificadosAdministracionXml(
        long empresaId,
        IReadOnlyCollection<long> cfdiIds,
        bool verificado)
    {
        AsegurarTablasAdministracionXml();

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        // Solo se aceptan CFDI de la empresa indicada.
        using var cmd = new NpgsqlCommand(verificado
            ? @"INSERT INTO cfdi_verificados (cfdi_id)
                SELECT cfdi_id FROM cfdi WHERE empresa_id = @empresa_id AND cfdi_id = ANY(@ids)
                ON CONFLICT (cfdi_id) DO NOTHING;"
            : @"DELETE FROM cfdi_verificados v
                USING cfdi c
                WHERE c.cfdi_id = v.cfdi_id AND c.empresa_id = @empresa_id AND v.cfdi_id = ANY(@ids);", conn);

        cmd.Parameters.AddWithValue("@empresa_id", empresaId);
        cmd.Parameters.AddWithValue("@ids", cfdiIds.ToArray());

        return cmd.ExecuteNonQuery();
    }

    /// <summary>Facturas menos notas de crédito agrupadas (emisor, receptor, mes, uso, método o tipo).</summary>
    public List<AdminXmlAgrupacionDto> AgruparAdministracionXml(
        AdminXmlFiltroDto filtro,
        string por)
    {
        AsegurarTablasAdministracionXml();

        (string clave, string nombre) = AgrupacionesAdminXml.GetValueOrDefault(por, AgrupacionesAdminXml["emisor"]);

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand { Connection = conn };

        string condiciones = CondicionesAdminXml(cmd, filtro, aplicarPestana: false);

        string signo = "CASE WHEN c.tipo_cfdi = 'E' THEN -1 ELSE 1 END";

        cmd.CommandText = $@"
            SELECT {clave} AS clave,
                   {nombre} AS nombre,
                   COUNT(*) AS cantidad,
                   COALESCE(SUM({signo} * COALESCE(c.subtotal, 0)), 0) AS subtotal,
                   COALESCE(SUM({signo} * {ImpuestoGlobal("TRASLADO", "002")}), 0) AS iva,
                   COALESCE(SUM({signo} * COALESCE(c.total, 0)), 0) AS total
            FROM {DesdeAdminXml}
            WHERE {condiciones} AND c.tipo_cfdi IN ('I', 'E')
            GROUP BY 1
            ORDER BY total DESC;";

        var lista = new List<AdminXmlAgrupacionDto>();

        using var dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            lista.Add(new AdminXmlAgrupacionDto
            {
                Clave = Texto(dr, "clave"),
                Nombre = Texto(dr, "nombre"),
                Cantidad = Convert.ToInt32(dr["cantidad"]),
                Subtotal = Decimal(dr, "subtotal"),
                Iva = Decimal(dr, "iva"),
                Total = Decimal(dr, "total")
            });
        }

        return lista;
    }

    /// <summary>Folios numéricos faltantes y repetidos por serie.</summary>
    public List<AdminXmlSerieDto> VerificarSeriesAdministracionXml(
        AdminXmlFiltroDto filtro)
    {
        var porSerie = new Dictionary<string, (HashSet<string> Tipos, Dictionary<long, int> Folios)>();

        foreach (AdminXmlFilaDto f in ObtenerSeleccionAdministracionXml(filtro))
        {
            if (!long.TryParse(f.Folio, out long folio))
            {
                continue;
            }

            if (!porSerie.TryGetValue(f.Serie, out var s))
            {
                s = (new HashSet<string>(), new Dictionary<long, int>());
                porSerie[f.Serie] = s;
            }

            s.Tipos.Add(f.TipoCfdi);
            s.Folios[folio] = s.Folios.GetValueOrDefault(folio) + 1;
        }

        var resultado = new List<AdminXmlSerieDto>();

        foreach (var (serie, s) in porSerie.OrderBy(x => x.Key))
        {
            List<long> folios = s.Folios.Keys.Order().ToList();
            var faltantes = new List<long>();

            for (int i = 1; i < folios.Count && faltantes.Count < 500; i++)
            {
                for (long m = folios[i - 1] + 1; m < folios[i] && faltantes.Count < 500; m++)
                {
                    faltantes.Add(m);
                }
            }

            resultado.Add(new AdminXmlSerieDto
            {
                Serie = serie == "" ? "(sin serie)" : serie,
                Tipos = string.Join(",", s.Tipos.Order()),
                Desde = folios[0],
                Hasta = folios[^1],
                Cantidad = folios.Count,
                Faltantes = faltantes,
                Repetidos = s.Folios.Where(x => x.Value > 1).Select(x => x.Key).Order().ToList()
            });
        }

        return resultado;
    }

    private static string CondicionesAdminXml(
        NpgsqlCommand cmd,
        AdminXmlFiltroDto f,
        bool aplicarPestana)
    {
        if (f.EmpresaId <= 0)
        {
            throw new ArgumentException("Selecciona una empresa.");
        }

        var w = new StringBuilder("c.empresa_id = @empresa_id");
        cmd.Parameters.AddWithValue("@empresa_id", f.EmpresaId);

        // Los renglones marcados se respetan tal cual, sin los demás filtros.
        if (f.Ids.Count > 0)
        {
            w.Append(" AND c.cfdi_id = ANY(@ids)");
            cmd.Parameters.AddWithValue("@ids", f.Ids.ToArray());
            return w.ToString();
        }

        w.Append(" AND c.clasificacion = @clasificacion");
        cmd.Parameters.AddWithValue("@clasificacion", f.Clasificacion);

        if (f.Anio is int anio)
        {
            if (f.Meses.Count > 0 && f.Acumular)
            {
                int ultimo = f.Meses.Max();
                w.Append(" AND c.fecha_emision >= @inicio AND c.fecha_emision < @fin");
                cmd.Parameters.AddWithValue("@inicio", new DateTime(anio, 1, 1));
                cmd.Parameters.AddWithValue("@fin", new DateTime(anio, ultimo, 1).AddMonths(1));
            }
            else if (f.Meses.Count == 1 && f.Dias.Count > 0)
            {
                w.Append(" AND c.fecha_emision >= @inicio AND c.fecha_emision < @fin" +
                         " AND EXTRACT(DAY FROM c.fecha_emision)::int = ANY(@dias)");
                cmd.Parameters.AddWithValue("@inicio", new DateTime(anio, f.Meses[0], 1));
                cmd.Parameters.AddWithValue("@fin", new DateTime(anio, f.Meses[0], 1).AddMonths(1));
                cmd.Parameters.AddWithValue("@dias", f.Dias.ToArray());
            }
            else if (f.Meses.Count > 0)
            {
                w.Append(" AND c.fecha_emision >= @inicio AND c.fecha_emision < @fin" +
                         " AND EXTRACT(MONTH FROM c.fecha_emision)::int = ANY(@meses)");
                cmd.Parameters.AddWithValue("@inicio", new DateTime(anio, 1, 1));
                cmd.Parameters.AddWithValue("@fin", new DateTime(anio + 1, 1, 1));
                cmd.Parameters.AddWithValue("@meses", f.Meses.ToArray());
            }
            else
            {
                w.Append(" AND c.fecha_emision >= @inicio AND c.fecha_emision < @fin");
                cmd.Parameters.AddWithValue("@inicio", new DateTime(anio, 1, 1));
                cmd.Parameters.AddWithValue("@fin", new DateTime(anio + 1, 1, 1));
            }
        }

        if (aplicarPestana && PestanasAdminXml.TryGetValue(f.Pestana, out string? pestana))
        {
            w.Append(" AND ").Append(pestana);
        }

        if (FiltrosAdminXml.TryGetValue(f.Filtrar, out string? filtrar))
        {
            w.Append(" AND ").Append(filtrar);
        }

        if (!string.IsNullOrWhiteSpace(f.Busqueda))
        {
            w.Append(@" AND (
                c.uuid ILIKE '%' || @busqueda || '%'
                OR COALESCE(c.serie, '') ILIKE '%' || @busqueda || '%'
                OR COALESCE(c.folio, '') ILIKE '%' || @busqueda || '%'
                OR emisor.rfc ILIKE '%' || @busqueda || '%'
                OR emisor.nombre ILIKE '%' || @busqueda || '%'
                OR receptor.rfc ILIKE '%' || @busqueda || '%'
                OR receptor.nombre ILIKE '%' || @busqueda || '%')");
            cmd.Parameters.AddWithValue("@busqueda", f.Busqueda.Trim());
        }

        return w.ToString();
    }

    private List<int> LeerEnteros(
        string sql,
        Action<NpgsqlCommand> parametros)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(sql, conn);
        parametros(cmd);

        var lista = new List<int>();

        using var dr = cmd.ExecuteReader();

        while (dr.Read())
        {
            lista.Add(dr.GetInt32(0));
        }

        return lista;
    }

    private static string Texto(NpgsqlDataReader dr, string columna)
    {
        int i = dr.GetOrdinal(columna);
        return dr.IsDBNull(i) ? "" : dr.GetValue(i).ToString() ?? "";
    }

    private static decimal Decimal(NpgsqlDataReader dr, string columna)
    {
        int i = dr.GetOrdinal(columna);
        return dr.IsDBNull(i) ? 0 : Convert.ToDecimal(dr.GetValue(i));
    }
}
