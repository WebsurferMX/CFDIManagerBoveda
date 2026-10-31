using ClosedXML.Excel;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services;

public sealed class ExportacionExcelService
{
    private const string TipoContenido =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly PostgreSqlRepository _repo;

    public ExportacionExcelService(PostgreSqlRepository repo)
    {
        _repo = repo;
    }

    public ExportacionExcelResultado GenerarReporteEmitidos(
        EmpresaDto empresa,
        DateTime fechaInicio,
        DateTime fechaFin,
        string tipoCfdi = "")
    {
        return GenerarReporte(
            empresa,
            fechaInicio,
            fechaFin,
            "EMITIDO",
            tipoCfdi);
    }

    public ExportacionExcelResultado GenerarReporteRecibidos(
        EmpresaDto empresa,
        DateTime fechaInicio,
        DateTime fechaFin,
        string tipoCfdi = "")
    {
        return GenerarReporte(
            empresa,
            fechaInicio,
            fechaFin,
            "RECIBIDO",
            tipoCfdi);
    }

    public List<ExportacionExcelResultado> GenerarReportes(
        EmpresaDto empresa,
        DateTime fechaInicio,
        DateTime fechaFin,
        string alcance,
        string tipoCfdi = "")
    {
        string alcanceNormalizado =
            (alcance ?? "").Trim().ToUpperInvariant();

        var resultados =
            new List<ExportacionExcelResultado>();

        if (alcanceNormalizado is "INGRESOS" or "EMITIDOS" or "AMBOS")
        {
            ExportacionExcelResultado? emitidos =
                IntentarGenerar(
                    empresa,
                    fechaInicio,
                    fechaFin,
                    "EMITIDO",
                    tipoCfdi);

            if (emitidos is not null)
            {
                resultados.Add(emitidos);
            }
        }

        if (alcanceNormalizado is "EGRESOS" or "RECIBIDOS" or "AMBOS")
        {
            ExportacionExcelResultado? recibidos =
                IntentarGenerar(
                    empresa,
                    fechaInicio,
                    fechaFin,
                    "RECIBIDO",
                    tipoCfdi);

            if (recibidos is not null)
            {
                resultados.Add(recibidos);
            }
        }

        if (resultados.Count == 0)
        {
            throw new InvalidOperationException(
                "No se encontraron CFDI para los filtros seleccionados.");
        }

        return resultados;
    }

    private ExportacionExcelResultado? IntentarGenerar(
        EmpresaDto empresa,
        DateTime fechaInicio,
        DateTime fechaFin,
        string clasificacion,
        string tipoCfdi)
    {
        List<CfdiExportacionDto> registros =
            ObtenerRegistros(
                empresa.EmpresaId,
                fechaInicio,
                fechaFin,
                clasificacion,
                tipoCfdi);

        return registros.Count == 0
            ? null
            : CrearLibro(
                empresa,
                fechaInicio,
                fechaFin,
                clasificacion,
                tipoCfdi,
                registros);
    }

    private ExportacionExcelResultado GenerarReporte(
        EmpresaDto empresa,
        DateTime fechaInicio,
        DateTime fechaFin,
        string clasificacion,
        string tipoCfdi)
    {
        Validar(empresa, fechaInicio, fechaFin);

        List<CfdiExportacionDto> registros =
            ObtenerRegistros(
                empresa.EmpresaId,
                fechaInicio,
                fechaFin,
                clasificacion,
                tipoCfdi);

        if (registros.Count == 0)
        {
            throw new InvalidOperationException(
                $"No se encontraron CFDI {clasificacion.ToLowerInvariant()}s " +
                "para los filtros seleccionados.");
        }

        return CrearLibro(
            empresa,
            fechaInicio,
            fechaFin,
            clasificacion,
            tipoCfdi,
            registros);
    }

    private List<CfdiExportacionDto> ObtenerRegistros(
        long empresaId,
        DateTime fechaInicio,
        DateTime fechaFin,
        string clasificacion,
        string tipoCfdi)
    {
        string tipoNormalizado =
            (tipoCfdi ?? "").Trim().ToUpperInvariant();

        var filtro = new CfdiFiltroDto
        {
            EmpresaId = empresaId,
            FechaInicio = fechaInicio.Date,
            FechaFin = fechaFin.Date,
            Clasificacion = clasificacion,
            TipoCfdi = tipoNormalizado,
            Busqueda = "",
            Pagina = 1,
            RegistrosPorPagina = 100,
            OrdenarPor = "fecha_emision",
            OrdenDescendente = false
        };

        return _repo.ObtenerCfdiParaExportacionDetallada(filtro);
    }

