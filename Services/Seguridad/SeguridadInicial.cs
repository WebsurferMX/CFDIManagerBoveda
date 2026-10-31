using System.Security.Cryptography;
using CFDIParser.Models;
using CFDIParser.Services;
using Microsoft.AspNetCore.Identity;

namespace CFDIManager.Web.Services.Seguridad;

public static class SeguridadInicial
{
    /// <summary>
    /// Crea las tablas de seguridad y, si todavía no hay usuarios, el administrador inicial.
    /// Usuario y contraseña salen de Seguridad:AdministradorInicial (user secrets o variables
    /// de entorno); sin contraseña configurada se genera una y se escribe una sola vez en el log.
    /// </summary>
    public static async Task AsegurarAsync(IServiceProvider servicios, IConfiguration configuracion, ILogger logger)
    {
        var repo = servicios.GetRequiredService<PostgreSqlRepository>();
        repo.AsegurarSeguridad();

        if (repo.ContarUsuarios() > 0)
            return;

        string nombreUsuario = configuracion["Seguridad:AdministradorInicial:Usuario"] is { Length: > 0 } u ? u : "admin";
        string? password = configuracion["Seguridad:AdministradorInicial:Password"];
        bool generada = string.IsNullOrWhiteSpace(password);

        if (generada)
            password = $"Adm{RandomNumberGenerator.GetHexString(12, lowercase: true)}7";

        var userManager = servicios.GetRequiredService<UserManager<UsuarioDto>>();
        var administrador = new UsuarioDto { NombreUsuario = nombreUsuario, NombreCompleto = "Administrador" };

        IdentityResult resultado = await userManager.CreateAsync(administrador, password!);

        if (resultado.Succeeded)
            resultado = await userManager.AddToRoleAsync(administrador, Roles.Administrador);

        if (!resultado.Succeeded)
        {
            logger.LogError("No se pudo crear el administrador inicial: {Errores}",
                string.Join(" ", resultado.Errors.Select(e => e.Description)));
            return;
        }

        if (generada)
            logger.LogWarning(
                "Se creó el administrador inicial '{Usuario}' con la contraseña '{Password}'. Cámbiala en Configuración → Usuarios.",
                nombreUsuario, password);
        else
            logger.LogInformation("Se creó el administrador inicial '{Usuario}'.", nombreUsuario);
    }
}
