namespace CFDIParser.Models;

/// <summary>Filtro de la pantalla Administración de XML.</summary>
public class AdminXmlFiltroDto
{
    public long EmpresaId { get; set; }

    /// <summary>EMITIDO o RECIBIDO.</summary>
    public string Clasificacion { get; set; } = "EMITIDO";

    public int? Anio { get; set; }

    public List<int> Meses { get; set; } = new();

    /// <summary>Días del mes (solo con un mes elegido y sin acumular); vacío = todo el mes.</summary>
    public List<int> Dias { get; set; } = new();

    /// <summary>Con un año y meses elegidos: de enero al último mes elegido.</summary>
    public bool Acumular { get; set; }

    /// <summary>facturas (I, E, T), pagos (P), nomina (N), nomina11, nomina12 o vacío para todos.</summary>
    public string Pestana { get; set; } = "";

    public string Busqueda { get; set; } = "";

    /// <summary>vigentes, cancelados, sinvalidar, verificados, noverificados, duplicados, ppd o vacío.</summary>
    public string Filtrar { get; set; } = "";

    /// <summary>Si trae elementos, sustituye a los demás filtros (renglones marcados).</summary>
    public List<long> Ids { get; set; } = new();

    /// <summary>Clave de la columna por la que se ordena (la de la pantalla); vacío = fecha de emisión descendente.</summary>
    public string Orden { get; set; } = "";

    public bool Descendente { get; set; }
}

public class AdminXmlFilaDto
{
    public long CfdiId { get; set; }
    public string Uuid { get; set; } = "";
    public string Clasificacion { get; set; } = "";
    public string TipoCfdi { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime FechaEmision { get; set; }
    public DateTime? FechaTimbrado { get; set; }
    public string Serie { get; set; } = "";
    public string Folio { get; set; } = "";
    public string RfcEmisor { get; set; } = "";
    public string NombreEmisor { get; set; } = "";
    public string RfcReceptor { get; set; } = "";
    public string NombreReceptor { get; set; } = "";
    public string UsoCfdi { get; set; } = "";
    public string MetodoPago { get; set; } = "";
    public string FormaPago { get; set; } = "";
    public string Moneda { get; set; } = "";
    public decimal? TipoCambio { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Iva { get; set; }
    public decimal Ieps { get; set; }
    public decimal RetIva { get; set; }
    public decimal RetIsr { get; set; }
    public decimal Total { get; set; }
    public decimal? MontoTotalPagos { get; set; }
    public string EstadoSat { get; set; } = "";
    public bool Verificado { get; set; }
    public string RutaXml { get; set; } = "";
}

public class AdminXmlTotalDto
{
    public int Cantidad { get; set; }
    public decimal Total { get; set; }
}

public class AdminXmlTotalesDto
{
    public AdminXmlTotalDto Facturas { get; set; } = new();
    public AdminXmlTotalDto NotasCredito { get; set; } = new();
    public AdminXmlTotalDto Traslados { get; set; } = new();
    public AdminXmlTotalDto Pagos { get; set; } = new();
    public AdminXmlTotalDto Nominas { get; set; } = new();

    /// <summary>Nómina 1.1 (CFDI 3.2) y 1.2 (CFDI 3.3 y 4.0).</summary>
    public AdminXmlTotalDto Nominas11 { get; set; } = new();
    public AdminXmlTotalDto Nominas12 { get; set; } = new();

    public int Archivos { get; set; }
    public decimal Vigente { get; set; }
    public decimal Cancelado { get; set; }

    /// <summary>Renglones que cumplen el filtro incluyendo la pestaña.</summary>
    public int EnPestana { get; set; }
}

public class AdminXmlAgrupacionDto
{
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
}

public class AdminXmlSerieDto
{
    public string Serie { get; set; } = "";
    public string Tipos { get; set; } = "";
    public long Desde { get; set; }
    public long Hasta { get; set; }
    public int Cantidad { get; set; }
    public List<long> Faltantes { get; set; } = new();
    public List<long> Repetidos { get; set; } = new();
}
