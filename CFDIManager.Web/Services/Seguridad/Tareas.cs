using System.Security.Claims;
using CFDIParser.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace CFDIManager.Web.Services.Seguridad;

/// <summary>Una opción del menú que se puede asignar o quitar a cada usuario.</summary>
/// <param name="Roles">Roles que pueden tenerla (el rol es el tope; al usuario se le pueden quitar).</param>
/// <param name="Fija">Siempre asignada (no aparece para quitar).</param>
public sealed record Tarea(
    string Clave, string Nombre, string Grupo, string Icono, string Ruta, string[] Roles,
    bool Fija = false, bool CoincidenciaExacta = false)
{
    public string Politica => "Tarea:" + Clave;

    /// <summary>Submenú: opciones con el mismo permiso que la tarea (vacío = la tarea es una opción directa).</summary>
    public IReadOnlyList<OpcionMenu> Opciones { get; init; } = [];
}

/// <summary>Opción de un submenú del menú lateral.</summary>
public sealed record OpcionMenu(string Nombre, string Icono, string Ruta, bool CoincidenciaExacta = false);

/// <summary>
/// Catálogo de tareas (menú). Una tarea se permite si el rol del usuario la incluye y no se la quitaron en
/// Configuración → Usuarios (tabla usuario_tareas_denegadas). El Administrador tiene todas.
/// </summary>
public static class Tareas
{
    private static readonly string[] Todos = Roles.Todos;
    private static readonly string[] Operativos = [Roles.Administrador, Roles.Supervisor, Roles.Contador];

    public static readonly IReadOnlyList<Tarea> Catalogo =
    [
        new("resumen", "Resumen", "Consulta", "bi-grid-1x2-fill", "", Todos, Fija: true, CoincidenciaExacta: true),
        new("cfdi-ingresos", "CFDI de ingresos", "Consulta", "bi-file-earmark-arrow-up-fill", "cfdi/emitidos", Todos),
        new("cfdi-egresos", "CFDI de egresos", "Consulta", "bi-file-earmark-arrow-down-fill", "cfdi/recibidos", Todos),
        new("administracion-xml", "Administración de XML", "Operación", "bi-folder2-open", "administracion-xml", Todos)
        {
            Opciones =
            [
                new("Todos", "bi-files", "administracion-xml", CoincidenciaExacta: true),
                new("Visor de nóminas", "bi-person-vcard", "administracion-xml/nominas")
            ]
        },
        new("importaciones", "Importaciones", "Operación", "bi-cloud-arrow-up-fill", "importaciones", Operativos),
        new("exportaciones", "Exportaciones", "Consulta", "bi-file-earmark-excel-fill", "exportaciones", Todos),
        new("exportacion-sinube", "Importación a SINUBE", "SINUBE", "bi-send-fill", "exportacion-sinube", Operativos),
        new("conciliacion-sinube", "Conciliación SINUBE", "SINUBE", "bi-arrow-left-right", "conciliacion-sinube", Operativos),
        new("efos", "EFOS / EDOS", "Consulta", "bi-shield-exclamation", "efos", Todos, CoincidenciaExacta: true),
        new("xml-azulejeras", "XML Azulejeras (Clientes)","Utilerías", "bi-file-earmark-zip-fill", "utilerias/xmlazulejeras", Todos),
        new("descargas-sat", "Descargas SAT", "Operación", "bi-cloud-arrow-down-fill", "sat-descargas", Operativos),
        new("empresas", "Empresas", "Configuración", "bi-building", "configuracion/empresas", [Roles.Administrador, Roles.Supervisor]),
        new("usuarios", "Usuarios", "Configuración", "bi-people-fill", "configuracion/usuarios", [Roles.Administrador]),
        new("correo", "Correo", "Configuración", "bi-envelope-at-fill", "configuracion/correo", [Roles.Administrador]),
        new("herramientas", "Herramientas", "Configuración", "bi-tools", "configuracion/herramientas", [Roles.Administrador])
    ];

