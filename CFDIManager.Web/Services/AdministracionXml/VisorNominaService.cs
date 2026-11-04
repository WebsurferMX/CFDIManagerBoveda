using System.Globalization;
using System.Xml.Linq;
using ClosedXML.Excel;
using CFDIParser.Models;
using CFDIParser.Services;
using Microsoft.Extensions.Caching.Memory;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>Un recibo de nómina (Complemento Nómina 1.2) con los importes que usa el visor.</summary>
public sealed class ReciboNomina
{
    public long CfdiId { get; init; }
    public string Uuid { get; init; } = "";
    public DateTime FechaPago { get; init; }

    /// <summary>O (ordinaria) o E (extraordinaria).</summary>
    public string TipoNomina { get; init; } = "";

    public string RfcEmisor { get; init; } = "";
    public string NombreEmisor { get; init; } = "";
    public string Rfc { get; init; } = "";
    public string Nombre { get; init; } = "";
    public string Curp { get; init; } = "";
    public string NumEmpleado { get; init; } = "";
    public string Departamento { get; init; } = "";
    public string Puesto { get; init; } = "";

    /// <summary>Del catálogo c_TipoRegimen: 02 sueldos, 05-11 asimilados, etc.</summary>
    public string TipoRegimen { get; init; } = "";

    public decimal TotalPercepciones { get; init; }
    public decimal TotalOtrosPagos { get; init; }
    public decimal TotalDeducciones { get; init; }
    public decimal Neto { get; init; }

    // Percepciones de sueldos (sin PTU, indemnizaciones ni jubilaciones); en asimilados van aparte.
    public decimal SueldosGravados { get; init; }
    public decimal SueldosExentos { get; init; }
    public decimal AguinaldoExento { get; init; }
    public decimal PrimaVacacionalExenta { get; init; }
    public decimal PrimaDominicalExenta { get; init; }
    public decimal Asimilados { get; init; }
    public decimal Indemnizaciones { get; init; }
    public decimal Jubilaciones { get; init; }
    public decimal PtuGravada { get; init; }
    public decimal PtuExenta { get; init; }

    public decimal IsrRetenido { get; init; }
    public decimal IsrEjercicioAnterior { get; init; }
    public decimal SubsidioCausado { get; init; }
    public decimal SubsidioPagado { get; init; }
    public decimal AjusteSubsidio { get; init; }
    public decimal SaldoFavorCompensado { get; init; }
    public decimal ReintegroIsr { get; init; }

    public bool EsAsimilado => TipoRegimen is "05" or "06" or "07" or "08" or "09" or "10" or "11";
}

/// <summary>Recibos del ejercicio (por fecha de pago) ya leídos de la bóveda.</summary>
public sealed class EjercicioNomina
{
    public int Anio { get; init; }
    public List<ReciboNomina> Recibos { get; init; } = [];

    /// <summary>Recibos cancelados en el SAT que no se consideran.</summary>
    public int Cancelados { get; init; }

    /// <summary>XML que no se pudieron leer (movidos o sin complemento de nómina).</summary>
    public int Ilegibles { get; init; }

    public string RfcRetenedor => Recibos.Select(r => r.RfcEmisor).FirstOrDefault(r => r != "") ?? "";
    public string NombreRetenedor => Recibos.Select(r => r.NombreEmisor).FirstOrDefault(r => r != "") ?? "";
    public int Trabajadores => Recibos.Select(r => r.Rfc).Distinct().Count();
}

/// <summary>Totales de un conjunto de recibos (el ejercicio, un mes o un trabajador).</summary>
public sealed class TotalesNomina(IReadOnlyCollection<ReciboNomina> recibos)
{
    private decimal S(Func<ReciboNomina, decimal> campo) => recibos.Sum(campo);

