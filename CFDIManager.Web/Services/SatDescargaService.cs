using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CFDIManager.Web.Models;
using CFDIManager.Web.Services.Interfaces;
using CFDIManager.Web.Services.Sat;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services;

/// <summary>
/// Descarga masiva del SAT con la e.firma configurada en la empresa
/// (Empresas → Acceso SAT). Los XML descargados entran por la misma
/// importación que la pantalla de Importaciones.
/// </summary>
public class SatDescargaService : ISatDescargaService
{
    private static readonly SemaphoreSlim CandadoArchivo =
        new(1, 1);

    private static readonly JsonSerializerOptions OpcionesJson =
        new() { WriteIndented = true };

    private static readonly Dictionary<string, string> TiposComprobante =
        new()
        {
            ["I"] = "Ingreso",
            ["E"] = "Egreso",
            ["T"] = "Traslado",
            ["N"] = "Nómina",
            ["P"] = "Pago"
        };

    private readonly PostgreSqlRepository _repo;
    private readonly IWebHostEnvironment _ambiente;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ImportacionCfdiService _importacion;
    private readonly FielPasswordProtector _passwordFiel;

    public SatDescargaService(
        PostgreSqlRepository repo,
        IWebHostEnvironment ambiente,
        IHttpClientFactory httpFactory,
        ImportacionCfdiService importacion,
        FielPasswordProtector passwordFiel)
    {
        _repo = repo;
        _ambiente = ambiente;
        _httpFactory = httpFactory;
        _importacion = importacion;
        _passwordFiel = passwordFiel;
    }

    public Task<SatEstadoDto> ValidarConfiguracionAsync(
        EmpresaDto empresa)
    {
        EmpresaSatDto? sat =
            _repo.ObtenerAccesoSat(empresa.EmpresaId);

        if (sat is null)
        {
            return Task.FromResult(new SatEstadoDto
            {
                Mensaje = "La empresa no tiene configurada su e.firma (Empresas → Acceso SAT)."
            });
        }

        string rutaCer = ResolverRuta(sat.RutaCer);
        string rutaKey = ResolverRuta(sat.RutaKey);

        var estado = new SatEstadoDto
        {
            TieneConfiguracion = true,
            RutaCer = rutaCer,
            RutaKey = rutaKey,
            ExisteCer = rutaCer != "" && File.Exists(rutaCer),
            ExisteKey = rutaKey != "" && File.Exists(rutaKey)
        };

        if (!estado.ExisteCer || !estado.ExisteKey)
        {
            estado.Mensaje = "Falta el archivo .cer o .key de la e.firma.";
            return Task.FromResult(estado);
        }

        try
        {
            using FielSat fiel =
                FielSat.Abrir(rutaCer, rutaKey, _passwordFiel.Descifrar(sat.PasswordKey) ?? "");

            estado.LlaveValida = true;
            estado.FechaVencimiento = fiel.VigenteHasta;
            estado.Vigente = fiel.EstaVigente;
            estado.EsFiel = fiel.EsFiel;
            estado.RfcCertificado = fiel.Rfc;
            estado.RfcCoincide = string.Equals(fiel.Rfc, empresa.Rfc.Trim(), StringComparison.OrdinalIgnoreCase);

            estado.Mensaje =
                !estado.EsFiel ? "El certificado es un sello digital (CSD); la descarga masiva requiere la e.firma (FIEL)."
                : !estado.RfcCoincide ? $"La e.firma es del RFC {fiel.Rfc}, no de {empresa.Rfc}."
                : !estado.Vigente ? $"La e.firma venció el {fiel.VigenteHasta:dd/MM/yyyy}."
                : "e.firma lista para descargar del SAT.";
        }
        catch (Exception ex)
        {
            estado.Mensaje = ex.Message;
        }

        return Task.FromResult(estado);
    }

    public async Task<DateTime> ProbarConexionAsync(
        EmpresaDto empresa,
        CancellationToken cancelacion = default)
    {
        using FielSat fiel = AbrirFiel(empresa);

        return await CrearCliente(fiel).AutenticarAsync(cancelacion);
    }

