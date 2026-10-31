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
