using CFDIManager.Web.Components;
using CFDIManager.Web.Services;
using CFDIParser.Services;
using Npgsql;
using CFDIManager.Web.Services.Interfaces;
using CFDIManager.Web.Services.AdministracionXml;
using CFDIManager.Web.Services.Storage;
using CFDIParser.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using CFDIManager.Web.Services.Seguridad;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting.WindowsServices;

// Como servicio de Windows arranca en System32; Storage se busca junto al ejecutable.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null
});

builder.Services.AddWindowsService(opciones => opciones.ServiceName = "CFDIManager");

// Cultura de la app: fechas dd/MM/yyyy (también para servicios en segundo plano).
var CulturaMexico = new System.Globalization.CultureInfo("es-MX");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = CulturaMexico;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = CulturaMexico;

// Servicios

builder.Services.AddScoped<ISatAuthService, SatAuthService>();

builder.Services.AddScoped<ISatDescargaService, SatDescargaService>();                            

builder.Services.AddScoped<EmpresaCertificadoService>();

builder.Services.AddHttpClient();

// Llaves para cifrar la CIEC, el SMTP y SINUBE. Con CFDI_CLAVE_MAESTRA van en la base
// (proteccion_llaves) cifradas con esa clave, así se leen en cualquier equipo que tenga la
// misma clave. Sin ella, como antes: Storage\Llaves con DPAPI, solo legibles en este equipo.
IDataProtectionBuilder proteccion = builder.Services.AddDataProtection()
    .SetApplicationName("CFDIManager");

if (ClaveMaestra.Leer(builder.Configuration) is byte[] claveMaestra)
{
    builder.Services.Configure<KeyManagementOptions>(opciones =>
    {
        opciones.XmlRepository = new LlavesProteccionPostgres(CadenaPostgres(builder.Configuration));
        opciones.XmlEncryptor = new CifradoLlaves(claveMaestra);
    });
}
else
{
    proteccion
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "Storage", "Llaves")))
        .ProtectKeysWithDpapi();
}

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

// Cierre de mes del SAT (metadatos + XML faltantes); lo lanza la descarga automática o Descargas SAT.
builder.Services.AddSingleton<CFDIManager.Web.Services.Sat.CierreMesSatService>();

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

// e.firma de cada empresa en Storage\Certificados\{RFC}\{RFC}.cer/.key.
builder.Services.AddSingleton<CFDIManager.Web.Services.Sat.CertificadosEmpresa>();

builder.Services.AddScoped<CFDIManager.Web.Services.Sat.DescargaArchivosSatService>();

builder.Services.AddSingleton<CatalogoLogosService>();

builder.Services.AddScoped<RepresentacionImpresaService>();

builder.Services.AddScoped<AdministracionXmlService>();

// "Valida Estado SAT" sigue aunque se cambie de pantalla.
builder.Services.AddSingleton<ValidacionSatSegundoPlano>();

// ZIP de XML y PDF que la pantalla arma y el navegador baja por /api/descargas/{token}.
builder.Services.AddSingleton<DescargasTemporales>();

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

// Consultas de solo lectura a la base de SINUBE (getpost) para la conciliación SINUBE vs SAT.
builder.Services.AddHttpClient(
    CFDIManager.Web.Services.Sinube.SinubeDatosService.ClienteHttp,
    cliente => cliente.Timeout = TimeSpan.FromMinutes(3));

builder.Services.AddScoped<CFDIManager.Web.Services.Sinube.SinubeDatosService>();

builder.Services.AddScoped<CFDIManager.Web.Services.Sinube.SatMetadatosService>();

builder.Services.AddScoped<CFDIManager.Web.Services.Sinube.ConciliacionSinubeService>();

builder.Services.AddScoped<PostgreSqlRepository>(
    serviceProvider => new PostgreSqlRepository(
        CadenaPostgres(serviceProvider.GetRequiredService<IConfiguration>())));