    public async Task<SolicitudSatDto> CrearSolicitudAsync(
        EmpresaDto empresa,
        ParametrosSolicitudSat p,
        CancellationToken cancelacion = default,
        bool automatica = false,
        string origen = "")
    {
        if (p.FechaFinal < p.FechaInicial)
        {
            throw new InvalidOperationException("La fecha final debe ser posterior a la inicial.");
        }

        if (p.FechaInicial > DateTime.Now)
        {
            throw new InvalidOperationException("La fecha inicial no puede ser futura.");
        }

        using FielSat fiel = AbrirFiel(empresa);

        ResultadoSolicitudSat r =
            await CrearCliente(fiel).SolicitarAsync(p, cancelacion);

        var solicitud = new SolicitudSatDto
        {
            EmpresaId = empresa.EmpresaId,
            Rfc = fiel.Rfc,
            IdSolicitudSat = r.IdSolicitud,
            Creada = DateTime.Now,
            Actualizada = DateTime.Now,
            FechaInicial = p.FechaInicial,
            FechaFinal = p.FechaFinal,
            Recibidos = p.Recibidos,
            Metadatos = p.Metadatos,
            Automatica = automatica,
            Origen = origen,
            TipoComprobante = p.TipoComprobante,
            RfcContraparte = (p.RfcContraparte ?? "").Trim().ToUpperInvariant(),
            Estado = r.Aceptada ? "Aceptada" : "Rechazada",
            Mensaje = $"{r.CodEstatus} {r.Mensaje}".Trim()
        };

        await GuardarAsync(solicitud);

        return solicitud;
    }

    public async Task<SolicitudSatDto> CrearSolicitudPorUuidAsync(
        EmpresaDto empresa,
        string uuid,
        CancellationToken cancelacion = default)
    {
        if (!Guid.TryParse(uuid, out _))
        {
            throw new InvalidOperationException("El UUID no es válido.");
        }

        using FielSat fiel = AbrirFiel(empresa);

        ResultadoSolicitudSat r =
            await CrearCliente(fiel).SolicitarPorUuidAsync(uuid, cancelacion);

        var solicitud = new SolicitudSatDto
        {
            EmpresaId = empresa.EmpresaId,
            Rfc = fiel.Rfc,
            IdSolicitudSat = r.IdSolicitud,
            Creada = DateTime.Now,
            Actualizada = DateTime.Now,
            Uuid = uuid.Trim().ToUpperInvariant(),
            Estado = r.Aceptada ? "Aceptada" : "Rechazada",
            Mensaje = $"{r.CodEstatus} {r.Mensaje}".Trim()
        };

        await GuardarAsync(solicitud);

        return solicitud;
    }

    public async Task<SolicitudSatDto> VerificarAsync(
        EmpresaDto empresa,
        string solicitudId,
        CancellationToken cancelacion = default)
    {
        SolicitudSatDto solicitud = await ObtenerAsync(empresa.EmpresaId, solicitudId);

        if (solicitud.IdSolicitudSat == "")
        {
            throw new InvalidOperationException("La solicitud no tiene identificador del SAT.");
        }

        using FielSat fiel = AbrirFiel(empresa);

        ResultadoVerificacionSat v =
            await CrearCliente(fiel).VerificarAsync(solicitud.IdSolicitudSat, cancelacion);

        if (!v.Aceptada)
        {
            solicitud.Mensaje = $"{v.CodEstatus} {v.Mensaje}".Trim();
        }
        else
        {
            solicitud.Estado = v.EstadoTexto;
            solicitud.NumeroCfdis = v.NumeroCfdis;
            solicitud.Mensaje = $"{v.CodigoEstadoSolicitud} {v.Mensaje}".Trim();

            if (v.EstadoSolicitud == 3)
            {
                solicitud.Paquetes = v.Paquetes;
            }
        }

        solicitud.Actualizada = DateTime.Now;

        await GuardarAsync(solicitud);

        return solicitud;
    }

