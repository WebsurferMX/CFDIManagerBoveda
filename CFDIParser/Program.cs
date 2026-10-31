using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Npgsql;
using CFDIParser.Models;
using CFDIParser.Services;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

string cadenaBase = configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException("No existe ConnectionStrings:PostgreSQL.");

string password = Environment.GetEnvironmentVariable("CFDI_POSTGRES_PASSWORD")
    ?? throw new InvalidOperationException("No existe la variable CFDI_POSTGRES_PASSWORD.");

var connectionStringBuilder = new NpgsqlConnectionStringBuilder(cadenaBase)
{
    Password = password
};

string carpetaXml = configuration["AppSettings:CarpetaXml"]
    ?? throw new InvalidOperationException("No existe AppSettings:CarpetaXml.");

string carpetaExportaciones = configuration["AppSettings:CarpetaExportaciones"]
    ?? "Exportaciones";

var parser = new XmlCfdiParser();
var repo = new PostgreSqlRepository(connectionStringBuilder.ConnectionString);

EmpresaDto empresaActiva = SeleccionarEmpresa(repo);

bool continuar = true;

while (continuar)
{
    Console.Clear();
    Console.WriteLine("========================================");
    Console.WriteLine("              CFDI MANAGER");
    Console.WriteLine("========================================");
    Console.WriteLine($"Empresa: {empresaActiva.Rfc} · {empresaActiva.NombreParaMostrar}");
    Console.WriteLine();
    Console.WriteLine("1. Clasificar XML por RFC");
    Console.WriteLine("2. Importar XML emitidos");
    Console.WriteLine("3. Importar XML recibidos");
    Console.WriteLine("4. Facturación por cliente");
    Console.WriteLine("5. Resumen de importaciones");
    Console.WriteLine("6. Últimas importaciones");
    Console.WriteLine("7. Exportar facturación a Excel");
    Console.WriteLine("8. Cambiar empresa / RFC");
    Console.WriteLine("9. Salir");
    Console.WriteLine();
    Console.Write("Selecciona una opción: ");

    string opcion = Console.ReadLine() ?? "";
    Console.WriteLine();

    switch (opcion)
    {
        case "1":
            ClasificarXml(carpetaXml, empresaActiva.Rfc, parser);
            break;
        case "2":
            ImportarCarpetaClasificada(carpetaXml, empresaActiva, "Emitidos", parser, repo);
            break;
        case "3":
            ImportarCarpetaClasificada(carpetaXml, empresaActiva, "Recibidos", parser, repo);
            break;
        case "4":
            MostrarReporteClientes(repo, empresaActiva);
            break;
        case "5":
            MostrarResumenImportaciones(repo);
            break;
        case "6":
            MostrarUltimasImportaciones(repo);
            break;
        case "7":
            ExportarFacturacionExcel(repo, carpetaExportaciones, empresaActiva);
            break;
        case "8":
            empresaActiva = SeleccionarEmpresa(repo);
            break;

        case "9":
            continuar = false;
            Console.WriteLine("Aplicación finalizada.");
            break;
        default:
            Console.WriteLine("Opción no válida.");
            break;
    }

    if (continuar)
    {
        Console.WriteLine();
        Console.WriteLine("Presiona una tecla para volver al menú...");
        Console.ReadKey();
    }
}

static EmpresaDto SeleccionarEmpresa(PostgreSqlRepository repo)
{
    List<EmpresaDto> empresas = repo.ObtenerEmpresasActivas();

    if (empresas.Count == 0)
    {
        throw new InvalidOperationException(
            "No existen empresas activas registradas en PostgreSQL.");
    }

    while (true)
    {
        Console.Clear();
        Console.WriteLine("========================================");
        Console.WriteLine("       SELECCION DE EMPRESA / RFC");
        Console.WriteLine("========================================");
        Console.WriteLine();

        for (int indice = 0; indice < empresas.Count; indice++)
        {
            EmpresaDto empresa = empresas[indice];
            Console.WriteLine(
                $"{indice + 1}. " +
                $"[ID {empresa.EmpresaId}] " +
                $"{empresa.Rfc} · " +
                empresa.NombreParaMostrar);
        }

        Console.WriteLine();
        Console.Write("Selecciona una empresa: ");
        string entrada = Console.ReadLine() ?? "";

        if (int.TryParse(entrada, out int opcion) &&
            opcion >= 1 &&
            opcion <= empresas.Count)
        {
            EmpresaDto seleccionada = empresas[opcion - 1];

            Console.WriteLine();
            Console.WriteLine(
                $"Empresa seleccionada: " +
                $"[ID {seleccionada.EmpresaId}] " +
                $"{seleccionada.Rfc} · " +
                seleccionada.NombreParaMostrar);

            return seleccionada;
        }

        Console.WriteLine();
        Console.WriteLine("Opción no válida. Presiona una tecla para intentar de nuevo...");
        Console.ReadKey();
    }
}