// Usuarios, roles y empresas por usuario: ASP.NET Identity sobre las tablas de PostgreSQL
// (usuarios, roles, usuario_roles, usuario_empresas). Ver Services\Seguridad.
builder.Services
    .AddIdentity<UsuarioDto, RolDto>(opciones =>
    {
        opciones.Password.RequiredLength = 8;
        opciones.Password.RequireNonAlphanumeric = false;
        opciones.Lockout.MaxFailedAccessAttempts = 5;
        opciones.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddUserStore<UsuarioStore>()
    .AddRoleStore<RolStore>()
    .AddClaimsPrincipalFactory<UsuarioClaimsFactory>()
    .AddErrorDescriber<ErroresIdentityEs>();

builder.Services.ConfigureApplicationCookie(opciones =>
{
    opciones.Cookie.Name = "CFDIManager.Sesion";
    opciones.LoginPath = "/cuenta/login";
    opciones.LogoutPath = "/cuenta/logout";
    opciones.AccessDeniedPath = "/cuenta/acceso-denegado";
    // La sesión vence tras Sesion:MinutosInactividad sin usar la app (0 = 10 horas, como antes). El navegador
    // cierra la sesión un poco antes (js/sesion.js) y, mientras hay actividad, la renueva con /cuenta/actividad.
    int minutosInactividad = builder.Configuration.GetValue("Sesion:MinutosInactividad", 30);
    opciones.ExpireTimeSpan = minutosInactividad > 0 ? TimeSpan.FromMinutes(minutosInactividad + 5) : TimeSpan.FromHours(10);
    opciones.SlidingExpiration = true;
});

// Desactivar un usuario o cambiarle el rol/contraseña invalida sus sesiones en minutos.
builder.Services.Configure<SecurityStampValidatorOptions>(
    opciones => opciones.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.AddAuthorization(opciones =>
{
    Permisos.Registrar(opciones);
    // Una política por opción del menú: rol + tareas quitadas al usuario (Configuración → Usuarios).
    Tareas.Registrar(opciones);
});

builder.Services.AddMemoryCache();

builder.Services.AddSingleton<TareasUsuarioCache>();

builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, TareaHandler>();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<AuthenticationStateProvider, RevalidacionSesionProvider>();

builder.Services.AddScoped<AccesoEmpresasService>();

builder.Services.AddScoped<CuentasUsuarioService>();

// Correo SMTP (Configuración → Correo): bienvenidas, contraseñas temporales y envío de reportes.
builder.Services.AddScoped<CFDIManager.Web.Services.Correo.CorreoService>();

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (ClaveMaestra.Leer(app.Configuration) is null)
    app.Logger.LogWarning(
        "Sin {Variable}: las contraseñas guardadas (CIEC, correo, SINUBE) solo se podrán leer en este equipo.",
        ClaveMaestra.Nombre);

// Rutas de XML guardadas en otro equipo se buscan en la carpeta local (CFDIParser.Services.RutaXmlLocal).
RutaXmlLocal.CarpetaGeneral = app.Services.GetRequiredService<RutaXmlService>().CarpetaGeneral;

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
        int movidas = app.Services.GetRequiredService<CFDIManager.Web.Services.Sat.CertificadosEmpresa>()
            .Migrar(scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>(), app.Logger);
        if (movidas > 0)
            app.Logger.LogInformation("e.firma de {Cantidad} empresas pasada a su carpeta por RFC.", movidas);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "No se pudo pasar la e.firma a las carpetas por RFC.");
    }

    try
    {
        scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>().AsegurarSinube();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "No se pudieron crear las tablas de Exportación a SINUBE.");
    }

    try
    {
        PostgreSqlRepository repoMetadatos = scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>();
        repoMetadatos.AsegurarSatMetadatos();

        // Los CFDI que ya tienen metadatos del SAT toman de ahí su Estado SAT sin consultar uno por uno.
        int conEstado = repoMetadatos.AplicarEstadoSatDeMetadatos();
        if (conEstado > 0)
            app.Logger.LogInformation("Estado SAT tomado de los metadatos del SAT en {Cantidad} CFDI.", conEstado);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "No se pudieron crear las tablas de metadatos del SAT.");
    }

    try
    {
        scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>().AsegurarCorreo();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "No se pudieron crear las tablas de correo.");
    }

    try
    {
        await SeguridadInicial.AsegurarAsync(scope.ServiceProvider, app.Configuration, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "No se pudieron crear las tablas de usuarios y roles.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Error",
        createScopeForErrors: true);

    app.UseHsts();
}

// Fechas y números con la cultura de México (dd/MM/yyyy) en toda la app, sin depender del servidor ni del navegador.
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(CulturaMexico),
    SupportedCultures = [CulturaMexico],
    SupportedUICultures = [CulturaMexico]
});

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

// [FromForm] obliga a validar el token antiforgery del formulario de "Cerrar sesión".
app.MapPost(
    "/cuenta/logout",
    async ([FromForm] string? returnUrl, [FromForm] string? motivo, SignInManager<UsuarioDto> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return Results.LocalRedirect(motivo == "inactividad" ? "/cuenta/login?inactividad=1" : "/cuenta/login");
    });

