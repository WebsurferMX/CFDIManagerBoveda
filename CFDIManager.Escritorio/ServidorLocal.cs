using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CFDIManager.Escritorio;

/// <summary>Lo que se guarda de una instalación propia, en {CarpetaDatos}\escritorio.json (vale para todos los usuarios de la PC).</summary>
public sealed class Instalacion
{
    public int PuertoPostgres { get; set; }
    public int PuertoWeb { get; set; }

    /// <summary>Contraseña del usuario postgres de la base local, cifrada con DPAPI del equipo.</summary>
    public string PasswordPostgres { get; set; } = "";

    /// <summary>False hasta que la web arrancó una vez con el administrador que se capturó al instalar.</summary>
    public bool AdministradorCreado { get; set; }

    public DateTime Creada { get; set; }
}

/// <summary>
/// Modo Local: PostgreSQL portátil ({programa}\pgsql) con sus datos en {CarpetaDatos}\PostgreSQL y CFDIManager.Web
/// ({programa}\web) con --Modo=Escritorio y su Storage en {CarpetaDatos}\Storage, todo en 127.0.0.1.
/// </summary>
public sealed class ServidorLocal
{
    public const string BaseDatos = "cfdi_manager";
    private const string Locale = "Spanish_Mexico.1252"; // igual que la base del servidor
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("CFDIManagerEscritorio");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly string _datos;
    private readonly object _candadoLog = new();
    private Process? _web;
    private StreamWriter? _logWeb;
    private Uri? _uri;

    public ServidorLocal(string carpetaDatos) => _datos = Path.GetFullPath(carpetaDatos);

    /// <summary>pgsql junto al programa; para desarrollo, la variable CFDI_ESCRITORIO_PGSQL.</summary>
    public static string CarpetaPgsql =>
        Environment.GetEnvironmentVariable("CFDI_ESCRITORIO_PGSQL") is { Length: > 0 } ruta
            ? ruta
            : Path.Combine(AppContext.BaseDirectory, "pgsql");

    /// <summary>web junto al programa; para desarrollo, la variable CFDI_ESCRITORIO_WEB (carpeta publicada).</summary>
    public static string ExeWeb =>
        Path.Combine(
            Environment.GetEnvironmentVariable("CFDI_ESCRITORIO_WEB") is { Length: > 0 } ruta
                ? ruta
                : Path.Combine(AppContext.BaseDirectory, "web"),
            "CFDIManager.Web.exe");

    private string CarpetaPgData => Path.Combine(_datos, "PostgreSQL");
    private string CarpetaStorage => Path.Combine(_datos, "Storage");
    private string CarpetaLogs => Path.Combine(_datos, "Logs");
    private string LogPostgres => Path.Combine(CarpetaLogs, "postgresql.log");
    private string LogWeb => Path.Combine(CarpetaLogs, "web.log");
    private static string Bin(string exe) => Path.Combine(CarpetaPgsql, "bin", exe);
    private static string ArchivoInstalacion(string datos) => Path.Combine(datos, "escritorio.json");

