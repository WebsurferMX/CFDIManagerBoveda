using System.Reflection;
using Npgsql;

namespace CFDIManager.Web.Services.Esquema;

/// <summary>
/// En modo Escritorio (instalación propia de un cliente) la base empieza vacía: se crea si no existe y se le aplica
/// esquema_inicial.sql (las tablas básicas que en el servidor ya existían: cfdi, empresas, empresas_sat...). Las
/// tablas y columnas nuevas las siguen creando los Asegurar* de PostgreSqlRepository al arrancar.
/// </summary>
public static class EsquemaInicial
{
    private const string Recurso = "CFDIManager.Web.Services.Esquema.esquema_inicial.sql";

    /// <summary>Crea la base y su esquema si faltan. Regresa true si se aplicó el esquema.</summary>
    public static bool Asegurar(string cadena, ILogger? logger = null)
    {
        var datos = new NpgsqlConnectionStringBuilder(cadena);
        string baseDatos = datos.Database ?? throw new InvalidOperationException("La cadena de conexión no tiene Database.");

        CrearBaseSiFalta(datos, baseDatos, logger);

        using var conexion = new NpgsqlConnection(cadena);
        conexion.Open();

        using (var existe = new NpgsqlCommand("SELECT to_regclass('public.cfdi') IS NOT NULL", conexion))
        {
            if ((bool)existe.ExecuteScalar()!)
                return false;
        }

        string sql = LeerRecurso();

        using NpgsqlTransaction transaccion = conexion.BeginTransaction();
        using (var comando = new NpgsqlCommand(sql, conexion, transaccion) { CommandTimeout = 300 })
        {
            comando.ExecuteNonQuery();
        }
        transaccion.Commit();

        logger?.LogInformation("Esquema inicial aplicado a la base {Base}.", baseDatos);
        return true;
    }

    private static void CrearBaseSiFalta(NpgsqlConnectionStringBuilder datos, string baseDatos, ILogger? logger)
    {
        var mantenimiento = new NpgsqlConnectionStringBuilder(datos.ConnectionString) { Database = "postgres", Pooling = false };

        using var conexion = new NpgsqlConnection(mantenimiento.ConnectionString);
        conexion.Open();

        using (var existe = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @nombre", conexion))
        {
            existe.Parameters.AddWithValue("nombre", baseDatos);
            if (existe.ExecuteScalar() is not null)
                return;
        }

        // CREATE DATABASE no acepta parámetros: el nombre va entre comillas dobles y escapado.
        string nombre = "\"" + baseDatos.Replace("\"", "\"\"") + "\"";
        using (var crear = new NpgsqlCommand($"CREATE DATABASE {nombre} ENCODING 'UTF8' TEMPLATE template0", conexion))
        {
            crear.ExecuteNonQuery();
        }

        logger?.LogInformation("Base {Base} creada.", baseDatos);
    }

    private static string LeerRecurso()
    {
        using Stream flujo = Assembly.GetExecutingAssembly().GetManifestResourceStream(Recurso)
            ?? throw new InvalidOperationException($"No se encontró el recurso {Recurso}.");
        using var lector = new StreamReader(flujo);
        return lector.ReadToEnd();
    }
}