    public async Task<SolicitudSatDto> DescargarAsync(
        EmpresaDto empresa,
        string solicitudId,
        IProgress<string>? progreso = null,
        CancellationToken cancelacion = default,
        IReadOnlySet<string>? soloUuids = null,
        bool soloNuevos = false)
    {
        SolicitudSatDto solicitud = await ObtenerAsync(empresa.EmpresaId, solicitudId);

        if (solicitud.Estado != "Terminada")
        {
            throw new InvalidOperationException("Verifica la solicitud hasta que el SAT la marque como Terminada.");
        }

        using FielSat fiel = AbrirFiel(empresa);

        SatDescargaMasivaCliente cliente = CrearCliente(fiel);

        string carpetaPaquetes =
            Path.Combine(CarpetaEmpresa(empresa.EmpresaId), "Paquetes");

        Directory.CreateDirectory(carpetaPaquetes);

        foreach (string paquete in solicitud.Paquetes.Except(solicitud.PaquetesDescargados).ToList())
        {
            progreso?.Report($"Descargando paquete {solicitud.PaquetesDescargados.Count + 1} de {solicitud.Paquetes.Count}…");

            ResultadoDescargaSat d =
                await cliente.DescargarPaqueteAsync(paquete, cancelacion);

            if (!d.Aceptada || d.Paquete.Length == 0)
            {
                solicitud.Mensaje = $"Paquete {paquete}: {d.CodEstatus} {d.Mensaje}".Trim();
                await GuardarAsync(solicitud);
                throw new InvalidOperationException(solicitud.Mensaje);
            }

            string rutaZip = Path.Combine(carpetaPaquetes, $"{paquete}.zip");

            await File.WriteAllBytesAsync(rutaZip, d.Paquete, cancelacion);

            if (!solicitud.Metadatos)
            {
                ImportarPaquete(empresa, solicitud, rutaZip, progreso, soloUuids, soloNuevos);
            }

            solicitud.PaquetesDescargados.Add(paquete);
            solicitud.Actualizada = DateTime.Now;

            await GuardarAsync(solicitud);
        }

        solicitud.Estado = "Descargada";
        solicitud.Mensaje = solicitud.Metadatos
            ? $"{LeerMetadatos(empresa, solicitud.Id).Count} registros de metadatos"
            : $"{solicitud.Importados + solicitud.Actualizados + solicitud.Omitidos + solicitud.Errores} descargados: " +
              $"{solicitud.Importados} nuevos, {solicitud.Actualizados} ya existían (actualizados), " +
              $"{solicitud.Omitidos} omitidos, {solicitud.Errores} con error";
        solicitud.Actualizada = DateTime.Now;

        await GuardarAsync(solicitud);

        return solicitud;
    }

    public async Task<List<SolicitudSatDto>> ListarSolicitudesAsync(
        long empresaId)
    {
        return (await LeerTodasAsync(empresaId))
            .OrderByDescending(s => s.Creada)
            .ToList();
    }

    public async Task<SolicitudSatDto> CancelarAsync(
        long empresaId,
        string solicitudId)
    {
        SolicitudSatDto solicitud = await ObtenerAsync(empresaId, solicitudId);

        if (!solicitud.Cancelable)
        {
            throw new InvalidOperationException($"Una solicitud {solicitud.Estado.ToLowerInvariant()} ya no se puede cancelar.");
        }

        solicitud.Estado = "Cancelada";
        solicitud.Mensaje = "Cancelada en CFDI Manager; ya no se verifica ni se descarga." +
                            (solicitud.PaquetesDescargados.Count > 0 ? $" {solicitud.Mensaje}" : "");
        solicitud.Actualizada = DateTime.Now;

        await GuardarAsync(solicitud);

        return solicitud;
    }

    public Task EliminarAsync(
        long empresaId,
        string solicitudId) =>
        EliminarVariasAsync(empresaId, [solicitudId]);

