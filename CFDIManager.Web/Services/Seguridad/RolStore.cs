using CFDIParser.Models;
using CFDIParser.Services;
using Microsoft.AspNetCore.Identity;

namespace CFDIManager.Web.Services.Seguridad;

// Store de roles de ASP.NET Identity sobre la tabla roles.
public sealed class RolStore(PostgreSqlRepository repo) : IRoleStore<RolDto>
{
    public Task<IdentityResult> CreateAsync(RolDto role, CancellationToken cancellationToken)
    {
        role.ConcurrencyStamp = Guid.NewGuid().ToString();
        role.RolId = repo.CrearRol(role);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(RolDto role, CancellationToken cancellationToken)
    {
        role.ConcurrencyStamp = Guid.NewGuid().ToString();
        repo.ActualizarRol(role);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> DeleteAsync(RolDto role, CancellationToken cancellationToken)
    {
        repo.EliminarRol(role.RolId);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<string> GetRoleIdAsync(RolDto role, CancellationToken cancellationToken) =>
        Task.FromResult(role.RolId.ToString());

    public Task<string?> GetRoleNameAsync(RolDto role, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(role.Nombre);

    public Task SetRoleNameAsync(RolDto role, string? roleName, CancellationToken cancellationToken)
    {
        role.Nombre = roleName ?? "";
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(RolDto role, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(role.NombreNormalizado);

    public Task SetNormalizedRoleNameAsync(RolDto role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NombreNormalizado = normalizedName ?? "";
        return Task.CompletedTask;
    }

    public Task<RolDto?> FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
        Task.FromResult(long.TryParse(roleId, out long id) ? repo.ObtenerRolPorId(id) : null);

    public Task<RolDto?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) =>
        Task.FromResult(repo.ObtenerRolPorNombre(normalizedRoleName));

    public void Dispose()
    {
    }
}
