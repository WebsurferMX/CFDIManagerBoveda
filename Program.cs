using CFDIManager.Web.Components;
using CFDIManager.Web.Services;
using CFDIParser.Services;
using Npgsql;
using CFDIManager.Web.Services.Interfaces;
using CFDIManager.Web.Services.AdministracionXml;
using CFDIManager.Web.Services.Storage;
using CFDIParser.Models;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Servicios

builder.Services.AddScoped<ISatAuthService, SatAuthService>();

builder.Services.AddScoped<ISatDescargaService, SatDescargaService>();                            

builder.Services.AddScoped<EmpresaCertificadoService>();

builder.Services.AddHttpClient();

// Llaves para cifrar la CIEC: en Storage\Llaves (protegidas con DPAPI de Windows) y con
// nombre fijo para que mover la carpeta de la app no impida leer las CIEC guardadas.
builder.Services.AddDataProtection()
    .SetApplicationName("CFDIManager")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "Storage", "Llaves")))
    .ProtectKeysWithDpapi();

// Los paquetes de la descarga masiva pueden pesar decenas de MB.
builder.Services.AddHttpClient(
    "SatDescargaMasiva",
    cliente => cliente.Timeout = TimeSpan.FromMinutes(10));

// Ruta XML general (AppSettings:CarpetaXml): cada empresa usa <ruta>\RFC.
builder.Services.AddSingleton<RutaXmlService>();

builder.Services.AddScoped<ImportacionCfdiService>();

// EFOS / EDOS: Listado completo del artículo 69-B (≈4.5 MB desde el portal del SAT).
builder.Services.AddHttpClient(
    "SatListados",
    cliente => cliente.Timeout = TimeSpan.FromMinutes(3));

builder.Services.AddScoped<IEfosSatService, EfosSatService>();

// La carpeta de XML de cada empresa (Storage) es la fuente de verdad; la base se
// mantiene sincronizada sola al arrancar y cada SincronizacionStorage:Minutos.
builder.Services.AddSingleton<EstadoSincronizacionStorage>();

builder.Services.AddScoped<SincronizacionStorageService>();

builder.Services.AddHostedService<SincronizacionStorageHostedService>();

// Descarga masiva automática del SAT para las empresas que la tienen encendida.
builder.Services.AddSingleton<CFDIManager.Web.Services.Sat.EstadoDescargaAutomaticaSat>();

builder.Services.AddHostedService<CFDIManager.Web.Services.Sat.DescargaAutomaticaSatHostedService>();

// Administración de XML
builder.Services.AddSingleton<CFDIManager.Web.Services.Sat.CiecProtector>();

// Una sesión del portal del SAT por pestaña del navegador (circuito de Blazor).
builder.Services.AddScoped<CFDIManager.Web.Services.Sat.DescargaCiecService>();

builder.Services.AddSingleton<CFDIManager.Web.Services.Cif.ConstanciaFiscalService>();

// Opiniones de cumplimiento y acuses de declaraciones de cada empresa (Empresas → General / Declaraciones).
builder.Services.AddSingleton<CFDIManager.Web.Services.Cif.ArchivosSatService>();

// Opinión y constancia por SAT Móvil (RFC + CIEC, sin captcha) y acuses de declaraciones
// por el portal del SAT (con captcha; una sesión por pestaña del navegador).
builder.Services.AddHttpClient(
    "SatMovil",
    cliente => cliente.Timeout = TimeSpan.FromSeconds(45));

builder.Services.AddSingleton<CFDIManager.Web.Services.Sat.SatMovilCliente>();

builder.Services.AddScoped<CFDIManager.Web.Services.Sat.DescargaArchivosSatService>();

builder.Services.AddSingleton<CatalogoLogosService>();

builder.Services.AddScoped<RepresentacionImpresaService>();

builder.Services.AddScoped<AdministracionXmlService>();

// QuestPDF exige declarar la licencia antes de generar documentos.
// Community: gratuita para organizaciones con ingresos anuales menores a 1 millón de USD.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

builder.Services.AddScoped<EmpresaContext>();

builder.Services.AddScoped<ConsultaEstadoSatService>();

builder.Services.AddScoped<ValidacionSatLoteService>();

builder.Services.AddScoped<XmlCfdiParser>();

builder.Services.AddScoped<ExportacionExcelService>();

builder.Services.AddScoped<XmlAzulejerasService>();

// Exportación a SINUBE (mismo protocolo que HerramientasSinube; 40 s por petición como el original).
builder.Services.AddHttpClient(
    CFDIManager.Web.Services.Sinube.SinubeService.ClienteHttp,
    cliente => cliente.Timeout = TimeSpan.FromSeconds(40));