    private ExportacionExcelResultado CrearLibro(
        EmpresaDto empresa,
        DateTime fechaInicio,
        DateTime fechaFin,
        string clasificacion,
        string tipoCfdi,
        List<CfdiExportacionDto> registros)
    {
        string nombreReporte =
            clasificacion == "EMITIDO" ? "Emitidos" : "Recibidos";

        using var libro = new XLWorkbook();

        var filtroConceptos = new CfdiFiltroDto
        {
            EmpresaId = empresa.EmpresaId,
            FechaInicio = fechaInicio.Date,
            FechaFin = fechaFin.Date,
            Clasificacion = clasificacion,
            TipoCfdi = (tipoCfdi ?? "").Trim().ToUpperInvariant(),
            Busqueda = "",
            Pagina = 1,
            RegistrosPorPagina = 100,
            OrdenarPor = "fecha_emision",
            OrdenDescendente = false
        };

        List<ConceptoExportacionDto> conceptos =
            _repo.ObtenerConceptosParaExportacion(filtroConceptos);

        CrearResumen(
            libro,
            empresa,
            fechaInicio,
            fechaFin,
            nombreReporte,
            registros);

        // Orden de los comprobantes en toda la app: facturas, nómina, notas de crédito, traslados y pagos.
        CrearHojaTipo(libro, registros, "I", "Ingresos", "#2563EB");
        CrearHojaTipo(libro, registros, "N", "Nomina", "#7C3AED");
        CrearHojaTipo(libro, registros, "E", "Egresos", "#DC2626");
        CrearHojaTipo(libro, registros, "T", "Traslados", "#475569");
        CrearHojaTipo(libro, registros, "P", "Pagos", "#0891B2");
        CrearHojaConceptos(libro, conceptos);

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);

