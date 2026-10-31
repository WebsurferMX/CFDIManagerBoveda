using CFDIParser.Models;
using CFDIParser.Services;
using Microsoft.AspNetCore.Identity;

namespace CFDIManager.Web.Services.Seguridad;

// Store de ASP.NET Identity sobre las tablas usuarios / usuario_roles de PostgreSQL.
// El repositorio es síncrono, igual que en el resto de la aplicación.
public sealed class UsuarioStore(PostgreSqlRepository repo) :
    IUserPasswordStore<UsuarioDto>,
    IUserSecurityStampStore<UsuarioDto>,
    IUserRoleStore<UsuarioDto>,
    IUserLockoutStore<UsuarioDto>
{
    public Task<IdentityResult> CreateAsync(UsuarioDto user, CancellationToken cancellationToken)
    {
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        user.UsuarioId = repo.CrearUsuario(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(UsuarioDto user, CancellationToken cancellationToken)
    {
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        repo.ActualizarUsuario(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> DeleteAsync(UsuarioDto user, CancellationToken cancellationToken)
    {
        repo.EliminarUsuario(user.UsuarioId);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<UsuarioDto?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(long.TryParse(userId, out long id) ? repo.ObtenerUsuarioPorId(id) : null);

    public Task<UsuarioDto?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        Task.FromResult(repo.ObtenerUsuarioPorNombre(normalizedUserName));

    public Task<string> GetUserIdAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult(user.UsuarioId.ToString());

    public Task<string?> GetUserNameAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NombreUsuario);

    public Task SetUserNameAsync(UsuarioDto user, string? userName, CancellationToken cancellationToken)
    {
        user.NombreUsuario = userName ?? "";
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NombreUsuarioNormalizado);

    public Task SetNormalizedUserNameAsync(UsuarioDto user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NombreUsuarioNormalizado = normalizedName ?? "";
        return Task.CompletedTask;
    }

    // ---------- Contraseña y sello de seguridad ----------

    public Task SetPasswordHashAsync(UsuarioDto user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash is not null);

    public Task SetSecurityStampAsync(UsuarioDto user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult(user.SecurityStamp);

    // ---------- Roles ----------

    public Task AddToRoleAsync(UsuarioDto user, string roleName, CancellationToken cancellationToken)
    {
        repo.AgregarUsuarioARol(user.UsuarioId, roleName);
        return Task.CompletedTask;
    }

    public Task RemoveFromRoleAsync(UsuarioDto user, string roleName, CancellationToken cancellationToken)
    {
        repo.QuitarUsuarioDeRol(user.UsuarioId, roleName);
        return Task.CompletedTask;
    }

    public Task<IList<string>> GetRolesAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult<IList<string>>(repo.ObtenerRolesDeUsuario(user.UsuarioId));

    public Task<bool> IsInRoleAsync(UsuarioDto user, string roleName, CancellationToken cancellationToken) =>
        Task.FromResult(repo.ObtenerRolesDeUsuario(user.UsuarioId)
            .Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase)));

    public Task<IList<UsuarioDto>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        Task.FromResult<IList<UsuarioDto>>(repo.ObtenerUsuariosEnRol(roleName));

    // ---------- Bloqueo por intentos fallidos ----------

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutFin);

    public Task SetLockoutEndDateAsync(UsuarioDto user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutFin = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult(++user.AccesosFallidos);

    public Task ResetAccessFailedCountAsync(UsuarioDto user, CancellationToken cancellationToken)
    {
        user.AccesosFallidos = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccesosFallidos);

    public Task<bool> GetLockoutEnabledAsync(UsuarioDto user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutHabilitado);

    public Task SetLockoutEnabledAsync(UsuarioDto user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutHabilitado = enabled;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
