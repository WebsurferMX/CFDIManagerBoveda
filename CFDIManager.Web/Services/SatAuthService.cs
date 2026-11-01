using CFDIManager.Web.Models;
using CFDIManager.Web.Services.Interfaces;
using CFDIManager.Web.Services.Sat;
using CFDIParser.Services;


namespace CFDIManager.Web.Services;

public class SatAuthService : ISatAuthService
{
    private readonly PostgreSqlRepository _repository;
    private readonly IWebHostEnvironment _env;
    private readonly CertificadosEmpresa _certificados;

    public SatAuthService(
        PostgreSqlRepository repository,
        IWebHostEnvironment env,
        CertificadosEmpresa certificados)
    {
        _repository = repository;
        _env = env;
        _certificados = certificados;
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
            if (!_certificados.Existe(accesoSat.RutaCer))
            {
                resultado.Errores.Add(
                    $"No existe el archivo CER: {accesoSat.RutaCer}");
            }
            else
            {
                resultado.RutaCer = _certificados.Resolver(accesoSat.RutaCer);
            }
        }

        if (!string.IsNullOrWhiteSpace(accesoSat.RutaKey))
        {
            if (!_certificados.Existe(accesoSat.RutaKey))
            {
                resultado.Errores.Add(
                    $"No existe el archivo KEY: {accesoSat.RutaKey}");
            }
            else
            {
                resultado.RutaKey = _certificados.Resolver(accesoSat.RutaKey);
            }
        }

        resultado.PasswordKey = accesoSat.PasswordKey;

        resultado.ConfiguracionCompleta =
            !resultado.Errores.Any();

        return resultado;
    }
}