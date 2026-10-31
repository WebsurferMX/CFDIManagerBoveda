using System.Security.Claims;
using CFDIParser.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CFDIManager.Web.Services.Seguridad;

// Agrega el nombre completo a la cookie para mostrarlo en la barra superior sin ir a la base.
public sealed class UsuarioClaimsFactory(
    UserManager<UsuarioDto> userManager,
    RoleManager<RolDto> roleManager,
    IOptions<IdentityOptions> opciones)
    : UserClaimsPrincipalFactory<UsuarioDto, RolDto>(userManager, roleManager, opciones)
{
    public const string NombreCompleto = "cfdi:nombre_completo";

    /// <summary>Presente mientras el usuario tenga una contraseña temporal (MainLayout lo manda a cambiarla).</summary>
    public const string DebeCambiarPassword = "cfdi:debe_cambiar_password";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(UsuarioDto user)
    {
        ClaimsIdentity identidad = await base.GenerateClaimsAsync(user);
        identidad.AddClaim(new Claim(NombreCompleto, user.NombreParaMostrar));

        if (user.DebeCambiarPassword)
            identidad.AddClaim(new Claim(DebeCambiarPassword, "1"));

        return identidad;
    }
}
