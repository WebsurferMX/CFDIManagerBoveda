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
}

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
        new("administracion-xml", "Administración de XML", "Operación", "bi-folder2-open", "administracion-xml", Operativos),
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

    private static readonly Dictionary<string, Tarea> PorClave = Catalogo.ToDictionary(t => t.Clave);

    public static Tarea Obtener(string clave) => PorClave[clave];

    public static string Politica(string clave) => PorClave[clave].Politica;

    /// <summary>Tareas que se pueden quitar a un usuario con ese rol (las que el rol permite y no son fijas).</summary>
    public static IEnumerable<Tarea> AsignablesPara(string rol) =>
        rol == Roles.Administrador ? [] : Catalogo.Where(t => !t.Fija && t.Roles.Contains(rol));

    public static void Registrar(AuthorizationOptions opciones)
    {
        foreach (Tarea t in Catalogo)
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
