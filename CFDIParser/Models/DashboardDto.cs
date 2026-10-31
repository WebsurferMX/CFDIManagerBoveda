namespace CFDIParser.Models;


public class DashboardDto
{
    // ========================================
    // EMITIDOS
    // ========================================


    public long CfdiEmitidos { get; set; }

    public long IngresosEmitidos { get; set; }

    public long EgresosEmitidos { get; set; }

    public long NominasEmitidas { get; set; }

    public long PagosEmitidos { get; set; }

    public decimal TotalPagosEmitidos { get; set; }

    public long TrasladosEmitidos { get; set; }

    public decimal TotalIngresosEmitidos { get; set; }

    public decimal TotalEgresosEmitidos { get; set; }

    public decimal TotalNominasEmitidas { get; set; }

    public decimal FacturacionNeta { get; set; }

    // ========================================
    // RECIBIDOS
    // ========================================

    public long CfdiRecibidos { get; set; }

    public long IngresosRecibidos { get; set; }

    public long EgresosRecibidos { get; set; }

    public long NominasRecibidas { get; set; }

    public long PagosRecibidos { get; set; }

    public decimal TotalPagosRecibidos { get; set; }

    public long TrasladosRecibidos { get; set; }

    public decimal TotalIngresosRecibidos { get; set; }

    public decimal TotalEgresosRecibidos { get; set; }

    public decimal TotalNominasRecibidas { get; set; }

    public decimal ComprasNetas { get; set; }
}