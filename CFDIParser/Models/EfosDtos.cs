namespace CFDIParser.Models;

/// <summary>Un contribuyente del Listado completo del artículo 69-B del CFF.</summary>
public class EfosRegistroDto
{
    public string Rfc { get; set; } = "";

    public string RazonSocial { get; set; } = "";

    /// <summary>PRESUNTO, DEFINITIVO, DESVIRTUADO o FAVORABLE.</summary>
    public string Situacion { get; set; } = "";

    public string OficioPresuncion { get; set; } = "";

    public DateTime? PublicacionPresuntoSat { get; set; }

    public DateTime? PublicacionPresuntoDof { get; set; }

    public DateTime? PublicacionDesvirtuadoDof { get; set; }

    public string OficioDefinitivo { get; set; } = "";

    public DateTime? PublicacionDefinitivoSat { get; set; }

    public DateTime? PublicacionDefinitivoDof { get; set; }

    public DateTime? PublicacionFavorableDof { get; set; }

    /// <summary>
    /// Procedimientos del RFC en el listado. Se guarda un registro por RFC (efos_sat tiene RFC único)
    /// con la situación y fechas de su procedimiento más grave.
    /// </summary>
    public int Procedimientos { get; set; } = 1;
}

/// <summary>Estado de la lista cargada: fecha de corte del SAT y conteo por situación.</summary>
public class EfosListadoEstadoDto
{
    public DateTime? FechaCorte { get; set; }

    public DateTime? FechaCarga { get; set; }

    public string Origen { get; set; } = "";

    public Dictionary<string, int> PorSituacion { get; set; } = new();

    public int Total => PorSituacion.Values.Sum();
}

public class ResultadoImportacionEfosDto
{
    public int RegistrosLeidos { get; set; }

    /// <summary>RFC guardados (uno por RFC, con su procedimiento más grave).</summary>
    public int Insertados { get; set; }

    /// <summary>Procedimientos leídos del listado (un RFC puede tener varios).</summary>
    public int Procedimientos { get; set; }

    /// <summary>Renglones con RFC suprimido por orden judicial (XXXXXXXXXXXX).</summary>
    public int Suprimidos { get; set; }

    public int Errores { get; set; }

    public DateTime? FechaCorte { get; set; }

    /// <summary>RFC por situación efectiva (la del procedimiento más grave).</summary>
    public Dictionary<string, int> PorSituacion { get; set; } = new();
}

/// <summary>
/// Coincidencia de la empresa con un RFC de la lista 69-B, separada por
/// clasificación: RECIBIDO (el RFC listado es el emisor, riesgo EDOS) o
/// EMITIDO (el RFC listado es el cliente, informativo).
/// </summary>
public class EfosCoincidenciaDto
{
    public string Rfc { get; set; } = "";

    public string Nombre { get; set; } = "";

    public string Situacion { get; set; } = "";

    public string Clasificacion { get; set; } = "";

    public DateTime? PublicacionPresuntoDof { get; set; }

    public DateTime? PublicacionDefinitivoDof { get; set; }

    /// <summary>Publicación en el portal del SAT; se usa si aún no sale en el DOF.</summary>
    public DateTime? PublicacionDefinitivoSat { get; set; }

    public DateTime? PublicacionDesvirtuadoDof { get; set; }

    public DateTime? PublicacionFavorableDof { get; set; }

    /// <summary>Procedimientos del RFC en la lista (puede tener más de uno).</summary>
    public int Procedimientos { get; set; }

    public int Cfdi { get; set; }

    public int Cancelados { get; set; }

    /// <summary>Facturas menos notas de crédito vigentes (no canceladas).</summary>
    public decimal ImporteNeto { get; set; }

    /// <summary>Subtotal menos descuento de facturas menos notas de crédito: base de la deducción.</summary>
    public decimal BaseNeta { get; set; }

    /// <summary>IVA trasladado de facturas menos notas de crédito: IVA acreditable en riesgo.</summary>
    public decimal IvaNeto { get; set; }

    public DateTime PrimerCfdi { get; set; }

    public DateTime UltimoCfdi { get; set; }

    /// <summary>CFDI con fecha posterior a la publicación definitiva en el DOF.</summary>
    public int PosterioresAlDefinitivo { get; set; }
}

public class EfosCfdiDto
{
    public long CfdiId { get; set; }

    public string Clasificacion { get; set; } = "";

    public DateTime FechaEmision { get; set; }

    public string UUID { get; set; } = string.Empty;

    public string TipoCfdi { get; set; } = string.Empty;

    public string Serie { get; set; } = "";

    public string Folio { get; set; } = "";

    public decimal Base { get; set; }

    public decimal Iva { get; set; }

    public decimal Total { get; set; }

    public string EstadoSat { get; set; } = string.Empty;

    public string RutaXml { get; set; } = string.Empty;
}