    public int Comprobantes => recibos.Count;
    public int TrabajadoresSueldos => recibos.Where(r => !r.EsAsimilado && r.SueldosGravados + r.SueldosExentos != 0).Select(r => r.Rfc).Distinct().Count();
    public decimal SueldosGravados => S(r => r.SueldosGravados);
    public decimal SueldosExentos => S(r => r.SueldosExentos);
    public decimal AguinaldoExento => S(r => r.AguinaldoExento);
    public decimal PrimaVacacionalExenta => S(r => r.PrimaVacacionalExenta);
    public decimal PrimaDominicalExenta => S(r => r.PrimaDominicalExenta);
    public decimal OtrosExentos => SueldosExentos - AguinaldoExento - PrimaVacacionalExenta - PrimaDominicalExenta;
    public decimal Asimilados => S(r => r.Asimilados);
    public decimal Indemnizaciones => S(r => r.Indemnizaciones);
    public decimal Jubilaciones => S(r => r.Jubilaciones);
    public decimal PtuGravada => S(r => r.PtuGravada);
    public decimal PtuExenta => S(r => r.PtuExenta);
    public decimal Ptu => PtuGravada + PtuExenta;
    public decimal TotalPagos => SueldosGravados + SueldosExentos + Asimilados + Indemnizaciones + Jubilaciones;
    public decimal SubsidioCausado => S(r => r.SubsidioCausado);
    public decimal AjusteSubsidio => S(r => r.AjusteSubsidio);
    public decimal SubsidioNeto => SubsidioCausado - AjusteSubsidio;
    public decimal SubsidioPagado => S(r => r.SubsidioPagado);
    public decimal IsrRetenido => S(r => r.IsrRetenido);
    public decimal IsrEjercicioAnterior => S(r => r.IsrEjercicioAnterior);
    public decimal SaldoFavorCompensado => S(r => r.SaldoFavorCompensado);
    public decimal ReintegroIsr => S(r => r.ReintegroIsr);
    public decimal TotalPercepciones => S(r => r.TotalPercepciones);
    public decimal Neto => S(r => r.Neto);
}

