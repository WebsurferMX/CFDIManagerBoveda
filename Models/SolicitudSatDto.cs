namespace CFDIManager.Web.Models;

/// <summary>
/// Solicitud de descarga masiva presentada al SAT. Se guarda en
/// Storage/SatDescargas/{empresa}/solicitudes.json porque el SAT la procesa
/// en minutos u horas y hay que poder retomarla después.
/// </summary>
public class SolicitudSatDto
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString("N");

    public long EmpresaId { get; set; }

    public string Rfc { get; set; } =
        "";

    public string IdSolicitudSat { get; set; } =
        "";

    public DateTime Creada { get; set; }

    public DateTime Actualizada { get; set; }

    public DateTime FechaInicial { get; set; }

    public DateTime FechaFinal { get; set; }

    public bool Recibidos { get; set; }

    public bool Metadatos { get; set; }

    /// <summary>La creó la descarga automática; sus periodos ya no se vuelven a pedir.</summary>
    public bool Automatica { get; set; }

    public string TipoComprobante { get; set; } =
        "";

    public string RfcContraparte { get; set; } =
        "";

    /// <summary>Solo en solicitudes de un CFDI por UUID.</summary>
    public string Uuid { get; set; } =
        "";

    /// <summary>
    /// Aceptada, En proceso, Terminada, Error, Rechazada, Vencida
    /// (estados del SAT), Descargada cuando ya se bajaron todos los paquetes
    /// o Cancelada cuando el usuario la detuvo.
    /// </summary>
    public string Estado { get; set; } =
        "";

    public string Mensaje { get; set; } =
        "";

    public int NumeroCfdis { get; set; }

    public List<string> Paquetes { get; set; } =
        new();

    public List<string> PaquetesDescargados { get; set; } =
        new();

    public int Importados { get; set; }

    public int Actualizados { get; set; }

    public int Omitidos { get; set; }

    public int Errores { get; set; }

    public bool Pendiente =>
        Estado is "Aceptada" or "En proceso";

    /// <summary>Aún no termina de descargarse ni fue rechazada por el SAT.</summary>
    public bool Cancelable =>
        Estado is "Aceptada" or "En proceso" or "Terminada";

    public bool ListaParaDescargar =>
        Estado == "Terminada" &&
        PaquetesDescargados.Count < Paquetes.Count;

    public string Descripcion =>
        Uuid != ""
            ? $"XML del UUID {Uuid}"
            : $"{(Recibidos ? "Recibidos" : "Emitidos")} · {(Metadatos ? "Metadatos" : "XML")}" +
              (TipoComprobante != "" ? $" · {TipoComprobante}" : "") +
              (RfcContraparte != "" ? $" · {RfcContraparte}" : "") +
              (Automatica ? " · Automática" : "");
}