builder.Services.AddScoped<CFDIManager.Web.Services.Sinube.SinubeService>();

builder.Services.AddScoped<PostgreSqlRepository>(
    serviceProvider =>
    {
        IConfiguration configuration =
            serviceProvider.GetRequiredService<IConfiguration>();

        string cadenaBase =
            configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException(
                "No existe ConnectionStrings:PostgreSQL.");

        // Variable de entorno o, en desarrollo, user secrets (dotnet user-secrets set CFDI_POSTGRES_PASSWORD ...).
        string password =
            Environment.GetEnvironmentVariable(
                "CFDI_POSTGRES_PASSWORD")
            ?? configuration["CFDI_POSTGRES_PASSWORD"]
            ?? throw new InvalidOperationException(
                "No existe la variable CFDI_POSTGRES_PASSWORD (ni en user secrets).");

        var connectionBuilder =
            new NpgsqlConnectionStringBuilder(cadenaBase)
            {
                Password = password
            };

        return new PostgreSqlRepository(
            connectionBuilder.ConnectionString);
    });

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// El mismo CFDI puede pertenecer a dos empresas del grupo (emitido en una, recibido
// en la otra): el UUID es único por empresa, no en toda la tabla.
using (IServiceScope scope = app.Services.CreateScope())
{
    try
    {
        scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>().AsegurarUuidPorEmpresa();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "No se pudo ajustar el índice de UUID por empresa.");
    }

    try
    {
        scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>().AsegurarDescargaAutomatica();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "No se pudieron crear las columnas de descarga automática del SAT.");
    }

    try
    {
        scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>().AsegurarCiec();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "No se pudieron crear las columnas de la CIEC.");
    }

    try
    {
        scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>().AsegurarSinube();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "No se pudieron crear las tablas de Exportación a SINUBE.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Error",
        createScopeForErrors: true);

    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

// XML y PDF de un CFDI. Las usan las pantallas de consulta, EFOS y Administración de XML;
// ?descargar=1 entrega el archivo como descarga en vez de mostrarlo.
app.MapGet(
    "/api/empresa/{empresaId:long}/cfdi/{cfdiId:long}/xml",
    (long empresaId, long cfdiId, bool? descargar, AdministracionXmlService admin) =>
    {
        var empresa = new EmpresaDto { EmpresaId = empresaId };
        AdminXmlFilaDto cfdi = admin.Obtener(empresa, cfdiId);
        byte[] contenido = System.Text.Encoding.UTF8.GetBytes(admin.LeerXml(empresa, cfdiId));

        return descargar == true
            ? Results.File(contenido, "application/xml", $"{cfdi.Uuid}.xml")
            : Results.File(contenido, "text/plain; charset=utf-8");
    });

app.MapGet(
    "/api/empresa/{empresaId:long}/cfdi/{cfdiId:long}/pdf",
    (long empresaId, long cfdiId, bool? descargar, AdministracionXmlService admin) =>
    {
        var empresa = new EmpresaDto { EmpresaId = empresaId };
        AdminXmlFilaDto cfdi = admin.Obtener(empresa, cfdiId);

        return Results.File(
            admin.GenerarPdf(empresa, cfdiId),
            "application/pdf",
            descargar == true ? $"{cfdi.Uuid}.pdf" : null);
    });

// PDF de los archivos SAT de una empresa: cif, opinion-sat, opinion-imss o declaracion-{id}.
app.MapGet(
    "/api/empresa/{empresaId:long}/archivos-sat/{clave}",
    (long empresaId, string clave, bool? descargar,
     CFDIManager.Web.Services.Cif.ConstanciaFiscalService constancias,
     CFDIManager.Web.Services.Cif.ArchivosSatService archivos) =>
    {
        string? ruta = clave switch
        {
            "cif" => constancias.RutaPdf(empresaId),
            "opinion-sat" => archivos.RutaOpinion(empresaId, "SAT"),
            "opinion-imss" => archivos.RutaOpinion(empresaId, "IMSS"),
            _ when clave.StartsWith("declaracion-") => archivos.RutaDeclaracion(empresaId, clave["declaracion-".Length..]),
            _ => null
        };

        return ruta is null
            ? Results.NotFound()
            : Results.File(File.ReadAllBytes(ruta), "application/pdf", descargar == true ? Path.GetFileName(ruta) : null);
    });

app.MapGet(
    "/api/logos/{rfc}",
    (string rfc, CatalogoLogosService logos) =>
        logos.Leer(rfc) is byte[] imagen
            ? Results.File(imagen, logos.TipoContenido(rfc)!)
            : Results.NotFound());

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();

