using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Components.Forms;

namespace CFDIManager.Web.Services;

public class EmpresaCertificadoService
{
    private readonly IWebHostEnvironment _env;

    public EmpresaCertificadoService(IWebHostEnvironment env)
    {
        _env = env;
    }

   
    public async Task<(string rutaCer,string rutaKey,DateTime? fechaVencimiento)>GuardarArchivosAsync(
    long empresaId,
    IBrowserFile? archivoCer,
    IBrowserFile? archivoKey)
    
    {
        var carpeta = Path.Combine(
            _env.ContentRootPath,
            "Storage",
            "Certificados",
            empresaId.ToString());

        Directory.CreateDirectory(carpeta);

        string rutaCer = "";
        string rutaKey = "";

        DateTime? fechaVencimiento = null;

        if (archivoCer != null)
        {
            rutaCer = Path.Combine(carpeta, "certificado.cer");

            await using var fs = new FileStream(
                rutaCer,
                FileMode.Create,
                FileAccess.Write);

            await archivoCer
                .OpenReadStream(long.MaxValue)
                .CopyToAsync(fs);

            fs.Close();

            using var certificado = X509CertificateLoader.LoadCertificateFromFile(rutaCer);

            fechaVencimiento = certificado.NotAfter;
        }

        if (archivoKey != null)
        {
            rutaKey = Path.Combine(carpeta, "llave.key");

            await using var fs = new FileStream(
                rutaKey,
                FileMode.Create,
                FileAccess.Write);

            await archivoKey
                .OpenReadStream(long.MaxValue)
                .CopyToAsync(fs);
        }

        return (rutaCer, rutaKey, fechaVencimiento);
    }
}