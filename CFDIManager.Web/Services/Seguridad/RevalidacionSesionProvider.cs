using System.Security.Claims;
using CFDIParser.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CFDIManager.Web.Services.Seguridad;

// Un circuito de Blazor puede vivir horas: cada pocos minutos se vuelve a revisar que el usuario
// siga activo y que su sello de seguridad no haya cambiado (cambio de rol, contraseña o baja).
// Si no es válido, la sesión del circuito se cierra y el usuario vuelve al login.
public sealed class RevalidacionSesionProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> opciones)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UsuarioDto>>();

        UsuarioDto? usuario = await userManager.GetUserAsync(authenticationState.User);

        if (usuario is null || !usuario.Activo)
            return false;

        string? selloSesion = authenticationState.User.FindFirstValue(opciones.Value.ClaimsIdentity.SecurityStampClaimType);

        return selloSesion == await userManager.GetSecurityStampAsync(usuario);
    }
}