// Mientras el usuario trabaja, js/sesion.js llama aquí cada pocos minutos para renovar la cookie de sesión
// (en Blazor Server la pantalla no hace peticiones HTTP que la renueven solas).
app.MapGet("/cuenta/actividad", () => Results.NoContent()).RequireAuthorization();

// Todo /api exige sesión; las rutas por empresa además validan que el usuario la tenga asignada.
RouteGroupBuilder api = app.MapGroup("/api").RequireAuthorization();

api.AddEndpointFilter(async (contexto, siguiente) =>
{
    HttpContext http = contexto.HttpContext;

    if (http.Request.RouteValues.TryGetValue("empresaId", out object? valor) &&
        long.TryParse(valor?.ToString(), out long empresaId) &&
        !http.RequestServices.GetRequiredService<AccesoEmpresasService>().PuedeAcceder(http.User, empresaId))
    {
        return Results.Forbid();
    }

    // XML inexistente, CFDI de otra empresa, etc.: mensaje legible en vez de la página de error.
    try
    {
        return await siguiente(contexto);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Text(ex.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
    }
});

// XML y PDF de un CFDI. Las usan las pantallas de consulta, EFOS y Administración de XML;
// ?descargar=1 entrega el archivo como descarga en vez de mostrarlo.
api.MapGet(
    "/empresa/{empresaId:long}/cfdi/{cfdiId:long}/xml",
    (long empresaId, long cfdiId, bool? descargar, AdministracionXmlService admin) =>
    {
        var empresa = new EmpresaDto { EmpresaId = empresaId };
        AdminXmlFilaDto cfdi = admin.Obtener(empresa, cfdiId);
        byte[] contenido = System.Text.Encoding.UTF8.GetBytes(admin.LeerXml(empresa, cfdiId));

        return descargar == true
            ? Results.File(contenido, "application/xml", $"{cfdi.Uuid}.xml")
            : Results.File(contenido, "text/plain; charset=utf-8");
    });

api.MapGet(
    "/empresa/{empresaId:long}/cfdi/{cfdiId:long}/pdf",
    (long empresaId, long cfdiId, bool? descargar, AdministracionXmlService admin) =>
    {
        var empresa = new EmpresaDto { EmpresaId = empresaId };
        AdminXmlFilaDto cfdi = admin.Obtener(empresa, cfdiId);

        return Results.File(
            admin.GenerarPdf(empresa, cfdiId),
            "application/pdf",
            descargar == true ? $"{cfdi.Uuid}.pdf" : null);
    });

// Primera página del PDF como imagen, para la vista previa de Administración de XML.
api.MapGet(
    "/empresa/{empresaId:long}/cfdi/{cfdiId:long}/vista",
    (long empresaId, long cfdiId, AdministracionXmlService admin) =>
        Results.File(admin.GenerarVistaPdf(new EmpresaDto { EmpresaId = empresaId }, cfdiId), "image/png"));

// Archivo armado por la pantalla (ZIP de XML o PDF); el token es de un solo uso y del mismo usuario.
api.MapGet(
    "/descargas/{token}",
    (string token, HttpContext http, DescargasTemporales descargas) =>
    {
        if (descargas.Tomar(token, http.User.Identity?.Name ?? "") is not { } d)
        {
            return Results.Text("La descarga ya no está disponible; vuelve a generarla.", "text/plain; charset=utf-8",
                statusCode: StatusCodes.Status404NotFound);
        }

        var archivo = new FileStream(d.Ruta, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920,
            d.Borrar ? FileOptions.DeleteOnClose | FileOptions.Asynchronous : FileOptions.Asynchronous);

        return Results.File(archivo, "application/zip", d.Nombre);
    });

// PDF de los archivos SAT de una empresa: cif, opinion-sat, opinion-imss o declaracion-{id}.
api.MapGet(
    "/empresa/{empresaId:long}/archivos-sat/{clave}",
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

api.MapGet(
    "/logos/{rfc}",
    (string rfc, CatalogoLogosService logos) =>
        logos.Leer(rfc) is byte[] imagen
            ? Results.File(imagen, logos.TipoContenido(rfc)!)
            : Results.NotFound());

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();

static string CadenaPostgres(IConfiguration configuration)
{
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

    return new NpgsqlConnectionStringBuilder(cadenaBase) { Password = password }.ConnectionString;
}

