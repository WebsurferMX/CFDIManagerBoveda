using System.Security.Claims;
using CFDIParser.Models;
using CFDIParser.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace CFDIManager.Web.Services.Seguridad;

// Qué empresas puede ver el usuario: el Administrador ve todas; los demás roles solo las
// que tienen asignadas en usuario_empresas (pantalla Configuración → Usuarios).
public class AccesoEmpresasService(PostgreSqlRepository repo, AuthenticationStateProvider autenticacion)
{
    public static long? ObtenerUsuarioId(ClaimsPrincipal usuario) =>
        long.TryParse(usuario.FindFirstValue(ClaimTypes.NameIdentifier), out long id) ? id : null;

    public static bool VeTodasLasEmpresas(ClaimsPrincipal usuario) =>
        usuario.Identity?.IsAuthenticated == true && usuario.IsInRole(Roles.Administrador);

    public List<EmpresaDto> ObtenerEmpresasPermitidas(ClaimsPrincipal usuario)
    {
        if (VeTodasLasEmpresas(usuario))
            return repo.ObtenerEmpresasActivas();

        return ObtenerUsuarioId(usuario) is long usuarioId
            ? repo.ObtenerEmpresasActivasDeUsuario(usuarioId)
            : [];
    }

    /// <summary>Ids permitidos; null significa todas (Administrador).</summary>
    public HashSet<long>? ObtenerIdsPermitidos(ClaimsPrincipal usuario)
    {
        if (VeTodasLasEmpresas(usuario))
            return null;

        return ObtenerUsuarioId(usuario) is long usuarioId
            ? repo.ObtenerEmpresasIdsDeUsuario(usuarioId).ToHashSet()
            : [];
    }

    public bool PuedeAcceder(ClaimsPrincipal usuario, long empresaId)
    {
        if (VeTodasLasEmpresas(usuario))
            return true;

        return ObtenerUsuarioId(usuario) is long usuarioId
            && repo.UsuarioTieneEmpresa(usuarioId, empresaId);
    }

    // Variantes para componentes de Blazor (toman al usuario del circuito).

    public async Task<ClaimsPrincipal> ObtenerUsuarioActualAsync() =>
        (await autenticacion.GetAuthenticationStateAsync()).User;

    public async Task<List<EmpresaDto>> ObtenerEmpresasPermitidasAsync() =>
        ObtenerEmpresasPermitidas(await ObtenerUsuarioActualAsync());

    public async Task<HashSet<long>?> ObtenerIdsPermitidosAsync() =>
        ObtenerIdsPermitidos(await ObtenerUsuarioActualAsync());
}