    public async Task<int> EliminarVariasAsync(
        long empresaId,
        IReadOnlyCollection<string> solicitudIds)
    {
        string archivo = RutaSolicitudes(empresaId);

        if (!File.Exists(archivo) || solicitudIds.Count == 0)
        {
            return 0;
        }

        List<SolicitudSatDto> eliminadas;

        await CandadoArchivo.WaitAsync();

        try
        {
            List<SolicitudSatDto> todas =
                JsonSerializer.Deserialize<List<SolicitudSatDto>>(await File.ReadAllTextAsync(archivo)) ?? new();

            var ids = solicitudIds.ToHashSet();
            eliminadas = todas.Where(s => ids.Contains(s.Id)).ToList();

            if (eliminadas.Count == 0)
            {
                return 0;
            }

            todas.RemoveAll(s => ids.Contains(s.Id));

            string temporal = archivo + ".tmp";

            await File.WriteAllTextAsync(temporal, JsonSerializer.Serialize(todas, OpcionesJson));

            File.Move(temporal, archivo, overwrite: true);
        }
        finally
        {
            CandadoArchivo.Release();
        }

        string carpetaPaquetes = Path.Combine(CarpetaEmpresa(empresaId), "Paquetes");

        foreach (string paquete in eliminadas.SelectMany(s => s.Paquetes))
        {
            string zip = Path.Combine(carpetaPaquetes, $"{paquete}.zip");

            if (File.Exists(zip))
            {
                File.Delete(zip);
            }
        }

        return eliminadas.Count;
    }