static void ClasificarXml(string carpetaBase, string rfcEmpresa, XmlCfdiParser parser)
{
    if (!Directory.Exists(carpetaBase))
    {
        Console.WriteLine($"La carpeta base no existe: {carpetaBase}");
        return;
    }

    string carpetaEntrada = Path.Combine(carpetaBase, "Entrada");

    if (!Directory.Exists(carpetaEntrada))
    {
        Console.WriteLine($"La carpeta de entrada no existe: {carpetaEntrada}");
        return;
    }

    string carpetaEmpresa = Path.Combine(carpetaBase, rfcEmpresa);
    string carpetaEmitidos = Path.Combine(carpetaEmpresa, "Emitidos");
    string carpetaRecibidos = Path.Combine(carpetaEmpresa, "Recibidos");
    string carpetaPropios = Path.Combine(carpetaEmpresa, "Propios");
    string carpetaNoIdentificados = Path.Combine(carpetaEmpresa, "NoIdentificados");

    Directory.CreateDirectory(carpetaEmitidos);
    Directory.CreateDirectory(carpetaRecibidos);
    Directory.CreateDirectory(carpetaPropios);
    Directory.CreateDirectory(carpetaNoIdentificados);

    string[] archivos = Directory.GetFiles(carpetaEntrada, "*.xml", SearchOption.AllDirectories);

    Console.WriteLine("=== CLASIFICACIÓN DE XML ===");
    Console.WriteLine();
    Console.WriteLine($"RFC de trabajo: {rfcEmpresa}");
    Console.WriteLine($"Carpeta de entrada: {carpetaEntrada}");
    Console.WriteLine($"Entrada existe: {Directory.Exists(carpetaEntrada)}");
    Console.WriteLine($"XML encontrados: {archivos.Length}");
    Console.WriteLine();

    int emitidos = 0;
    int recibidos = 0;
    int propios = 0;
    int noIdentificados = 0;
    int errores = 0;

    foreach (string archivo in archivos)
    {
        try
        {
            var cfdi = parser.Parse(archivo);
            string rfcEmisor = (cfdi.RFCEmisor ?? "").Trim().ToUpperInvariant();
            string rfcReceptor = (cfdi.RFCReceptor ?? "").Trim().ToUpperInvariant();
            bool esEmisor = rfcEmisor == rfcEmpresa;
            bool esReceptor = rfcReceptor == rfcEmpresa;

            string carpetaDestino;
            string clasificacion;

            if (esEmisor && esReceptor)
            {
                carpetaDestino = carpetaPropios;
                clasificacion = "PROPIO";
                propios++;
            }
            else if (esEmisor)
            {
                carpetaDestino = carpetaEmitidos;
                clasificacion = "EMITIDO";
                emitidos++;
            }
            else if (esReceptor)
            {
                carpetaDestino = carpetaRecibidos;
                clasificacion = "RECIBIDO";
                recibidos++;
            }
            else
            {
                carpetaDestino = carpetaNoIdentificados;
                clasificacion = "NO IDENTIFICADO";
                noIdentificados++;
            }

            string nombreArchivo = Path.GetFileName(archivo);
            string rutaDestino = ObtenerRutaDisponible(
                Path.Combine(carpetaDestino, nombreArchivo),
                cfdi.UUID);

            File.Move(archivo, rutaDestino);
            Console.WriteLine($"{clasificacion} -> {nombreArchivo}");
        }
        catch (Exception ex)
        {
            errores++;
            Console.WriteLine($"ERROR -> {Path.GetFileName(archivo)}");
            Console.WriteLine($"Detalle: {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("=== RESUMEN DE CLASIFICACIÓN ===");
    Console.WriteLine($"Emitidos:         {emitidos}");
    Console.WriteLine($"Recibidos:        {recibidos}");
    Console.WriteLine($"Propios:          {propios}");
    Console.WriteLine($"No identificados: {noIdentificados}");
    Console.WriteLine($"Errores:          {errores}");
    Console.WriteLine();
    Console.WriteLine($"Carpeta empresa: {carpetaEmpresa}");
}

static string ObtenerRutaDisponible(string rutaDestino, string uuid)
{
    if (!File.Exists(rutaDestino))
    {
        return rutaDestino;
    }

    string carpeta = Path.GetDirectoryName(rutaDestino)
        ?? throw new InvalidOperationException("No se pudo obtener la carpeta destino.");
    string extension = Path.GetExtension(rutaDestino);
    string nombreSinExtension = Path.GetFileNameWithoutExtension(rutaDestino);
    string sufijo = string.IsNullOrWhiteSpace(uuid)
        ? DateTime.Now.ToString("yyyyMMddHHmmssfff")
        : uuid.Trim();

    string nuevaRuta = Path.Combine(carpeta, $"{nombreSinExtension}_{sufijo}{extension}");
    int consecutivo = 1;

    while (File.Exists(nuevaRuta))
    {
        nuevaRuta = Path.Combine(
            carpeta,
            $"{nombreSinExtension}_{sufijo}_{consecutivo}{extension}");
        consecutivo++;
    }

    return nuevaRuta;
}

static string ConvertirClasificacion(
    string nombreCarpeta)
{
    return nombreCarpeta
        .Trim()
        .ToUpperInvariant() switch
    {
        "EMITIDOS" =>
            "EMITIDO",

        "RECIBIDOS" =>
            "RECIBIDO",

        "PROPIOS" =>
            "PROPIO",

        "NOIDENTIFICADOS" =>
            "NO_IDENTIFICADO",

        "NO_IDENTIFICADOS" =>
            "NO_IDENTIFICADO",

        _ =>
            throw new InvalidOperationException(
                $"Clasificación no reconocida: {nombreCarpeta}")
    };
}


static void ImportarCarpetaClasificada(
    string carpetaBase,
    EmpresaDto empresa,
    string nombreCarpeta,
    XmlCfdiParser parser,
    PostgreSqlRepository repo)
{
    long empresaId = empresa.EmpresaId;
    string rfcEmpresa = empresa.Rfc.Trim().ToUpperInvariant();

    string carpetaSeleccionada =
        Path.Combine(
            carpetaBase,
            rfcEmpresa,
            nombreCarpeta);

    if (!Directory.Exists(carpetaSeleccionada))
    {
        Console.WriteLine(
            $"La carpeta no existe: {carpetaSeleccionada}");

        Console.WriteLine(
            "Primero ejecuta la clasificación.");

        return;
    }

    string clasificacion =
        ConvertirClasificacion(nombreCarpeta);

    string[] archivos =
        Directory.GetFiles(
            carpetaSeleccionada,
            "*.xml",
            SearchOption.AllDirectories);

    Console.WriteLine(
        $"=== IMPORTACIÓN: {clasificacion} ===");

    Console.WriteLine();
    Console.WriteLine(
        $"RFC seleccionado: {rfcEmpresa}");

    Console.WriteLine(
        $"Carpeta: {carpetaSeleccionada}");

    Console.WriteLine(
        $"Archivos encontrados: {archivos.Length}");

    Console.WriteLine();

    int correctos = 0;
    int duplicados = 0;
    int errores = 0;

    foreach (string archivo in archivos)
    {
        string uuidBitacora = "";

        try
        {
            var resultado =
                parser.Parse(archivo);

            uuidBitacora =
                resultado.UUID;

            if (string.IsNullOrWhiteSpace(
                    resultado.UUID))
            {
                throw new InvalidOperationException(
                    "El XML no contiene un UUID válido.");
            }

            string rutaXml =
                Path.GetFullPath(archivo);

            long cfdiId =
                repo.GuardarCfdi(
                    empresaId,
                    resultado,
                    clasificacion,
                    rutaXml);

            repo.GuardarConceptos(
                cfdiId,
                resultado.Conceptos);

            repo.GuardarImportacion(
                Path.GetFileName(archivo),
                resultado.UUID,
                "OK",
                $"Clasificación: {clasificacion}");

            correctos++;

            Console.WriteLine(
                $"OK -> {resultado.UUID}");
        }
        catch (Exception ex)
        {
            errores++;

            try
            {
                repo.GuardarImportacion(
                    Path.GetFileName(archivo),
                    uuidBitacora,
                    "ERROR",
                    ex.Message);
            }
            catch (Exception errorBitacora)
            {
                Console.WriteLine(
                    "No se pudo registrar el error " +
                    $"en la bitácora: {errorBitacora.Message}");
            }

            Console.WriteLine(
                $"ERROR -> {Path.GetFileName(archivo)}");

            Console.WriteLine(
                $"Detalle: {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        "=== RESUMEN DE IMPORTACIÓN ===");

    Console.WriteLine(
        $"Clasificación: {clasificacion}");

    Console.WriteLine(
        $"Procesados:    {archivos.Length}");

    Console.WriteLine(
        $"Correctos:     {correctos}");

    Console.WriteLine(
        $"Duplicados:    {duplicados}");

    Console.WriteLine(
        $"Errores:       {errores}");
}

static void MostrarReporteClientes(PostgreSqlRepository repo, EmpresaDto empresa)
{
    Console.WriteLine("=== FACTURACIÓN POR CLIENTE ===");
    Console.WriteLine();

    DateTime? fechaInicio = LeerFecha("Fecha inicial (AAAA-MM-DD): ");
    if (fechaInicio == null) return;

    DateTime? fechaFin = LeerFecha("Fecha final (AAAA-MM-DD): ");
    if (fechaFin == null) return;

    if (fechaFin.Value.Date < fechaInicio.Value.Date)
    {
        Console.WriteLine("La fecha final no puede ser menor que la fecha inicial.");
        return;
    }

    var reporte = repo.ObtenerFacturacionClientes(empresa.EmpresaId, fechaInicio.Value, fechaFin.Value);

    if (reporte.Count == 0)
    {
        Console.WriteLine("No hay CFDI en el periodo seleccionado.");
        return;
    }

    decimal totalGeneral = 0;
    long cantidadGeneral = 0;

    foreach (var cliente in reporte)
    {
        Console.WriteLine("----------------------------------------");
        Console.WriteLine($"RFC: {cliente.RFC}");
        Console.WriteLine($"Cliente: {cliente.Nombre}");
        Console.WriteLine($"Cantidad CFDI: {cliente.CantidadCfdi}");
        Console.WriteLine($"Total facturado: {cliente.TotalFacturado:N2}");
        Console.WriteLine($"Primera factura: {cliente.PrimeraFactura:yyyy-MM-dd}");
        Console.WriteLine($"Última factura: {cliente.UltimaFactura:yyyy-MM-dd}");

        cantidadGeneral += cliente.CantidadCfdi;
        totalGeneral += cliente.TotalFacturado;
    }

    Console.WriteLine("========================================");
    Console.WriteLine($"Clientes encontrados: {reporte.Count}");
    Console.WriteLine($"Cantidad total CFDI: {cantidadGeneral}");
    Console.WriteLine($"Facturación total: {totalGeneral:N2}");
}

static DateTime? LeerFecha(string mensaje)
{
    Console.Write(mensaje);
    string valor = Console.ReadLine() ?? "";

    bool esValida = DateTime.TryParseExact(
        valor,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out DateTime fecha);

    if (!esValida)
    {
        Console.WriteLine("Fecha no válida. Usa el formato AAAA-MM-DD.");
        return null;
    }

    return fecha;
}

static void MostrarResumenImportaciones(PostgreSqlRepository repo)
{
    var resumen = repo.ObtenerResumenImportaciones();

    Console.WriteLine("=== RESUMEN DE IMPORTACIONES ===");
    Console.WriteLine();

    if (resumen.Count == 0)
    {
        Console.WriteLine("No hay importaciones registradas.");
        return;
    }

    long total = 0;

    foreach (var item in resumen)
    {
        Console.WriteLine($"{item.Estatus,-15} {item.Cantidad,10:N0}");
        total += item.Cantidad;
    }

    Console.WriteLine("---------------------------");
    Console.WriteLine($"{"TOTAL",-15} {total,10:N0}");
}

static void MostrarUltimasImportaciones(PostgreSqlRepository repo)
{
    Console.Write("Cantidad de registros a mostrar: ");
    string valor = Console.ReadLine() ?? "";

    if (!int.TryParse(valor, out int cantidad) || cantidad <= 0)
    {
        cantidad = 20;
    }

    var importaciones = repo.ObtenerUltimasImportaciones(cantidad);

    Console.WriteLine();
    Console.WriteLine("=== ÚLTIMAS IMPORTACIONES ===");
    Console.WriteLine();

    if (importaciones.Count == 0)
    {
        Console.WriteLine("No hay importaciones registradas.");
        return;
    }

    foreach (var item in importaciones)
    {
        Console.WriteLine("----------------------------------------");
        Console.WriteLine($"ID: {item.ImportacionId}");
        Console.WriteLine($"Fecha: {item.FechaImportacion:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Archivo: {item.Archivo}");
        Console.WriteLine($"UUID: {item.UUID}");
        Console.WriteLine($"Estatus: {item.Estatus}");

        if (!string.IsNullOrWhiteSpace(item.MensajeError))
        {
            Console.WriteLine($"Detalle: {item.MensajeError}");
        }
    }
}

static void ExportarFacturacionExcel(
    PostgreSqlRepository repo,
    string carpetaExportaciones,
    EmpresaDto empresa)
{
    string rfcEmpresa = empresa.Rfc;

    Console.WriteLine("=== EXPORTAR FACTURACIÓN A EXCEL ===");
    Console.WriteLine();

    DateTime? fechaInicio = LeerFecha("Fecha inicial (AAAA-MM-DD): ");
    if (fechaInicio == null) return;

    DateTime? fechaFin = LeerFecha("Fecha final (AAAA-MM-DD): ");
    if (fechaFin == null) return;

    if (fechaFin.Value.Date < fechaInicio.Value.Date)
    {
        Console.WriteLine("La fecha final no puede ser menor que la fecha inicial.");
        return;
    }

    var reporte = repo.ObtenerFacturacionClientes(empresa.EmpresaId, fechaInicio.Value, fechaFin.Value);

    if (reporte.Count == 0)
    {
        Console.WriteLine("No hay CFDI en el periodo seleccionado.");
        return;
    }

    string rutaExportaciones = Path.IsPathRooted(carpetaExportaciones)
        ? carpetaExportaciones
        : Path.Combine(Environment.CurrentDirectory, carpetaExportaciones);

    Directory.CreateDirectory(rutaExportaciones);

    string nombreArchivo =
        $"FacturacionClientes_{rfcEmpresa}_" +
        $"{fechaInicio.Value:yyyyMMdd}_{fechaFin.Value:yyyyMMdd}_" +
        $"{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

    string rutaArchivo = Path.Combine(rutaExportaciones, nombreArchivo);

    decimal totalGeneral = reporte.Sum(x => x.TotalFacturado);
    long cantidadGeneral = reporte.Sum(x => x.CantidadCfdi);
    decimal promedioPorCfdi = cantidadGeneral > 0
        ? totalGeneral / cantidadGeneral
        : 0;

    DateTime primeraFactura = reporte.Min(x => x.PrimeraFactura);
    DateTime ultimaFactura = reporte.Max(x => x.UltimaFactura);
    var clientePrincipal = reporte.OrderByDescending(x => x.TotalFacturado).First();
    var topClientes = reporte.OrderByDescending(x => x.TotalFacturado).Take(10).ToList();

    using var workbook = new XLWorkbook();

    var resumen = workbook.Worksheets.Add("Resumen");
    resumen.ShowGridLines = false;
    resumen.Column(1).Width = 3;
    resumen.Column(2).Width = 25;
    resumen.Column(3).Width = 20;
    resumen.Column(4).Width = 35;
    resumen.Column(5).Width = 22;
    resumen.Column(6).Width = 20;

    resumen.Range("B2:F2").Merge();
    resumen.Cell("B2").Value = "RESUMEN DE FACTURACIÓN";
    resumen.Cell("B2").Style.Font.Bold = true;
    resumen.Cell("B2").Style.Font.FontSize = 20;
    resumen.Cell("B2").Style.Font.FontColor = XLColor.White;
    resumen.Cell("B2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    resumen.Range("B2:F2").Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");

    resumen.Range("B3:F3").Merge();
    resumen.Cell("B3").Value =
        $"RFC: {rfcEmpresa} | Periodo: {fechaInicio.Value:yyyy-MM-dd} al {fechaFin.Value:yyyy-MM-dd}";
    resumen.Cell("B3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

    CrearTarjetaKpiDecimal(
        resumen, "B5:C5", "B6:C7", "TOTAL FACTURADO",
        totalGeneral, "$#,##0.00", "#1F4E78");

    CrearTarjetaKpiEntero(
        resumen, "E5:F5", "E6:F7", "CANTIDAD DE CFDI",
        cantidadGeneral, "#,##0", "#548235");

    CrearTarjetaKpiEntero(
        resumen, "B9:C9", "B10:C11", "CLIENTES",
        reporte.Count, "#,##0", "#BF8F00");

    CrearTarjetaKpiDecimal(
        resumen, "E9:F9", "E10:F11", "PROMEDIO POR CFDI",
        promedioPorCfdi, "$#,##0.00", "#7030A0");

    resumen.Cell("B13").Value = "Primera factura";
    resumen.Cell("C13").Value = primeraFactura;
    resumen.Cell("C13").Style.DateFormat.Format = "yyyy-mm-dd";
    resumen.Cell("E13").Value = "Última factura";
    resumen.Cell("F13").Value = ultimaFactura;
    resumen.Cell("F13").Style.DateFormat.Format = "yyyy-mm-dd";
    resumen.Cell("B14").Value = "Cliente principal";
    resumen.Range("C14:F14").Merge();
    resumen.Cell("C14").Value = $"{clientePrincipal.RFC} - {clientePrincipal.Nombre}";
    resumen.Cell("B15").Value = "Facturación principal";
    resumen.Cell("C15").Value = clientePrincipal.TotalFacturado;
    resumen.Cell("C15").Style.NumberFormat.Format = "$#,##0.00";
    resumen.Range("B13:F15").Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
    resumen.Range("B13:F15").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

    resumen.Range("B17:F17").Merge();
    resumen.Cell("B17").Value = "TOP 10 CLIENTES POR FACTURACIÓN";
    resumen.Cell("B17").Style.Font.Bold = true;
    resumen.Cell("B17").Style.Font.FontColor = XLColor.White;
    resumen.Cell("B17").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    resumen.Range("B17:F17").Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");

    resumen.Cell("B18").Value = "Posición";
    resumen.Cell("C18").Value = "RFC";
    resumen.Cell("D18").Value = "Cliente";
    resumen.Cell("E18").Value = "CFDI";
    resumen.Cell("F18").Value = "Total facturado";

    int filaTop = 19;
    int posicion = 1;

    foreach (var cliente in topClientes)
    {
        resumen.Cell(filaTop, 2).Value = posicion;
        resumen.Cell(filaTop, 3).Value = cliente.RFC;
        resumen.Cell(filaTop, 4).Value = cliente.Nombre;
        resumen.Cell(filaTop, 5).Value = cliente.CantidadCfdi;
        resumen.Cell(filaTop, 6).Value = cliente.TotalFacturado;
        resumen.Cell(filaTop, 6).Style.NumberFormat.Format = "$#,##0.00";
        filaTop++;
        posicion++;
    }

    var tablaTop = resumen.Range(18, 2, filaTop - 1, 6).CreateTable("TablaTopClientes");
    tablaTop.Theme = XLTableTheme.TableStyleMedium2;
    resumen.SheetView.FreezeRows(3);

    var detalle = workbook.Worksheets.Add("Facturación por cliente");
    detalle.Cell(1, 1).Value = "FACTURACIÓN POR CLIENTE";
    detalle.Range(1, 1, 1, 6).Merge();
    detalle.Cell(1, 1).Style.Font.Bold = true;
    detalle.Cell(1, 1).Style.Font.FontSize = 16;
    detalle.Cell(1, 1).Style.Font.FontColor = XLColor.White;
    detalle.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    detalle.Range(1, 1, 1, 6).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");

    detalle.Cell(2, 1).Value =
        $"RFC: {rfcEmpresa} | Periodo: {fechaInicio.Value:yyyy-MM-dd} al {fechaFin.Value:yyyy-MM-dd}";
    detalle.Range(2, 1, 2, 6).Merge();

    const int filaEncabezados = 4;
    detalle.Cell(filaEncabezados, 1).Value = "RFC";
    detalle.Cell(filaEncabezados, 2).Value = "Cliente";
    detalle.Cell(filaEncabezados, 3).Value = "Cantidad CFDI";
    detalle.Cell(filaEncabezados, 4).Value = "Total facturado";
    detalle.Cell(filaEncabezados, 5).Value = "Primera factura";
    detalle.Cell(filaEncabezados, 6).Value = "Última factura";

    int fila = filaEncabezados + 1;

    foreach (var cliente in reporte)
    {
        detalle.Cell(fila, 1).Value = cliente.RFC;
        detalle.Cell(fila, 2).Value = cliente.Nombre;
        detalle.Cell(fila, 3).Value = cliente.CantidadCfdi;
        detalle.Cell(fila, 4).Value = cliente.TotalFacturado;
        detalle.Cell(fila, 5).Value = cliente.PrimeraFactura;
        detalle.Cell(fila, 6).Value = cliente.UltimaFactura;
        fila++;
    }

    int ultimaFila = fila - 1;
    var tabla = detalle.Range(filaEncabezados, 1, ultimaFila, 6)
        .CreateTable("TablaFacturacionClientes");
    tabla.Theme = XLTableTheme.TableStyleMedium2;

    detalle.Column(1).Style.NumberFormat.Format = "@";
    detalle.Column(3).Style.NumberFormat.Format = "0";
    detalle.Column(4).Style.NumberFormat.Format = "$#,##0.00";
    detalle.Column(5).Style.DateFormat.Format = "yyyy-mm-dd";
    detalle.Column(6).Style.DateFormat.Format = "yyyy-mm-dd";
    detalle.SheetView.FreezeRows(filaEncabezados);
    detalle.Columns().AdjustToContents();

    if (detalle.Column(2).Width > 50)
    {
        detalle.Column(2).Width = 50;
    }

    int filaTotales = ultimaFila + 2;
    detalle.Cell(filaTotales, 2).Value = "TOTALES";
    detalle.Cell(filaTotales, 3).Value = cantidadGeneral;
    detalle.Cell(filaTotales, 4).Value = totalGeneral;
    detalle.Range(filaTotales, 2, filaTotales, 4).Style.Font.Bold = true;
    detalle.Cell(filaTotales, 4).Style.NumberFormat.Format = "$#,##0.00";

    workbook.SaveAs(rutaArchivo);

    Console.WriteLine();
    Console.WriteLine("Exportación terminada correctamente.");
    Console.WriteLine($"Archivo: {rutaArchivo}");
}

static void CrearTarjetaKpiDecimal(
    IXLWorksheet hoja,
    string rangoTitulo,
    string rangoValor,
    string titulo,
    decimal valor,
    string formato,
    string colorHtml)
{
    PrepararTarjetaKpi(hoja, rangoTitulo, rangoValor, titulo, formato, colorHtml);
    hoja.Range(rangoValor).FirstCell().Value = valor;
}

static void CrearTarjetaKpiEntero(
    IXLWorksheet hoja,
    string rangoTitulo,
    string rangoValor,
    string titulo,
    long valor,
    string formato,
    string colorHtml)
{
    PrepararTarjetaKpi(hoja, rangoTitulo, rangoValor, titulo, formato, colorHtml);
    hoja.Range(rangoValor).FirstCell().Value = valor;
}

static void PrepararTarjetaKpi(
    IXLWorksheet hoja,
    string rangoTitulo,
    string rangoValor,
    string titulo,
    string formato,
    string colorHtml)
{
    hoja.Range(rangoTitulo).Merge();
    hoja.Range(rangoValor).Merge();

    var celdaTitulo = hoja.Range(rangoTitulo).FirstCell();
    var celdaValor = hoja.Range(rangoValor).FirstCell();
    XLColor color = XLColor.FromHtml(colorHtml);

    celdaTitulo.Value = titulo;
    celdaTitulo.Style.Fill.BackgroundColor = color;
    celdaTitulo.Style.Font.FontColor = XLColor.White;
    celdaTitulo.Style.Font.Bold = true;
    celdaTitulo.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    celdaTitulo.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

    celdaValor.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    celdaValor.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    celdaValor.Style.Font.Bold = true;
    celdaValor.Style.Font.FontSize = 18;
    celdaValor.Style.NumberFormat.Format = formato;

    hoja.Range(rangoTitulo).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    hoja.Range(rangoValor).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;



}