    /// <summary>Grupo de las herramientas de Administración de XML en Configuración → Usuarios.</summary>
    public const string GrupoHerramientasXml = "Herramientas de XML";

    /// <summary>
    /// Botones del panel de herramientas de Administración de XML, en su orden. No son opciones del menú lateral:
    /// se asignan o quitan por usuario igual que las tareas, y la página solo muestra los permitidos.
    /// </summary>
    public static readonly IReadOnlyList<Tarea> HerramientasXml =
    [
        new("xml-exportar-excel", "Exportar a Excel", GrupoHerramientasXml, "bi-file-earmark-excel", "", Todos),
        new("xml-imagen-resumen", "Imagen del resumen", GrupoHerramientasXml, "bi-image", "", Todos),
        new("xml-enviar-correo", "Enviar por correo", GrupoHerramientasXml, "bi-envelope-paper", "", Todos),
        new("xml-enviar-cfdi", "Enviar CFDI", GrupoHerramientasXml, "bi-send", "", Todos),
        new("xml-abrir-xml", "Abrir XML", GrupoHerramientasXml, "bi-filetype-xml", "", Todos),
        new("xml-generar-pdf", "Generar PDF", GrupoHerramientasXml, "bi-file-earmark-pdf", "", Todos),
        new("xml-validar-sat", "Valida Estado SAT", GrupoHerramientasXml, "bi-shield-check", "", Todos),
        new("xml-localizar", "Localizar XML", GrupoHerramientasXml, "bi-search", "", Todos),
        new("xml-importar", "Importar XML", GrupoHerramientasXml, "bi-upload", "", Todos),
        new("xml-zip-xml", "Descargar XML (ZIP)", GrupoHerramientasXml, "bi-file-earmark-zip", "", Todos),
        new("xml-zip-pdf", "Descargar PDF (ZIP)", GrupoHerramientasXml, "bi-file-earmark-zip", "", Todos),
        new("xml-agrupaciones", "Agrupaciones", GrupoHerramientasXml, "bi-diagram-3", "", Todos),
        new("xml-renombrar", "Renombrar XML's", GrupoHerramientasXml, "bi-input-cursor-text", "", Todos),
        new("xml-logos", "Catálogo de logos", GrupoHerramientasXml, "bi-images", "", Todos),
        new("xml-series", "Verifica series", GrupoHerramientasXml, "bi-123", "", Todos),
        new("xml-complemento", "Leer complemento", GrupoHerramientasXml, "bi-braces", "", Todos),
        new("xml-externos", "XML externos", GrupoHerramientasXml, "bi-box-arrow-in-down", "", Todos),
        new("xml-direccion-datos", "Dirección de datos", GrupoHerramientasXml, "bi-hdd-network", "", Todos)
    ];

    /// <summary>Grupo de los reportes de "Nvo. reporte de" de Administración de XML en Configuración → Usuarios.</summary>
    public const string GrupoReportesXml = "Reportes de XML";

    /// <summary>
    /// Reportes de la lista "Nvo. reporte de" (Exportar a Excel) de Administración de XML. La ruta es el tipo de
    /// reporte que recibe AdministracionXmlService.GenerarExcel; "agrupado:contraparte" es por emisor en recibidos y
    /// por receptor en emitidos.
    /// </summary>
    public static readonly IReadOnlyList<Tarea> ReportesXml =
    [
        new("xml-rep-general", "Reporte general (por tipo)", GrupoReportesXml, "bi-table", "general", Todos),
        new("xml-rep-conceptos", "Conceptos", GrupoReportesXml, "bi-list-ul", "conceptos", Todos),
        new("xml-rep-contraparte", "Agrupado por emisor / receptor", GrupoReportesXml, "bi-people", "agrupado:contraparte", Todos),
        new("xml-rep-mes", "Agrupado por mes", GrupoReportesXml, "bi-calendar3", "agrupado:mes", Todos),
        new("xml-rep-uso", "Uso CFDI", GrupoReportesXml, "bi-tag", "agrupado:uso", Todos),
        new("xml-rep-metodo", "Método de pago", GrupoReportesXml, "bi-credit-card", "agrupado:metodo", Todos),
        new("xml-rep-tipo", "Tipo", GrupoReportesXml, "bi-collection", "agrupado:tipo", Todos),
        new("xml-rep-pareto", "Análisis 80-20 (Pareto) con dashboard", GrupoReportesXml, "bi-bar-chart", "pareto", Todos)
    ];

