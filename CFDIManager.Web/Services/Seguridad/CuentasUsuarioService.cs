using System.Security.Cryptography;
using CFDIManager.Web.Services.Correo;
using CFDIParser.Models;
using CFDIParser.Services;
using Microsoft.AspNetCore.Identity;

namespace CFDIManager.Web.Services.Seguridad;

// Operaciones de cuenta de Configuración → Usuarios que van más allá de guardar el formulario:
// contraseñas temporales, restablecer, eliminar y los correos de bienvenida / restablecimiento.
public sealed class CuentasUsuarioService(
    UserManager<UsuarioDto> userManager,
    PostgreSqlRepository repo,
    CorreoService correo)
{
    // Sin caracteres que se confunden al dictarlos o copiarlos (0/O, 1/l/I).
    private const string Mayusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Minusculas = "abcdefghijkmnpqrstuvwxyz";
    private const string Numeros = "23456789";

    /// <summary>Contraseña temporal tipo "Kxmt-Rbqe-47" que cumple la política de Identity.</summary>
    public static string GenerarPasswordTemporal() =>
        RandomNumberGenerator.GetString(Mayusculas, 1) +
        RandomNumberGenerator.GetString(Minusculas, 3) + "-" +
        RandomNumberGenerator.GetString(Mayusculas, 1) +
        RandomNumberGenerator.GetString(Minusculas, 3) + "-" +
        RandomNumberGenerator.GetString(Numeros, 2);

    /// <summary>Pone una contraseña temporal, desbloquea la cuenta y cierra sus sesiones abiertas.</summary>
    public async Task<string> RestablecerPasswordAsync(UsuarioDto usuario)
    {
        string temporal = GenerarPasswordTemporal();

        usuario.PasswordHash = userManager.PasswordHasher.HashPassword(usuario, temporal);
        usuario.DebeCambiarPassword = true;
        usuario.LockoutFin = null;
        usuario.AccesosFallidos = 0;

        Verificar(await userManager.UpdateSecurityStampAsync(usuario));

        return temporal;
    }

    /// <summary>Elimina el usuario (sus roles y empresas asignadas se borran en cascada).</summary>
    public async Task EliminarAsync(UsuarioDto usuario, long? usuarioActualId)
    {
        if (usuario.UsuarioId == usuarioActualId)
            throw new InvalidOperationException("No puedes eliminar tu propio usuario.");

        if (repo.ObtenerRolesDeUsuario(usuario.UsuarioId).Contains(Roles.Administrador) &&
            !repo.ObtenerUsuariosEnRol(Roles.Administrador.ToUpperInvariant())
                .Any(u => u.UsuarioId != usuario.UsuarioId && u.Activo))
        {
            throw new InvalidOperationException("No se puede eliminar al último Administrador activo.");
        }

        Verificar(await userManager.DeleteAsync(usuario));
    }

    public Task EnviarBienvenidaAsync(UsuarioDto usuario, string rol, string? passwordTemporal, string enviadoPor)
    {
        ConfiguracionCorreoDto configuracion = correo.ObtenerConfiguracion();
        List<string> empresas = EmpresasDe(usuario, rol);

        var datos = new List<(string, string)>
        {
            ("Usuario", usuario.NombreUsuario),
            ("Rol", rol),
            ("Empresas", empresas.Count == 0 ? "Pendientes de asignar" : string.Join(", ", empresas))
        };

        if (passwordTemporal is not null)
            datos.Insert(1, ("Contraseña temporal", passwordTemporal));

        (string html, string texto) = PlantillaCorreo.Construir(new PlantillaCorreo.Contenido
        {
            Titulo = "Te damos la bienvenida a CFDI Manager",
            Saludo = $"Hola {usuario.NombreParaMostrar}:",
            Parrafos =
            [
                "Se creó tu cuenta en CFDI Manager, la bóveda de comprobantes fiscales de la empresa. Estos son tus datos de acceso:"
            ],
            Datos = datos,
            DatosResaltados = passwordTemporal is null ? [] : [passwordTemporal, usuario.NombreUsuario],
            TextoBoton = "Entrar a CFDI Manager",
            UrlBoton = UrlLogin(configuracion),
            Nota = passwordTemporal is null
                ? "Tu contraseña te la proporcionará el administrador por un medio seguro."
                : "Por seguridad, al iniciar sesión por primera vez se te pedirá cambiar la contraseña temporal."
        });

        return correo.EnviarAsync(new MensajeCorreo
        {
            Tipo = "Bienvenida",
            Para = [usuario.Email],
            Asunto = "Tu acceso a CFDI Manager",
            CuerpoHtml = html,
            CuerpoTexto = texto,
            EnviadoPor = enviadoPor
        });
    }

    public Task EnviarRestablecimientoAsync(UsuarioDto usuario, string passwordTemporal, string enviadoPor)
    {
        (string html, string texto) = PlantillaCorreo.Construir(new PlantillaCorreo.Contenido
        {
            Titulo = "Se restableció tu contraseña",
            Saludo = $"Hola {usuario.NombreParaMostrar}:",
            Parrafos =
            [
                "El administrador restableció la contraseña de tu cuenta de CFDI Manager. Usa esta contraseña temporal para entrar:"
            ],
            Datos = [("Usuario", usuario.NombreUsuario), ("Contraseña temporal", passwordTemporal)],
            DatosResaltados = [usuario.NombreUsuario, passwordTemporal],
            TextoBoton = "Entrar a CFDI Manager",
            UrlBoton = UrlLogin(correo.ObtenerConfiguracion()),
            Nota = "Al iniciar sesión se te pedirá elegir una contraseña nueva. Tus sesiones abiertas se cerraron. Si tú no lo solicitaste, avisa al administrador."
        });

        return correo.EnviarAsync(new MensajeCorreo
        {
            Tipo = "Restablecimiento",
            Para = [usuario.Email],
            Asunto = "Restablecimiento de contraseña · CFDI Manager",
            CuerpoHtml = html,
            CuerpoTexto = texto,
            EnviadoPor = enviadoPor
        });
    }

    private List<string> EmpresasDe(UsuarioDto usuario, string rol)
    {
        if (rol == Roles.Administrador)
            return ["Todas"];

        HashSet<long> ids = repo.ObtenerEmpresasIdsDeUsuario(usuario.UsuarioId).ToHashSet();

        return repo.ObtenerEmpresasActivas()
            .Where(e => ids.Contains(e.EmpresaId))
            .Select(e => e.NombreParaMostrar)
            .ToList();
    }

    private static string? UrlLogin(ConfiguracionCorreoDto configuracion) =>
        string.IsNullOrWhiteSpace(configuracion.UrlAplicacion)
            ? null
            : configuracion.UrlAplicacion.TrimEnd('/') + "/cuenta/login";

    private static void Verificar(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
            throw new InvalidOperationException(string.Join(" ", resultado.Errors.Select(e => e.Description)));
    }
}
