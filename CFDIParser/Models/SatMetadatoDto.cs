namespace CFDIParser.Models;

/// <summary>Un CFDI según los metadatos de la descarga masiva del SAT (existencia y estatus oficiales).</summary>
public class SatMetadatoDto
{
    public string Uuid { get; set; } = "";

    public string RfcEmisor { get; set; } = "";

    public string NombreEmisor { get; set; } = "";

    public string RfcReceptor { get; set; } = "";

    public string NombreReceptor { get; set; } = "";

    public DateTime FechaEmision { get; set; }

    public decimal Monto { get; set; }

    /// <summary>EfectoComprobante: I, E, T, N o P.</summary>
    public string Efecto { get; set; } = "";

    /// <summary>Vigente o Cancelado.</summary>
    public string Estatus { get; set; } = "";

    public DateTime? FechaCancelacion { get; set; }
}

/// <summary>Resultado del cierre de mes de una empresa (metadatos + descarga de XML faltantes).</summary>
public class SatCierreDto
{
    public long EmpresaId { get; set; }

    public int Periodo { get; set; }

    public DateTime Inicio { get; set; }

    public DateTime? Fin { get; set; }

    /// <summary>En curso, Completo, Con pendientes o Error.</summary>
    public string Estado { get; set; } = "";

    public int MetadatosEmitidos { get; set; }

    public int MetadatosRecibidos { get; set; }

    public int XmlImportados { get; set; }

    /// <summary>Recibidos cancelados sin XML: el SAT no los entrega (5012).</summary>
    public int CanceladosSinXml { get; set; }

    /// <summary>Vigentes (o emitidos cancelados) que siguen sin XML después del cierre.</summary>
    public int OtrosSinXml { get; set; }

    public string Detalle { get; set; } = "";

    public bool Automatico { get; set; }
}

/// <summary>Mes cubierto por metadatos del SAT (de qué solicitud y cuándo).</summary>
public class SatMetadatosPeriodoDto
{
    public long EmpresaId { get; set; }

    public bool Recibidos { get; set; }

    public int Periodo { get; set; }

    public string SolicitudId { get; set; } = "";

    public int Registros { get; set; }

    /// <summary>Hasta qué momento cubre la solicitud (meses en curso quedan parciales).</summary>
    public DateTime CubreHasta { get; set; }

    public DateTime Consultado { get; set; }
}