    public static Instalacion? LeerInstalacion(string carpetaDatos)
    {
        try
        {
            string archivo = ArchivoInstalacion(Path.GetFullPath(carpetaDatos));
            return File.Exists(archivo) ? JsonSerializer.Deserialize<Instalacion>(File.ReadAllText(archivo)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Guardar(Instalacion instalacion) =>
        File.WriteAllText(ArchivoInstalacion(_datos), JsonSerializer.Serialize(instalacion, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>
    /// Arranca (o reutiliza) PostgreSQL y la web y regresa la dirección. La primera vez crea la base;
    /// el administrador inicial se pasa solo hasta que la web arrancó con él.
    /// </summary>
    public async Task<Uri> IniciarAsync(string? adminUsuario, string? adminPassword, IProgress<string> avance)
    {
        if (_web is { HasExited: false } && _uri is not null && await RespondeAsync(_uri))
            return _uri;

        if (!File.Exists(Bin("pg_ctl.exe")))
            throw new InvalidOperationException($"No se encontró PostgreSQL en {CarpetaPgsql}.");
        if (!File.Exists(ExeWeb))
            throw new InvalidOperationException($"No se encontró {ExeWeb}.");

        Directory.CreateDirectory(CarpetaStorage);
        Directory.CreateDirectory(CarpetaLogs);

        Instalacion instalacion = LeerInstalacion(_datos) ?? new Instalacion
        {
            PuertoPostgres = PuertoLibre(5433),
            PuertoWeb = PuertoLibre(5180),
            PasswordPostgres = Cifrar(RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789", 32)),
            Creada = DateTime.Now
        };

        string passwordPostgres = Descifrar(instalacion.PasswordPostgres);

        if (!File.Exists(Path.Combine(CarpetaPgData, "PG_VERSION")))
        {
            avance.Report("Creando la base de datos…");
            await CrearClusterAsync(passwordPostgres);
        }

        Guardar(instalacion);

        avance.Report("Iniciando PostgreSQL…");
        await IniciarPostgresAsync(instalacion);

        DetenerWeb();
        if (!PuertoDisponible(instalacion.PuertoWeb))
        {
            instalacion.PuertoWeb = PuertoLibre(instalacion.PuertoWeb + 1);
            Guardar(instalacion);
        }

        avance.Report(instalacion.AdministradorCreado ? "Iniciando CFDI Manager…" : "Preparando la base de datos…");

        // Solo para pruebas sin el asistente: CFDI_ESCRITORIO_ADMIN_PASSWORD.
        if (!instalacion.AdministradorCreado && adminPassword is null &&
            Environment.GetEnvironmentVariable("CFDI_ESCRITORIO_ADMIN_PASSWORD") is { Length: > 0 } passwordPrueba)
        {
            adminUsuario = "admin";
            adminPassword = passwordPrueba;
        }

        if (!instalacion.AdministradorCreado && adminPassword is null)
            throw new InvalidOperationException("Falta el usuario administrador: abre Configuración y captúralo.");

        IniciarWeb(instalacion, passwordPostgres,
            instalacion.AdministradorCreado ? null : adminUsuario,
            instalacion.AdministradorCreado ? null : adminPassword);

        _uri = new Uri($"http://127.0.0.1:{instalacion.PuertoWeb}/");
        await EsperarWebAsync(_uri);

        if (!instalacion.AdministradorCreado)
        {
            instalacion.AdministradorCreado = true;
            Guardar(instalacion);
        }

        return _uri;
    }

    /// <summary>Detiene la web y PostgreSQL (al salir de la app o al cerrar la sesión de Windows).</summary>
    public void Detener()
    {
        DetenerWeb();

        if (File.Exists(Bin("pg_ctl.exe")) && Directory.Exists(CarpetaPgData))
            Ejecutar(Bin("pg_ctl.exe"), ["stop", "-D", CarpetaPgData, "-m", "fast", "-w", "-t", "30"]);
    }

    // ---------- PostgreSQL ----------

    private async Task CrearClusterAsync(string password)
    {
        // initdb exige una carpeta vacía: lo que dejó un intento fallido se borra.
        if (Directory.Exists(CarpetaPgData))
            Directory.Delete(CarpetaPgData, recursive: true);

        string archivoPassword = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await File.WriteAllTextAsync(archivoPassword, password);

        try
        {
            (int codigo, string salida) = await EjecutarCapturandoAsync(Bin("initdb.exe"),
            [
                "-D", CarpetaPgData, "-U", "postgres", "-A", "scram-sha-256", "--pwfile", archivoPassword,
                "-E", "UTF8", "--locale", Locale
            ]);

            if (codigo != 0)
            {
                await File.WriteAllTextAsync(Path.Combine(CarpetaLogs, "initdb.log"), salida);
                throw new InvalidOperationException("No se pudo crear la base de datos: " + UltimasLineas(salida, 3));
            }
        }
        finally
        {
            File.Delete(archivoPassword);
        }
    }

    private async Task IniciarPostgresAsync(Instalacion instalacion)
    {
        // Sigue corriendo de la vez anterior (la app se cerró sin detenerlo): se usa tal cual.
        if (await EjecutarAsync(Bin("pg_ctl.exe"), ["status", "-D", CarpetaPgData]) == 0)
            return;

        if (!PuertoDisponible(instalacion.PuertoPostgres))
        {
            instalacion.PuertoPostgres = PuertoLibre(instalacion.PuertoPostgres + 1);
            Guardar(instalacion);
        }

        // Sin redirigir la salida: postgres heredaría el pipe y la espera no terminaría. Su salida va a -l.
        int codigo = await EjecutarAsync(Bin("pg_ctl.exe"),
        [
            "start", "-D", CarpetaPgData, "-l", LogPostgres, "-w", "-t", "90",
            "-o", $"-p {instalacion.PuertoPostgres} -c listen_addresses=127.0.0.1"
        ]);

        if (codigo != 0)
            throw new InvalidOperationException("PostgreSQL no arrancó: " + UltimasLineas(LeerCompartido(LogPostgres), 3));
    }

    // ---------- CFDIManager.Web ----------

    private void IniciarWeb(Instalacion instalacion, string passwordPostgres, string? adminUsuario, string? adminPassword)
    {
        var inicio = new ProcessStartInfo(ExeWeb)
        {
            WorkingDirectory = Path.GetDirectoryName(ExeWeb)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argumento in new[]
        {
            "--Modo=Escritorio",
            $"--urls=http://127.0.0.1:{instalacion.PuertoWeb}",
            $"--ConnectionStrings:PostgreSQL=Host=127.0.0.1;Port={instalacion.PuertoPostgres};Database={BaseDatos};Username=postgres",
            $"--AppSettings:CarpetaStorage={CarpetaStorage}",
            $"--Respaldo:PgDump={Bin("pg_dump.exe")}"
        })
        {
            inicio.ArgumentList.Add(argumento);
        }

        // Contraseñas por variables de entorno, no en la línea de comandos. Sin clave maestra: llaves DPAPI del equipo.
        inicio.Environment.Remove("CFDI_CLAVE_MAESTRA");
        inicio.Environment["CFDI_POSTGRES_PASSWORD"] = passwordPostgres;
        inicio.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        inicio.Environment["DOTNET_ENVIRONMENT"] = "Production";

        if (adminPassword is not null)
        {
            inicio.Environment["Seguridad__AdministradorInicial__Usuario"] = adminUsuario ?? "admin";
            inicio.Environment["Seguridad__AdministradorInicial__Password"] = adminPassword;
        }

        _logWeb = new StreamWriter(new FileStream(LogWeb, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
        {
            AutoFlush = true
        };
        _logWeb.WriteLine($"===== {DateTime.Now:dd/MM/yyyy HH:mm:ss} inicio =====");

        _web = Process.Start(inicio) ?? throw new InvalidOperationException("No se pudo iniciar CFDIManager.Web.");
        TrabajoProcesos.Agregar(_web);

        _web.OutputDataReceived += (_, e) => EscribirLog(e.Data);
        _web.ErrorDataReceived += (_, e) => EscribirLog(e.Data);
        _web.BeginOutputReadLine();
        _web.BeginErrorReadLine();
    }

    private void EscribirLog(string? renglon)
    {
        if (renglon is null)
            return;

        lock (_candadoLog)
        {
            _logWeb?.WriteLine(renglon);
        }
    }

    /// <summary>La web espera a PostgreSQL y crea las tablas al arrancar: la primera vez tarda más.</summary>
    private async Task EsperarWebAsync(Uri uri)
    {
        DateTime limite = DateTime.Now.AddMinutes(5);

        while (DateTime.Now < limite)
        {
            if (_web is null || _web.HasExited)
                throw new InvalidOperationException("CFDI Manager se cerró al iniciar: " + UltimasLineas(LeerCompartido(LogWeb), 4));

            if (await RespondeAsync(uri))
                return;

            await Task.Delay(1000);
        }

        throw new InvalidOperationException($"CFDI Manager no respondió en 5 minutos. Revisa {LogWeb}.");
    }

    private void DetenerWeb()
    {
        try
        {
            if (_web is { HasExited: false })
            {
                _web.Kill(entireProcessTree: true);
                _web.WaitForExit(10_000);
            }
        }
        catch (Exception)
        {
            // Ya había terminado.
        }

        _web?.Dispose();
        _web = null;

        lock (_candadoLog)
        {
            _logWeb?.Dispose();
            _logWeb = null;
        }
    }

    // ---------- utilerías ----------

    private static async Task<bool> RespondeAsync(Uri uri)
    {
        try
        {
            using HttpResponseMessage respuesta = await Http.GetAsync(new Uri(uri, "cuenta/login"));
            return respuesta.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool PuertoDisponible(int puerto) =>
        !IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == puerto);

    private static int PuertoLibre(int desde)
    {
        for (int puerto = desde; puerto < desde + 200; puerto++)
        {
            if (PuertoDisponible(puerto))
                return puerto;
        }

        throw new InvalidOperationException($"No hay puertos libres a partir de {desde}.");
    }

    private static string Cifrar(string texto) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(texto), Entropia, DataProtectionScope.LocalMachine));

    private static string Descifrar(string cifrado) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cifrado), Entropia, DataProtectionScope.LocalMachine));

    private static ProcessStartInfo Proceso(string exe, IEnumerable<string> argumentos)
    {
        var inicio = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argumento in argumentos)
            inicio.ArgumentList.Add(argumento);
        return inicio;
    }

    private static async Task<int> EjecutarAsync(string exe, IEnumerable<string> argumentos)
    {
        using Process proceso = Process.Start(Proceso(exe, argumentos))!;
        await proceso.WaitForExitAsync();
        return proceso.ExitCode;
    }

    private static int Ejecutar(string exe, IEnumerable<string> argumentos)
    {
        using Process proceso = Process.Start(Proceso(exe, argumentos))!;
        proceso.WaitForExit(40_000);
        return proceso.HasExited ? proceso.ExitCode : -1;
    }

    private static async Task<(int Codigo, string Salida)> EjecutarCapturandoAsync(string exe, IEnumerable<string> argumentos)
    {
        ProcessStartInfo inicio = Proceso(exe, argumentos);
        inicio.RedirectStandardOutput = true;
        inicio.RedirectStandardError = true;

        using Process proceso = Process.Start(inicio)!;
        Task<string> salida = proceso.StandardOutput.ReadToEndAsync();
        Task<string> errores = proceso.StandardError.ReadToEndAsync();
        await proceso.WaitForExitAsync();

        return (proceso.ExitCode, await salida + await errores);
    }

    private static string LeerCompartido(string archivo)
    {
        try
        {
            using var flujo = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var lector = new StreamReader(flujo);
            return lector.ReadToEnd();
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string UltimasLineas(string texto, int cuantas) =>
        string.Join(" ", texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(cuantas));
}