    public List<SatCfdiDto> LeerMetadatos(
        EmpresaDto empresa,
        string solicitudId,
        bool marcarImportados = true)
    {
        string carpetaPaquetes =
            Path.Combine(CarpetaEmpresa(empresa.EmpresaId), "Paquetes");

        string archivo = RutaSolicitudes(empresa.EmpresaId);

        if (!File.Exists(archivo))
        {
            return new();
        }

        SolicitudSatDto? solicitud =
            (JsonSerializer.Deserialize<List<SolicitudSatDto>>(File.ReadAllText(archivo)) ?? new())
            .FirstOrDefault(s => s.Id == solicitudId);

        if (solicitud is null || !solicitud.Metadatos)
        {
            return new();
        }

        var resultado = new List<SatCfdiDto>();

        foreach (string paquete in solicitud.PaquetesDescargados)
        {
            string rutaZip = Path.Combine(carpetaPaquetes, $"{paquete}.zip");

            if (!File.Exists(rutaZip))
            {
                continue;
            }

            using ZipArchive zip = ZipFile.OpenRead(rutaZip);

            foreach (ZipArchiveEntry entrada in zip.Entries.Where(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
            {
                using var lector = new StreamReader(entrada.Open(), Encoding.UTF8);

                resultado.AddRange(LeerArchivoMetadatos(lector.ReadToEnd()));
            }
        }

        if (marcarImportados)
        {
            foreach (SatCfdiDto cfdi in resultado)
            {
                cfdi.YaImportado = _repo.ExisteCfdi(empresa.EmpresaId, cfdi.Uuid);
            }
        }

        return resultado;
    }

    private void ImportarPaquete(
        EmpresaDto empresa,
        SolicitudSatDto solicitud,
        string rutaZip,
        IProgress<string>? progreso,
        IReadOnlySet<string>? soloUuids = null,
        bool soloNuevos = false)
    {
        string carpetaTemporal =
            Path.Combine(_ambiente.ContentRootPath, "Storage", "Temporal", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(carpetaTemporal);

        try
        {
            using ZipArchive zip = ZipFile.OpenRead(rutaZip);

            // El SAT nombra cada XML del paquete con su UUID (en minúsculas): con soloUuids (en mayúsculas)
            // se omiten los demás sin leerlos.
            List<ZipArchiveEntry> xmls =
                zip.Entries
                    .Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .Where(e => soloUuids is null || soloUuids.Contains(Path.GetFileNameWithoutExtension(e.Name).ToUpperInvariant()))
                    .ToList();

            // Ventanas que se traslapan: lo que ya está en la bóveda ni se extrae ni se vuelve a importar.
            // Un solo query; la base guarda UUID en mayúsculas y minúsculas, por eso se compara en mayúsculas.
            HashSet<string>? existentes = soloNuevos ? _repo.ObtenerUuidsEmpresa(empresa.EmpresaId) : null;

            for (int i = 0; i < xmls.Count; i++)
            {
                ZipArchiveEntry entrada = xmls[i];

                if (existentes is not null && existentes.Contains(Path.GetFileNameWithoutExtension(entrada.Name).ToUpperInvariant()))
                {
                    solicitud.Actualizados++;
                    continue;
                }

                // Path.GetFileName evita rutas dentro del ZIP que salgan de la carpeta temporal.
                string rutaXml = Path.Combine(carpetaTemporal, Path.GetFileName(entrada.Name));

                entrada.ExtractToFile(rutaXml, overwrite: true);

                ResultadoImportacionCfdi r =
                    _importacion.Importar(empresa, rutaXml, entrada.Name);

                switch (r.Estatus)
                {
                    case "CORRECTO": solicitud.Importados++; break;
                    case "ACTUALIZADO": solicitud.Actualizados++; break;
                    case "OMITIDO": solicitud.Omitidos++; break;
                    default: solicitud.Errores++; break;
                }

                File.Delete(rutaXml);

                if (i % 25 == 0)
                {
                    progreso?.Report($"Importando XML {i + 1} de {xmls.Count}…");
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(carpetaTemporal, recursive: true);
            }
            catch
            {
                // La limpieza temporal no debe ocultar el resultado.
            }
        }
    }

    /// <summary>
    /// El archivo de metadatos del SAT separa columnas con "~" y renglones con CRLF;
    /// un campo puede contener LF suelto, por eso se separa solo por CRLF.
    /// </summary>
    private static IEnumerable<SatCfdiDto> LeerArchivoMetadatos(
        string contenido)
    {
        string[] renglones = contenido.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        if (renglones.Length == 0)
        {
            yield break;
        }

        string[] encabezado = renglones[0].Split('~');

        int Columna(string nombre) =>
            Array.FindIndex(encabezado, h => h.Trim().Equals(nombre, StringComparison.OrdinalIgnoreCase));

        int cUuid = Columna("Uuid"), cRfcEmisor = Columna("RfcEmisor"), cNombreEmisor = Columna("NombreEmisor"),
            cRfcReceptor = Columna("RfcReceptor"), cNombreReceptor = Columna("NombreReceptor"),
            cFecha = Columna("FechaEmision"), cMonto = Columna("Monto"), cEfecto = Columna("EfectoComprobante"),
            cEstatus = Columna("Estatus"), cCancelacion = Columna("FechaCancelacion");

        foreach (string renglon in renglones.Skip(1))
        {
            string[] c = renglon.Split('~');

            string Valor(int i) => i >= 0 && i < c.Length ? c[i].Trim() : "";

            if (Valor(cUuid) == "")
            {
                continue;
            }

            string efecto = Valor(cEfecto);

            yield return new SatCfdiDto
            {
                Uuid = Valor(cUuid).ToUpperInvariant(),
                RfcEmisor = Valor(cRfcEmisor),
                NombreEmisor = Valor(cNombreEmisor),
                RfcReceptor = Valor(cRfcReceptor),
                NombreReceptor = Valor(cNombreReceptor),
                Fecha = DateTime.TryParse(Valor(cFecha), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime f) ? f : default,
                Monto = decimal.TryParse(Valor(cMonto), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal m) ? m : 0,
                Tipo = TiposComprobante.GetValueOrDefault(efecto, efecto),
                Estatus = Valor(cEstatus) == "0" ? "Cancelado" : "Vigente",
                FechaCancelacion = DateTime.TryParse(Valor(cCancelacion), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fc) ? fc : null
            };
        }
    }

    private FielSat AbrirFiel(
        EmpresaDto empresa)
    {
        EmpresaSatDto sat =
            _repo.ObtenerAccesoSat(empresa.EmpresaId)
            ?? throw new InvalidOperationException("La empresa no tiene configurada su e.firma (Empresas → Acceso SAT).");

        FielSat fiel =
            FielSat.Abrir(ResolverRuta(sat.RutaCer), ResolverRuta(sat.RutaKey), _passwordFiel.Descifrar(sat.PasswordKey) ?? "");

        string? problema =
            !fiel.EsFiel ? "El certificado configurado es un sello digital (CSD); la descarga masiva requiere la e.firma (FIEL)."
            : !string.Equals(fiel.Rfc, empresa.Rfc.Trim(), StringComparison.OrdinalIgnoreCase) ? $"La e.firma es del RFC {fiel.Rfc}, no de {empresa.Rfc}."
            : !fiel.EstaVigente ? $"La e.firma venció el {fiel.VigenteHasta:dd/MM/yyyy}."
            : null;

        if (problema is not null)
        {
            fiel.Dispose();
            throw new InvalidOperationException(problema);
        }

        return fiel;
    }

    private SatDescargaMasivaCliente CrearCliente(
        FielSat fiel)
    {
        HttpClient http = _httpFactory.CreateClient("SatDescargaMasiva");

        return new SatDescargaMasivaCliente(fiel, http);
    }

    private string ResolverRuta(
        string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta))
        {
            return "";
        }

        return Path.IsPathRooted(ruta)
            ? ruta
            : Path.Combine(_ambiente.ContentRootPath, ruta);
    }

    private string CarpetaEmpresa(
        long empresaId)
    {
        return Path.Combine(_ambiente.ContentRootPath, "Storage", "SatDescargas", empresaId.ToString());
    }

    private string RutaSolicitudes(
        long empresaId)
    {
        return Path.Combine(CarpetaEmpresa(empresaId), "solicitudes.json");
    }

    private async Task<List<SolicitudSatDto>> LeerTodasAsync(
        long empresaId)
    {
        string archivo = RutaSolicitudes(empresaId);

        if (!File.Exists(archivo))
        {
            return new();
        }

        await CandadoArchivo.WaitAsync();

        try
        {
            return JsonSerializer.Deserialize<List<SolicitudSatDto>>(
                       await File.ReadAllTextAsync(archivo))
                   ?? new();
        }
        finally
        {
            CandadoArchivo.Release();
        }
    }

    private async Task<SolicitudSatDto> ObtenerAsync(
        long empresaId,
        string solicitudId)
    {
        return (await LeerTodasAsync(empresaId)).FirstOrDefault(s => s.Id == solicitudId)
               ?? throw new InvalidOperationException("La solicitud ya no existe.");
    }

    private async Task GuardarAsync(
        SolicitudSatDto solicitud)
    {
        string archivo = RutaSolicitudes(solicitud.EmpresaId);

        Directory.CreateDirectory(Path.GetDirectoryName(archivo)!);

        await CandadoArchivo.WaitAsync();

        try
        {
            List<SolicitudSatDto> todas =
                File.Exists(archivo)
                    ? JsonSerializer.Deserialize<List<SolicitudSatDto>>(await File.ReadAllTextAsync(archivo)) ?? new()
                    : new();

            todas.RemoveAll(s => s.Id == solicitud.Id);
            todas.Add(solicitud);

            // Escritura atómica: si falla a la mitad, el archivo anterior queda intacto.
            string temporal = archivo + ".tmp";

            await File.WriteAllTextAsync(temporal, JsonSerializer.Serialize(todas, OpcionesJson));

            File.Move(temporal, archivo, overwrite: true);
        }
        finally
        {
            CandadoArchivo.Release();
        }
    }
}
