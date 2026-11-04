namespace CFDIParser.Models;

/// <summary>
/// Datos para firmarse a SINUBE y parámetros de descarga/nombre de XML de una empresa
/// (equivalen a Parámetros → Opciones de HerramientasSinube).
/// </summary>
public class SinubeConfiguracionDto
{
    public long EmpresaId { get; set; }

    /// <summary>Clave de la empresa en SINUBE; normalmente el RFC.</summary>
    public string Empresa { get; set; } = "";

    public string Sucursal { get; set; } = "Matriz";

    public string Usuario { get; set; } = "";

    /// <summary>Contraseña para importaciones SINUBE (cifrada en la base).</summary>
    public string? Password { get; set; }

    /// <summary>PRODUCCION, PRUEBAS_FACTURANUBE o PRUEBAS_SINUBEFB.</summary>
    public string Ambiente { get; set; } = SinubeAmbientes.Produccion;

    // Opciones de "Importar facturas de clientes"
    public string ModoProducto { get; set; } = SinubeModosProducto.NoIdentificacion;

    public bool SerieFolioDeFolio { get; set; }

    public bool AgruparCodigoSat { get; set; }

    public bool UsarProductoClientes { get; set; }

    public bool ValidarIeps { get; set; } = true;

    /// <summary>
    /// Series, separadas por coma y en orden, con las que se suben los comprobantes de clientes (facturas, notas y
    /// pagos) cuyo serie-folio ya ocupa otro en SINUBE (SINUBE valida el folio, no el UUID). Una serie fija
    /// ("COMFR") o un sufijo de la serie original ("+R": NC- → NC-R). Se usa la primera con el folio libre.
    /// Vacía: no se cambia. (El nombre de la columna viene de cuando solo aplicaba a pagos.)
    /// </summary>
    public string SerieAlternaPagos { get; set; } = "";

    /// <summary>
    /// Igual que <see cref="SerieAlternaPagos"/> pero solo para XML Azulejeras, sobre la serie ya convertida
    /// (72A → 72AA; con "+A,+AA": 72AAA y luego 72AAAA). No afecta la Importación a SINUBE normal.
    /// </summary>
    public string SerieAlternaAzulejeras { get; set; } = "";

    /// <summary><see cref="SerieAlternaPagos"/> como lista, en mayúsculas y sin repetidas.</summary>
    public List<string> SeriesAlternasPagos => Lista(SerieAlternaPagos);

    /// <summary>Series separadas por coma como lista, en mayúsculas y sin repetidas.</summary>
    public static List<string> Lista(string series) =>
        (series ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .ToList();

    /// <summary>
    /// Las series alternas ya resueltas para un comprobante de esa serie: "+R" se agrega a la serie y en el sufijo
    /// "*" es la última letra de la serie ("+*": 72AA → 72AAA, 76EE → 76EEE; "+**": 72AAAA).
    /// </summary>
    public List<string> SeriesAlternasPara(string? serie)
    {
        string original = (serie ?? "").Trim().ToUpperInvariant();
        string ultima = original.Length > 0 ? original[^1].ToString() : "";

        return SeriesAlternasPagos
            .Select(s => s.StartsWith('+') ? original + s[1..].Replace("*", ultima) : s)
            .Where(s => s != "" && s != original)
            .Distinct()
            .ToList();
    }

    // Parámetros de descarga / nombre de XML (pestaña "Descarga XMLs del SAT")
    public bool IncluirUuid { get; set; }

    public bool IncluirTipo { get; set; }

    /// <summary>RFC o RAZON_SOCIAL del emisor/receptor en el nombre del archivo.</summary>
    public string EmisorReceptor { get; set; } = "RFC";

    /// <summary>1: {RFC}-Fecha-serie-folio-UUID, 2: Fecha-{RFC}-serie-folio-uuid, 3: Fecha-serie-folio-{RFC}-uuid.</summary>
    public int CriterioNombre { get; set; } = 1;

    // Último foliador elegido para "Importar facturas de clientes sin folio"
    public string FoliadorLlave { get; set; } = "";

    public string FoliadorNombre { get; set; } = "";

    public DateTime? UltimaPrueba { get; set; }

    public string? ResultadoPrueba { get; set; }
}

public static class SinubeAmbientes
{
    public const string Produccion = "PRODUCCION";
    public const string PruebasFacturanube = "PRUEBAS_FACTURANUBE";
    public const string PruebasSinubeFb = "PRUEBAS_SINUBEFB";
}

public static class SinubeModosProducto
{
    public const string NoIdentificacion = "NOIDENTIFICACION";
    public const string OmitirNoIdentificacion = "OMITIR";
    public const string CodigoSat6 = "CODSAT6";
    public const string CodigoSatComoViene = "CODSAT";
}

/// <summary>Foliador (serie de facturación) de SINUBE para importar facturas sin folio.</summary>
public class SinubeFoliadorDto
{
    /// <summary>Lo que muestra SINUBE (atributo folio).</summary>
    public string Nombre { get; set; } = "";

    /// <summary>Se envía como llaveSerFol.</summary>
    public string Llave { get; set; } = "";
}

/// <summary>Importación de facturas de clientes sin folio (frmImportarFacturasDeXmlsfV2).</summary>
public class SinubeSinFolioDto
{
    public string LlaveFoliador { get; set; } = "";

    /// <summary>
    /// Si la factura ya trae Folio: true la importa con el foliador (sustituye serie/folio),
    /// false la omite ("Se canceló porque tiene SERIE/FOLIO").
    /// </summary>
    public bool SustituirFolio { get; set; }
}

/// <summary>Resultado de enviar un XML a SINUBE; se guarda en sinube_envios.</summary>
public class SinubeEnvioDto
{
    public long EmpresaId { get; set; }

    /// <summary>C = factura de cliente (emitida), P = factura de proveedor (recibida).</summary>
    public string Tipo { get; set; } = "C";

    public string Uuid { get; set; } = "";

    public string Archivo { get; set; } = "";

    public string Serie { get; set; } = "";

    public string Folio { get; set; } = "";

    public string Rfc { get; set; } = "";

    public string Nombre { get; set; } = "";

    public string TipoComprobante { get; set; } = "";

    public DateTime? FechaCfdi { get; set; }

    /// <summary>OK, EXISTE, ERROR, INVALIDO, OMITIDO o IEPS.</summary>
    public string Estatus { get; set; } = "";

    public string Mensaje { get; set; } = "";

    public DateTime FechaEnvio { get; set; }
}
