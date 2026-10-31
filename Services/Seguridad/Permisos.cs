using Microsoft.AspNetCore.Authorization;

namespace CFDIManager.Web.Services.Seguridad;

// Los roles se crean en la tabla roles (PostgreSqlRepository.AsegurarSeguridad).
public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Supervisor = "Supervisor";
    public const string Contador = "Contador";
    public const string Consulta = "Consulta";

    public static readonly string[] Todos = [Administrador, Supervisor, Contador, Consulta];
}

// Cada permiso es una política de autorización: [Authorize(Policy = Permisos.X)] en páginas
// y <AuthorizeView Policy="@Permisos.X"> en el menú y los botones.
public static class Permisos
{
    /// <summary>Alta de usuarios, roles y asignación de empresas.</summary>
    public const string AdministrarUsuarios = "AdministrarUsuarios";

    /// <summary>Configuración de empresas: datos fiscales, FIEL, CIEC, declaraciones.</summary>
    public const string AdministrarEmpresas = "AdministrarEmpresas";

    /// <summary>Importar XML, descargas del SAT, administración de XML y exportación a SINUBE.</summary>
    public const string OperarCfdi = "OperarCfdi";

    /// <summary>Resumen, consulta de CFDI, EFOS y reportes.</summary>
    public const string ConsultarCfdi = "ConsultarCfdi";

    public static void Registrar(AuthorizationOptions opciones)
    {
        opciones.AddPolicy(AdministrarUsuarios, p => p.RequireRole(Roles.Administrador));
        opciones.AddPolicy(AdministrarEmpresas, p => p.RequireRole(Roles.Administrador, Roles.Supervisor));
        opciones.AddPolicy(OperarCfdi, p => p.RequireRole(Roles.Administrador, Roles.Supervisor, Roles.Contador));
        opciones.AddPolicy(ConsultarCfdi, p => p.RequireRole(Roles.Todos));
    }
}