        return new ExportacionExcelResultado
        {
            NombreArchivo =
                $"CFDI_{nombreReporte}_{empresa.Rfc}_" +
                $"{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.xlsx",
            TipoContenido = TipoContenido,
            Contenido = memoria.ToArray(),
            Clasificacion = clasificacion,
            NombreReporte = nombreReporte,
            TotalRegistros = registros.Count
        };
    }

    private static void CrearResumen(
        XLWorkbook libro,
        EmpresaDto empresa,
        DateTime fechaInicio,
        DateTime fechaFin,
        string nombreReporte,
        List<CfdiExportacionDto> registros)
    {
        IXLWorksheet hoja = libro.Worksheets.Add("Resumen");
        hoja.ShowGridLines = false;

        hoja.Range("A1:D1").Merge();
        hoja.Cell("A1").Value =
            $"CFDI {nombreReporte.ToUpperInvariant()}";
        hoja.Cell("A1").Style.Font.Bold = true;
        hoja.Cell("A1").Style.Font.FontSize = 17;
        hoja.Cell("A1").Style.Font.FontColor = XLColor.White;
        hoja.Cell("A1").Style.Fill.BackgroundColor =
            XLColor.FromHtml("#1D4ED8");
        hoja.Cell("A1").Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;

        hoja.Cell("A3").Value = "Empresa";
        hoja.Cell("B3").Value = empresa.NombreParaMostrar;
        hoja.Cell("A4").Value = "RFC";
        hoja.Cell("B4").Value = empresa.Rfc;
        hoja.Cell("A5").Value = "Periodo";
        hoja.Cell("B5").Value =
            $"{fechaInicio:yyyy-MM-dd} al {fechaFin:yyyy-MM-dd}";
        hoja.Cell("A6").Value = "Registros";
        hoja.Cell("B6").Value = registros.Count;
        hoja.Cell("A8").Value = "Tipo";
        hoja.Cell("B8").Value = "Cantidad";
        hoja.Cell("C8").Value = "Subtotal";
        hoja.Cell("D8").Value = "Total";

        string[] tipos = { "I", "N", "E", "T", "P" };
        string[] nombres = { "Ingresos", "Nómina", "Egresos", "Traslados", "Pagos" };

        for (int i = 0; i < tipos.Length; i++)
        {
            int fila = 9 + i;
            List<CfdiExportacionDto> grupo = registros
                .Where(x => string.Equals(
                    x.Tipo,
                    tipos[i],
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            hoja.Cell(fila, 1).Value = nombres[i];
            hoja.Cell(fila, 2).Value = grupo.Count;
            hoja.Cell(fila, 3).Value = grupo.Sum(x => x.SubTotal);
            hoja.Cell(fila, 4).Value = grupo.Sum(x => x.Total);
        }

        hoja.Range("A8:D13").CreateTable("ResumenCfdi");
        hoja.Range("C9:D13").Style.NumberFormat.Format = "$#,##0.00";
        hoja.Columns("A:D").AdjustToContents();
    }

    private static void CrearHojaTipo(
        XLWorkbook libro,
        IEnumerable<CfdiExportacionDto> registros,
        string tipo,
        string nombreHoja,
        string color)
    {
        List<CfdiExportacionDto> grupo = registros
            .Where(x => string.Equals(
                x.Tipo,
                tipo,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.FechaEmision)
            .ThenBy(x => x.UUID)
            .ToList();

        if (grupo.Count == 0)
        {
            return;
        }

        CrearDetalle(libro, nombreHoja, grupo, color);
    }

    private static void CrearDetalle(
        XLWorkbook libro,
        string nombreHoja,
        List<CfdiExportacionDto> registros,
        string color)
    {
        string[] encabezados =
        {
            "Verificado ó Asoc.", "Estado SAT", "Tipo", "Fecha Emision",
            "Fecha Timbrado", "Serie", "Folio", "UUID", "UUID Relacion",
            "RFC Emisor", "Nombre Emisor", "LugarDeExpedicion", "RFC Receptor",
            "Nombre Receptor", "ResidenciaFiscal", "NumRegIdTrib", "UsoCFDI",
            "SubTotal", "Descuento", "Total IEPS", "IVA 16%", "Retenido IVA",
            "Retenido ISR", "ISH", "Total", "TotalOriginal", "Total Trasladados",
            "Total Retenidos", "Total LocalTrasladado", "Total LocalRetenido",
            "Complemento", "Moneda", "Tipo De Cambio", "FormaDePago",
            "Metodo de Pago", "NumCtaPago", "Condicion de Pago", "Conceptos",
            "Combustible", "IEPS 3%", "IEPS 6%", "IEPS 7%", "IEPS 8%",
            "IEPS 9%", "IEPS 26.5%", "IEPS 30%", "IEPS 53%", "IEPS 160%",
            "Archivo XML", "Direccion Emisor", "Localidad Emisor",
            "Direccion Receptor", "Localidad Receptor", "IVA 8%", "IEPS 30.4%",
            "IVA Ret 6%", "RegimenFiscalReceptor", "DomicilioFiscalReceptor",
            "Tipo Relacion", "Monto Total Pagos"
        };

        IXLWorksheet hoja = libro.Worksheets.Add(nombreHoja);
        hoja.ShowGridLines = false;

        hoja.Range(1, 1, 1, encabezados.Length).Merge();
        hoja.Cell(1, 1).Value = $"CFDI {nombreHoja.ToUpperInvariant()}";
        hoja.Cell(1, 1).Style.Font.Bold = true;
        hoja.Cell(1, 1).Style.Font.FontSize = 15;
        hoja.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        hoja.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(color);
        hoja.Cell(1, 1).Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;

        const int filaEncabezado = 3;
        for (int i = 0; i < encabezados.Length; i++)
        {
            hoja.Cell(filaEncabezado, i + 1).Value = encabezados[i];
        }

        int fila = filaEncabezado + 1;
        foreach (CfdiExportacionDto x in registros)
        {
            Texto(hoja.Cell(fila, 1), x.EtiquetaVerificadoAsociado);
            Texto(hoja.Cell(fila, 2), x.EstadoSat);
            Texto(hoja.Cell(fila, 3), x.Tipo);
            Fecha(hoja.Cell(fila, 4), x.FechaEmision);
            Fecha(hoja.Cell(fila, 5), x.FechaTimbrado);
            Texto(hoja.Cell(fila, 6), x.Serie);
            Texto(hoja.Cell(fila, 7), x.Folio);
            Texto(hoja.Cell(fila, 8), x.UUID);
            Texto(hoja.Cell(fila, 9), x.UUIDRelacion);
            Texto(hoja.Cell(fila, 10), x.RfcEmisor);
            Texto(hoja.Cell(fila, 11), x.NombreEmisor);
            Texto(hoja.Cell(fila, 12), x.LugarExpedicion);
            Texto(hoja.Cell(fila, 13), x.RfcReceptor);
            Texto(hoja.Cell(fila, 14), x.NombreReceptor);
            Texto(hoja.Cell(fila, 15), x.ResidenciaFiscal);
            Texto(hoja.Cell(fila, 16), x.NumRegIdTrib);
            Texto(hoja.Cell(fila, 17), x.UsoCfdi);
            Numero(hoja.Cell(fila, 18), x.SubTotal);
            Numero(hoja.Cell(fila, 19), x.Descuento);
            Numero(hoja.Cell(fila, 20), x.TotalIeps);
            Numero(hoja.Cell(fila, 21), x.Iva16);
            Numero(hoja.Cell(fila, 22), x.RetenidoIva);
            Numero(hoja.Cell(fila, 23), x.RetenidoIsr);
            Numero(hoja.Cell(fila, 24), x.Ish);
            Numero(hoja.Cell(fila, 25), x.Total);
            Numero(hoja.Cell(fila, 26), x.TotalOriginal);
            Numero(hoja.Cell(fila, 27), x.TotalTrasladados);
            Numero(hoja.Cell(fila, 28), x.TotalRetenidos);
            Numero(hoja.Cell(fila, 29), x.TotalLocalTrasladado);
            Numero(hoja.Cell(fila, 30), x.TotalLocalRetenido);
            Texto(hoja.Cell(fila, 31), x.Complemento);
            Texto(hoja.Cell(fila, 32), x.Moneda);
            Numero(hoja.Cell(fila, 33), x.TipoCambio, "0.000000");
            Texto(hoja.Cell(fila, 34), x.FormaPago);
            Texto(hoja.Cell(fila, 35), x.MetodoPago);
            Texto(hoja.Cell(fila, 36), x.NumCtaPago);
            Texto(hoja.Cell(fila, 37), x.CondicionPago);
            Texto(hoja.Cell(fila, 38), x.Conceptos);
            Texto(hoja.Cell(fila, 39), x.Combustible);
            Numero(hoja.Cell(fila, 40), x.Ieps3);
            Numero(hoja.Cell(fila, 41), x.Ieps6);
            Numero(hoja.Cell(fila, 42), x.Ieps7);
            Numero(hoja.Cell(fila, 43), x.Ieps8);
            Numero(hoja.Cell(fila, 44), x.Ieps9);
            Numero(hoja.Cell(fila, 45), x.Ieps265);
            Numero(hoja.Cell(fila, 46), x.Ieps30);
            Numero(hoja.Cell(fila, 47), x.Ieps53);
            Numero(hoja.Cell(fila, 48), x.Ieps160);
            Texto(hoja.Cell(fila, 49), x.ArchivoXml);
            Texto(hoja.Cell(fila, 50), x.DireccionEmisor);
            Texto(hoja.Cell(fila, 51), x.LocalidadEmisor);
            Texto(hoja.Cell(fila, 52), x.DireccionReceptor);
            Texto(hoja.Cell(fila, 53), x.LocalidadReceptor);
            Numero(hoja.Cell(fila, 54), x.Iva8);
            Numero(hoja.Cell(fila, 55), x.Ieps304);
            Numero(hoja.Cell(fila, 56), x.IvaRetenido6);
            Texto(hoja.Cell(fila, 57), x.RegimenFiscalReceptor);
            Texto(hoja.Cell(fila, 58), x.DomicilioFiscalReceptor);
            Texto(hoja.Cell(fila, 59), x.TipoRelacion);
            Numero(hoja.Cell(fila, 60), x.MontoTotalPagos);
            fila++;
        }

        int ultimaFila = fila - 1;
        hoja.Range(filaEncabezado, 1, ultimaFila, encabezados.Length)
            .CreateTable($"Tabla{nombreHoja}");

        hoja.SheetView.FreezeRows(filaEncabezado);
        hoja.SheetView.FreezeColumns(8);
        hoja.Columns().AdjustToContents();

        for (int columna = 1; columna <= encabezados.Length; columna++)
        {
            double maximo = columna switch
            {
                8 or 9 => 40,
                11 or 14 => 42,
                38 => 70,
                49 => 65,
                _ => 24
            };

            if (hoja.Column(columna).Width > maximo)
            {
                hoja.Column(columna).Width = maximo;
            }
        }

        hoja.Column(38).Style.Alignment.WrapText = true;
        hoja.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        //hoja.PageSetup.FitToPages(1, 0);//
    }


    private static void CrearHojaConceptos(
        XLWorkbook libro,
        List<ConceptoExportacionDto> conceptos)
    {
        if (conceptos.Count == 0)
        {
            return;
        }

        string[] encabezados =
        {
            "Clasificación", "Tipo CFDI", "Fecha Emisión", "Fecha Timbrado",
            "Serie", "Folio", "UUID", "RFC Emisor", "Nombre Emisor",
            "RFC Receptor", "Nombre Receptor", "ClaveProdServ",
            "NoIdentificacion", "Descripción", "Cantidad", "ClaveUnidad",
            "Unidad", "ValorUnitario", "Importe", "Descuento", "ObjetoImp",
            "CuentaPredial", "IVA 16%", "IVA 8%", "IVA 0%", "IVA Exento",
            "IVA Retenido", "IVA Ret 6%", "ISR Retenido", "IEPS",
            "IEPS 3%", "IEPS 6%", "IEPS 7%", "IEPS 8%", "IEPS 9%",
            "IEPS 26.5%", "IEPS 30%", "IEPS 30.4%", "IEPS 53%",
            "IEPS 160%", "Total Impuestos", "Archivo XML"
        };

        IXLWorksheet hoja = libro.Worksheets.Add("Conceptos");
        hoja.ShowGridLines = false;
        hoja.Range(1, 1, 1, encabezados.Length).Merge();
        hoja.Cell(1, 1).Value = "DETALLE DE CONCEPTOS";
        hoja.Cell(1, 1).Style.Font.Bold = true;
        hoja.Cell(1, 1).Style.Font.FontSize = 16;
        hoja.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        hoja.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0F766E");
        hoja.Cell(1, 1).Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;
        hoja.Row(1).Height = 28;
        hoja.Row(2).Height = 8;

        const int filaEncabezados = 3;
        for (int columna = 0; columna < encabezados.Length; columna++)
        {
            hoja.Cell(filaEncabezados, columna + 1).Value = encabezados[columna];
        }

        hoja.Row(filaEncabezados).Height = 32;
        hoja.Range(filaEncabezados, 1, filaEncabezados, encabezados.Length)
            .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        hoja.Range(filaEncabezados, 1, filaEncabezados, encabezados.Length)
            .Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        hoja.Range(filaEncabezados, 1, filaEncabezados, encabezados.Length)
            .Style.Alignment.WrapText = true;

        int fila = filaEncabezados + 1;
        foreach (ConceptoExportacionDto x in conceptos)
        {
            Texto(hoja.Cell(fila, 1), x.Clasificacion);
            Texto(hoja.Cell(fila, 2), x.TipoCfdi);
            Fecha(hoja.Cell(fila, 3), x.FechaEmision);
            Fecha(hoja.Cell(fila, 4), x.FechaTimbrado);
            Texto(hoja.Cell(fila, 5), x.Serie);
            Texto(hoja.Cell(fila, 6), x.Folio);
            Texto(hoja.Cell(fila, 7), x.UUID);
            Texto(hoja.Cell(fila, 8), x.RfcEmisor);
            Texto(hoja.Cell(fila, 9), x.NombreEmisor);
            Texto(hoja.Cell(fila, 10), x.RfcReceptor);
            Texto(hoja.Cell(fila, 11), x.NombreReceptor);
            Texto(hoja.Cell(fila, 12), x.ClaveProdServ);
            Texto(hoja.Cell(fila, 13), x.NoIdentificacion);
            Texto(hoja.Cell(fila, 14), x.Descripcion);
            Numero(hoja.Cell(fila, 15), x.Cantidad, "#,##0.######");
            Texto(hoja.Cell(fila, 16), x.ClaveUnidad);
            Texto(hoja.Cell(fila, 17), x.Unidad);
            Numero(hoja.Cell(fila, 18), x.ValorUnitario);
            Numero(hoja.Cell(fila, 19), x.Importe);
            Numero(hoja.Cell(fila, 20), x.Descuento);
            Texto(hoja.Cell(fila, 21), x.ObjetoImp);
            Texto(hoja.Cell(fila, 22), x.CuentaPredial);
            Numero(hoja.Cell(fila, 23), x.Iva16);
            Numero(hoja.Cell(fila, 24), x.Iva8);
            Numero(hoja.Cell(fila, 25), x.IvaCero);
            Numero(hoja.Cell(fila, 26), x.IvaExento);
            Numero(hoja.Cell(fila, 27), x.IvaRetenido);
            Numero(hoja.Cell(fila, 28), x.IvaRetenido6);
            Numero(hoja.Cell(fila, 29), x.IsrRetenido);
            Numero(hoja.Cell(fila, 30), x.Ieps);
            Numero(hoja.Cell(fila, 31), x.Ieps3);
            Numero(hoja.Cell(fila, 32), x.Ieps6);
            Numero(hoja.Cell(fila, 33), x.Ieps7);
            Numero(hoja.Cell(fila, 34), x.Ieps8);
            Numero(hoja.Cell(fila, 35), x.Ieps9);
            Numero(hoja.Cell(fila, 36), x.Ieps265);
            Numero(hoja.Cell(fila, 37), x.Ieps30);
            Numero(hoja.Cell(fila, 38), x.Ieps304);
            Numero(hoja.Cell(fila, 39), x.Ieps53);
            Numero(hoja.Cell(fila, 40), x.Ieps160);
            Numero(hoja.Cell(fila, 41), x.TotalImpuestos);
            Texto(hoja.Cell(fila, 42), x.ArchivoXml);
            fila++;
        }

        int ultimaFila = fila - 1;
        IXLTable tabla = hoja.Range(
                filaEncabezados,
                1,
                ultimaFila,
                encabezados.Length)
            .CreateTable("TablaConceptos");
        tabla.Theme = XLTableTheme.TableStyleMedium4;

        hoja.SheetView.FreezeRows(filaEncabezados);
        hoja.SheetView.FreezeColumns(7);
        hoja.Rows(filaEncabezados + 1, ultimaFila).Height = 22;
        hoja.Range(filaEncabezados + 1, 1, ultimaFila, encabezados.Length)
            .Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        hoja.Range(filaEncabezados + 1, 1, ultimaFila, encabezados.Length)
            .Style.Alignment.WrapText = false;
        hoja.Columns().AdjustToContents();

        for (int columna = 1; columna <= encabezados.Length; columna++)
        {
            double anchoMaximo = columna switch
            {
                7 => 40,
                9 or 11 => 42,
                14 => 60,
                42 => 65,
                _ => 24
            };

            if (hoja.Column(columna).Width > anchoMaximo)
            {
                hoja.Column(columna).Width = anchoMaximo;
            }
        }

        hoja.Column(14).Width = 60;
        hoja.Column(42).Width = 65;
        hoja.Rows(4, ultimaFila).Height = 15;
        hoja.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        hoja.PageSetup.FitToPages(1, 0);
    }

    private static void Texto(IXLCell celda, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
        {
            celda.Value = valor;
        }
    }

    private static void Fecha(IXLCell celda, DateTime valor)
    {
        celda.Value = valor;
        celda.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
    }

    private static void Fecha(IXLCell celda, DateTime? valor)
    {
        if (valor.HasValue)
        {
            Fecha(celda, valor.Value);
        }
    }

    private static void Numero(
        IXLCell celda,
        decimal valor,
        string formato = "$#,##0.00")
    {
        celda.Value = valor;
        celda.Style.NumberFormat.Format = formato;
    }

    private static void Numero(
        IXLCell celda,
        decimal? valor,
        string formato = "$#,##0.00")
    {
        if (valor.HasValue)
        {
            Numero(celda, valor.Value, formato);
        }
    }

    private static void Validar(
        EmpresaDto empresa,
        DateTime fechaInicio,
        DateTime fechaFin)
    {
        ArgumentNullException.ThrowIfNull(empresa);

        if (empresa.EmpresaId <= 0)
        {
            throw new InvalidOperationException(
                "Selecciona una empresa válida.");
        }

        if (fechaFin.Date < fechaInicio.Date)
        {
            throw new InvalidOperationException(
                "La fecha final no puede ser menor que la fecha inicial.");
        }
    }
}

public sealed class ExportacionExcelResultado
{
    public string NombreArchivo { get; set; } = "";

    public string TipoContenido { get; set; } = "";

    public byte[] Contenido { get; set; } = Array.Empty<byte>();

    public string Clasificacion { get; set; } = "";

    public string NombreReporte { get; set; } = "";

    public int TotalRegistros { get; set; }
}