/// <summary>
/// Visor de nóminas: los recibos de nómina emitidos de la bóveda acumulados por ejercicio y mes según su FechaPago
/// (criterio del visor del SAT), sin cancelados. El ISR enterado se captura por mes y se guarda por empresa.
/// </summary>
public class VisorNominaService(PostgreSqlRepository repo, IMemoryCache cache)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(5);

    public List<int> Anios(long empresaId) => repo.ObtenerAniosNomina(empresaId);

    /// <summary>Recibos con FechaPago en el año (se buscan emitidos de diciembre anterior a marzo siguiente). Se guardan 5 min.</summary>
    public EjercicioNomina Leer(long empresaId, int anio, bool recargar = false)
    {
        string llave = $"visor-nomina:{empresaId}:{anio}";
        if (recargar)
            cache.Remove(llave);

        return cache.GetOrCreate(llave, entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Vigencia;
            var recibos = new List<ReciboNomina>();
            int cancelados = 0, ilegibles = 0;

            foreach (NominaCfdiDto c in repo.ObtenerNominasEmitidas(empresaId, new DateTime(anio - 1, 12, 1), new DateTime(anio + 1, 4, 1)))
            {
                ReciboNomina? r = LeerRecibo(c);
                if (r is null)
                {
                    if (c.FechaEmision.Year == anio) ilegibles++;
                    continue;
                }

                if (r.FechaPago.Year != anio)
                    continue;

                if (c.EstadoSat.StartsWith("cancel", StringComparison.OrdinalIgnoreCase))
                {
                    cancelados++;
                    continue;
                }

                recibos.Add(r);
            }

            return new EjercicioNomina { Anio = anio, Recibos = recibos, Cancelados = cancelados, Ilegibles = ilegibles };
        })!;
    }

    private static ReciboNomina? LeerRecibo(NominaCfdiDto c)
    {
        XElement raiz;
        try
        {
            raiz = XDocument.Load(c.RutaXml).Root!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
        {
            return null;
        }

        static IEnumerable<XElement> Hijos(XElement? e, string nombre) => e?.Elements().Where(x => x.Name.LocalName == nombre) ?? [];
        static XElement? Hijo(XElement? e, string nombre) => Hijos(e, nombre).FirstOrDefault();
        static string A(XElement? e, string nombre) => e?.Attributes().FirstOrDefault(a => a.Name.LocalName == nombre)?.Value.Trim() ?? "";
        static decimal N(XElement? e, string nombre) => decimal.TryParse(A(e, nombre), NumberStyles.Number, Inv, out decimal v) ? v : 0;

        XElement? nomina = raiz.Descendants().FirstOrDefault(e => e.Name.LocalName == "Nomina");
        if (nomina is null || !DateTime.TryParse(A(nomina, "FechaPago"), Inv, DateTimeStyles.None, out DateTime fechaPago))
            return null;

        XElement? emisor = Hijo(raiz, "Emisor");
        XElement? receptor = Hijo(raiz, "Receptor");
        XElement? recNomina = Hijo(nomina, "Receptor");
        string regimen = A(recNomina, "TipoRegimen");
        bool asimilado = regimen is "05" or "06" or "07" or "08" or "09" or "10" or "11";

        // c_TipoPercepcion: 003 PTU; 022, 023 y 025 separación/indemnización; 039 y 044 jubilaciones y pensiones.
        decimal sueldosG = 0, sueldosE = 0, aguinaldo = 0, primaVac = 0, primaDom = 0, ptuG = 0, ptuE = 0, indem = 0, jubil = 0;
        foreach (XElement p in Hijos(Hijo(nomina, "Percepciones"), "Percepcion"))
        {
            string tipo = A(p, "TipoPercepcion");
            decimal g = N(p, "ImporteGravado"), e = N(p, "ImporteExento");
            switch (tipo)
            {
                case "003": ptuG += g; ptuE += e; break;
                case "022" or "023" or "025": indem += g + e; break;
                case "039" or "044": jubil += g + e; break;
                default:
                    sueldosG += g;
                    sueldosE += e;
                    if (tipo == "002") aguinaldo += e;
                    else if (tipo == "021") primaVac += e;
                    else if (tipo == "020") primaDom += e;
                    break;
            }
        }

        // c_TipoDeduccion: 002 ISR, 101 ISR retenido de ejercicio anterior, 071 y 107 ajustes al subsidio.
        var deducciones = Hijos(Hijo(nomina, "Deducciones"), "Deduccion").ToList();
        decimal Deduccion(params string[] tipos) => deducciones.Where(d => tipos.Contains(A(d, "TipoDeduccion"))).Sum(d => N(d, "Importe"));

        // c_TipoOtroPago: 002 subsidio para el empleo, 004 saldo a favor por compensación anual, 001 reintegro de ISR en exceso.
        var otros = Hijos(Hijo(nomina, "OtrosPagos"), "OtroPago").ToList();
        decimal OtroPago(string tipo) => otros.Where(o => A(o, "TipoOtroPago") == tipo).Sum(o => N(o, "Importe"));
        decimal causado = otros.Where(o => A(o, "TipoOtroPago") == "002").Sum(o => N(Hijo(o, "SubsidioAlEmpleo"), "SubsidioCausado"));

        return new ReciboNomina
        {
            CfdiId = c.CfdiId,
            Uuid = c.Uuid,
            FechaPago = fechaPago,
            TipoNomina = A(nomina, "TipoNomina"),
            RfcEmisor = A(emisor, "Rfc").ToUpperInvariant(),
            NombreEmisor = A(emisor, "Nombre"),
            Rfc = A(receptor, "Rfc").ToUpperInvariant(),
            Nombre = A(receptor, "Nombre"),
            Curp = A(recNomina, "Curp"),
            NumEmpleado = A(recNomina, "NumEmpleado"),
            Departamento = A(recNomina, "Departamento"),
            Puesto = A(recNomina, "Puesto"),
            TipoRegimen = regimen,
            TotalPercepciones = N(nomina, "TotalPercepciones"),
            TotalOtrosPagos = N(nomina, "TotalOtrosPagos"),
            TotalDeducciones = N(nomina, "TotalDeducciones"),
            Neto = N(raiz, "Total"),
            SueldosGravados = asimilado ? 0 : sueldosG,
            SueldosExentos = asimilado ? 0 : sueldosE,
            AguinaldoExento = asimilado ? 0 : aguinaldo,
            PrimaVacacionalExenta = asimilado ? 0 : primaVac,
            PrimaDominicalExenta = asimilado ? 0 : primaDom,
            Asimilados = asimilado ? sueldosG + sueldosE : 0,
            Indemnizaciones = indem,
            Jubilaciones = jubil,
            PtuGravada = ptuG,
            PtuExenta = ptuE,
            IsrRetenido = Deduccion("002"),
            IsrEjercicioAnterior = Deduccion("101"),
            AjusteSubsidio = Deduccion("071", "107"),
            SubsidioCausado = causado,
            SubsidioPagado = OtroPago("002"),
            SaldoFavorCompensado = OtroPago("004"),
            ReintegroIsr = OtroPago("001")
        };
    }

    // ---------- ISR enterado (captura manual por mes) ----------

    private static string ClaveEnterado(long empresaId, int anio) => $"isr_enterado:{empresaId}:{anio}";

    /// <summary>ISR enterado por mes (1-12) capturado para la empresa y el ejercicio.</summary>
    public decimal?[] IsrEnterado(long empresaId, int anio)
    {
        var meses = new decimal?[12];
        string valor = repo.ObtenerConfiguracionApp(ClaveEnterado(empresaId, anio)) ?? "";
        foreach (string parte in valor.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] mv = parte.Split('=');
            if (mv.Length == 2 && int.TryParse(mv[0], out int m) && m is >= 1 and <= 12 &&
                decimal.TryParse(mv[1], NumberStyles.Number, Inv, out decimal v))
                meses[m - 1] = v;
        }
        return meses;
    }

    public void GuardarIsrEnterado(long empresaId, int anio, decimal?[] meses) =>
        repo.GuardarConfiguracionApp(ClaveEnterado(empresaId, anio),
            string.Join(";", meses.Select((v, i) => v is decimal d ? $"{i + 1}={d.ToString(Inv)}" : null).Where(s => s is not null)));

    // ---------- Excel ----------

    public byte[] GenerarExcel(EjercicioNomina ej, decimal?[] enterado)
    {
        const string moneda = "\"$\"#,##0.00;\\(\"$\"#,##0.00\\);\\-";
        var mx = CultureInfo.GetCultureInfo("es-MX");
        using var libro = new XLWorkbook();
        libro.Style.Font.FontName = "Segoe UI";

        // Detalle mensual: un renglón por concepto, Total + Ene..Dic.
        var ws = libro.AddWorksheet("Detalle mensual");
        ws.Cell("A1").Value = $"VISOR DE NÓMINA · {ej.RfcRetenedor} {ej.NombreRetenedor} · EJERCICIO {ej.Anio}";
        ws.Range("A1:N1").Merge().Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#0B2E4F"));
        ws.Cell("A2").Value = $"Acumulado por fecha de pago · {ej.Recibos.Count} recibos vigentes · {ej.Trabajadores} trabajadores" +
                              (ej.Cancelados > 0 ? $" · {ej.Cancelados} cancelados no considerados" : "");
        ws.Cell("A2").Style.Font.SetFontSize(9).Font.SetFontColor(XLColor.Gray);

        ws.Cell(4, 1).Value = "CONCEPTO";
        ws.Cell(4, 2).Value = "TOTAL";
        for (int m = 1; m <= 12; m++) ws.Cell(4, m + 2).Value = mx.DateTimeFormat.GetAbbreviatedMonthName(m).TrimEnd('.').ToUpperInvariant();
        ws.Range(4, 1, 4, 14).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#D9EEF9"));

        TotalesNomina Mes(int m) => new(ej.Recibos.Where(r => r.FechaPago.Month == m).ToList());
        var meses = Enumerable.Range(1, 12).Select(Mes).ToArray();
        var anual = new TotalesNomina(ej.Recibos);
        (string Concepto, Func<TotalesNomina, decimal> Valor, bool Dinero)[] filas =
        [
            ("Comprobantes", t => t.Comprobantes, false),
            ("Trabajadores por sueldos y salarios", t => t.TrabajadoresSueldos, false),
            ("Pagos por sueldos y salarios (gravados)", t => t.SueldosGravados, true),
            ("Pagos exentos de sueldos", t => t.SueldosExentos, true),
            ("   Aguinaldo exento", t => t.AguinaldoExento, true),
            ("   Prima vacacional exenta", t => t.PrimaVacacionalExenta, true),
            ("   Prima dominical exenta", t => t.PrimaDominicalExenta, true),
            ("Pagos de asimilados a salarios", t => t.Asimilados, true),
            ("Indemnizaciones", t => t.Indemnizaciones, true),
            ("Jubilaciones y pensiones", t => t.Jubilaciones, true),
            ("PTU gravada", t => t.PtuGravada, true),
            ("PTU exenta", t => t.PtuExenta, true),
            ("Subsidio para el empleo causado", t => t.SubsidioCausado, true),
            ("(-) Ajustes al subsidio", t => t.AjusteSubsidio, true),
            ("Subsidio pagado en efectivo", t => t.SubsidioPagado, true),
            ("ISR retenido", t => t.IsrRetenido, true),
            ("(+) ISR retenido de ejercicio anterior", t => t.IsrEjercicioAnterior, true),
            ("(-) Saldo a favor por compensación anual", t => t.SaldoFavorCompensado, true),
            ("(-) Reintegro de ISR retenido en exceso", t => t.ReintegroIsr, true),
            ("Neto pagado", t => t.Neto, true),
        ];
        int r = 5;
        foreach (var (concepto, valor, dinero) in filas)
        {
            ws.Cell(r, 1).Value = concepto;
            ws.Cell(r, 2).Value = valor(anual);
            for (int m = 0; m < 12; m++) ws.Cell(r, m + 3).Value = valor(meses[m]);
            if (dinero) ws.Range(r, 2, r, 14).Style.NumberFormat.Format = moneda;
            r++;
        }
        ws.Cell(r, 1).Value = "ISR enterado (captura)";
        ws.Cell(r, 2).Value = enterado.Sum(v => v ?? 0);
        for (int m = 0; m < 12; m++) ws.Cell(r, m + 3).Value = enterado[m] is decimal e ? e : Blank.Value;
        ws.Range(r, 2, r, 14).Style.NumberFormat.Format = moneda;
        r++;
        int filaIsr = 5 + Array.FindIndex(filas, x => x.Concepto == "ISR retenido");
        ws.Cell(r, 1).Value = "Diferencia (retenido − enterado)";
        for (int c = 2; c <= 14; c++)
        {
            string col = ws.Column(c).ColumnLetter();
            ws.Cell(r, c).FormulaA1 = $"{col}{filaIsr}-{col}{r - 1}";
        }
        ws.Range(r, 1, r, 14).Style.Font.SetBold().NumberFormat.Format = moneda;
        ws.Column(1).Width = 42;
        ws.Columns(2, 14).Width = 15;
        ws.SheetView.FreezeRows(4);

        // Trabajadores.
        var wt = libro.AddWorksheet("Trabajadores");
        string[] enc = ["RFC", "NOMBRE", "CURP", "NO. EMPLEADO", "DEPARTAMENTO", "PUESTO", "RECIBOS", "PERCEPCIONES", "ISR RETENIDO", "SUBSIDIO", "NETO PAGADO"];
        for (int i = 0; i < enc.Length; i++) wt.Cell(1, i + 1).Value = enc[i];
        int f = 2;
        foreach (var g in ej.Recibos.GroupBy(x => x.Rfc).OrderBy(g => g.First().Nombre))
        {
            var u = g.OrderByDescending(x => x.FechaPago).First();
            object[] v = [g.Key, u.Nombre, u.Curp, u.NumEmpleado, u.Departamento, u.Puesto, g.Count(),
                g.Sum(x => x.TotalPercepciones), g.Sum(x => x.IsrRetenido), g.Sum(x => x.SubsidioPagado), g.Sum(x => x.Neto)];
            for (int i = 0; i < v.Length; i++) wt.Cell(f, i + 1).Value = XLCellValue.FromObject(v[i], Inv);
            f++;
        }
        var tt = wt.Range(1, 1, Math.Max(2, f - 1), enc.Length).CreateTable("TblTrabajadores");
        tt.Theme = XLTableTheme.TableStyleMedium2;
        tt.ShowTotalsRow = true;
        tt.Field("RFC").TotalsRowLabel = "TOTAL";
        foreach (string c in new[] { "RECIBOS", "PERCEPCIONES", "ISR RETENIDO", "SUBSIDIO", "NETO PAGADO" }) tt.Field(c).TotalsRowFunction = XLTotalsRowFunction.Sum;
        wt.Columns(8, 11).Style.NumberFormat.Format = moneda;
        wt.Columns().AdjustToContents(1, Math.Min(f, 300), 8, 50);
        wt.SheetView.FreezeRows(1);

        // Recibos.
        var wr = libro.AddWorksheet("Recibos");
        string[] encR = ["FECHA PAGO", "TIPO", "RFC", "NOMBRE", "UUID", "SUELDOS GRAVADOS", "SUELDOS EXENTOS", "PTU", "PERCEPCIONES", "OTROS PAGOS", "DEDUCCIONES", "ISR", "SUBSIDIO", "NETO"];
        for (int i = 0; i < encR.Length; i++) wr.Cell(1, i + 1).Value = encR[i];
        int fr = 2;
        foreach (var x in ej.Recibos.OrderBy(x => x.FechaPago).ThenBy(x => x.Nombre))
        {
            object[] v = [x.FechaPago, x.TipoNomina == "E" ? "Extraordinaria" : "Ordinaria", x.Rfc, x.Nombre, x.Uuid, x.SueldosGravados + x.Asimilados,
                x.SueldosExentos, x.PtuGravada + x.PtuExenta, x.TotalPercepciones, x.TotalOtrosPagos, x.TotalDeducciones, x.IsrRetenido, x.SubsidioPagado, x.Neto];
            for (int i = 0; i < v.Length; i++) wr.Cell(fr, i + 1).Value = XLCellValue.FromObject(v[i], Inv);
            fr++;
        }
        var tr = wr.Range(1, 1, Math.Max(2, fr - 1), encR.Length).CreateTable("TblRecibos");
        tr.Theme = XLTableTheme.TableStyleMedium2;
        tr.ShowTotalsRow = true;
        tr.Field("FECHA PAGO").TotalsRowLabel = "TOTAL";
        foreach (string c in encR[5..]) tr.Field(c).TotalsRowFunction = XLTotalsRowFunction.Sum;
        wr.Column(1).Style.NumberFormat.Format = "dd/mm/yyyy";
        wr.Columns(6, 14).Style.NumberFormat.Format = moneda;
        wr.Columns().AdjustToContents(1, Math.Min(fr, 300), 8, 50);
        wr.SheetView.FreezeRows(1);

        ws.SetTabActive();
        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return memoria.ToArray();
    }
}
