using CFDIManager.Web.Models;
using CFDIManager.Web.Services.Interfaces;
using CFDIParser.Services;


namespace CFDIManager.Web.Services;

public class SatAuthService : ISatAuthService
{
    private readonly PostgreSqlRepository _repository;
    private readonly IWebHostEnvironment _env;

    public SatAuthService(
        PostgreSqlRepository repository,
        IWebHostEnvironment env)
    {
        _repository = repository;
        _env = env;
    }

    public async Task<SatAuthResult> ValidarAsync(long empresaId)
    {
        var resultado = new SatAuthResult();

        var accesoSat = _repository.ObtenerAccesoSat(empresaId);

        if (accesoSat == null)
        {
            resultado.Errores.Add(
                "La empresa no tiene configuración SAT.");
            return resultado;
        }

        if (string.IsNullOrWhiteSpace(accesoSat.PasswordKey))
        {
            resultado.Errores.Add(
                "No existe contraseña de la llave privada.");
        }

        if (string.IsNullOrWhiteSpace(accesoSat.RutaCer))
        {
            resultado.Errores.Add(
                "No existe ruta del certificado.");
        }

        if (string.IsNullOrWhiteSpace(accesoSat.RutaKey))
        {
            resultado.Errores.Add(
                "No existe ruta de la llave privada.");
        }

        if (!string.IsNullOrWhiteSpace(accesoSat.RutaCer))
        {
            if (!File.Exists(accesoSat.RutaCer))
            {
                resultado.Errores.Add(
                    $"No existe el archivo CER: {accesoSat.RutaCer}");
            }
            else
            {
                resultado.RutaCer = accesoSat.RutaCer;
            }
        }

        if (!string.IsNullOrWhiteSpace(accesoSat.RutaKey))
        {
            if (!File.Exists(accesoSat.RutaKey))
            {
                resultado.Errores.Add(
                    $"No existe el archivo KEY: {accesoSat.RutaKey}");
            }
            else
            {
                resultado.RutaKey = accesoSat.RutaKey;
            }
        }

        resultado.PasswordKey = accesoSat.PasswordKey;

        resultado.ConfiguracionCompleta =
            !resultado.Errores.Any();

        return resultado;
    }
}