    /// <summary>Grupos de Administración de XML que Configuración → Usuarios muestra aparte del menú.</summary>
    public static readonly IReadOnlyList<(string Grupo, string Icono)> GruposAdministracionXml =
        [(GrupoHerramientasXml, "bi-tools"), (GrupoReportesXml, "bi-file-earmark-bar-graph")];

    /// <summary>Menú, herramientas y reportes: todo lo que se puede asignar o quitar a un usuario.</summary>
    private static readonly IReadOnlyList<Tarea> Todas = [.. Catalogo, .. HerramientasXml, .. ReportesXml];

    /// <summary>
    /// Lo que un usuario nuevo (o que cambia a ese rol) empieza sin tener: a Consulta no se le dan de inicio las
    /// herramientas que modifican archivos o datos o envían CFDI a terceros; se le pueden marcar después.
    /// </summary>
    public static IEnumerable<string> DenegadasPorOmision(string rol) =>
        rol == Roles.Consulta
            ? ["xml-importar", "xml-externos", "xml-renombrar", "xml-logos", "xml-validar-sat", "xml-enviar-cfdi"]
            : [];

    private static readonly Dictionary<string, Tarea> PorClave = Todas.ToDictionary(t => t.Clave);

    public static Tarea Obtener(string clave) => PorClave[clave];

    public static string Politica(string clave) => PorClave[clave].Politica;

    /// <summary>Tareas que se pueden quitar a un usuario con ese rol (las que el rol permite y no son fijas).</summary>
    public static IEnumerable<Tarea> AsignablesPara(string rol) =>
        rol == Roles.Administrador ? [] : Todas.Where(t => !t.Fija && t.Roles.Contains(rol));

    public static void Registrar(AuthorizationOptions opciones)
    {
        foreach (Tarea t in Todas)
            opciones.AddPolicy(t.Politica, p => p.RequireAuthenticatedUser().AddRequirements(new TareaRequisito(t)));
    }
}

public sealed record TareaRequisito(Tarea Tarea) : IAuthorizationRequirement;

/// <summary>Tareas quitadas a cada usuario, en memoria 1 min (se invalida al guardar en Usuarios).</summary>
public sealed class TareasUsuarioCache(IMemoryCache cache, IServiceScopeFactory scopes)
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(1);

    public IReadOnlySet<string> Denegadas(long usuarioId) =>
        cache.GetOrCreate(Llave(usuarioId), entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Vigencia;
            using IServiceScope scope = scopes.CreateScope();
            return (IReadOnlySet<string>)scope.ServiceProvider.GetRequiredService<PostgreSqlRepository>()
                .ObtenerTareasDenegadas(usuarioId).ToHashSet();
        })!;

    public void Invalidar(long usuarioId) => cache.Remove(Llave(usuarioId));

    private static string Llave(long usuarioId) => $"tareas-denegadas:{usuarioId}";
}

public sealed class TareaHandler(TareasUsuarioCache cache) : AuthorizationHandler<TareaRequisito>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TareaRequisito requisito)
    {
        ClaimsPrincipal usuario = context.User;

        if (usuario.IsInRole(Roles.Administrador))
        {
            context.Succeed(requisito);
        }
        else if (requisito.Tarea.Roles.Any(usuario.IsInRole) &&
                 (requisito.Tarea.Fija ||
                  AccesoEmpresasService.ObtenerUsuarioId(usuario) is long id && !cache.Denegadas(id).Contains(requisito.Tarea.Clave)))
        {
            context.Succeed(requisito);
        }

        return Task.CompletedTask;
    }
}
