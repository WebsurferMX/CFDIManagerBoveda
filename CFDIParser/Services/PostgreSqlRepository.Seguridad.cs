using CFDIParser.Models;
using Npgsql;

namespace CFDIParser.Services;

// Usuarios, roles y empresas asignadas por usuario. Las tablas las usa ASP.NET Identity
// por medio de UsuarioStore / RolStore (CFDIManager.Web\Services\Seguridad).
public partial class PostgreSqlRepository
{
    /// <summary>Tablas de seguridad y roles base. Idempotente; se llama al arrancar.</summary>
    public void AsegurarSeguridad()
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS usuarios (
                usuario_id BIGSERIAL PRIMARY KEY,
                nombre_usuario TEXT NOT NULL,
                nombre_usuario_normalizado TEXT NOT NULL UNIQUE,
                nombre_completo TEXT NOT NULL DEFAULT '',
                email TEXT NOT NULL DEFAULT '',
                password_hash TEXT,
                security_stamp TEXT,
                concurrency_stamp TEXT,
                lockout_habilitado BOOLEAN NOT NULL DEFAULT TRUE,
                lockout_fin TIMESTAMPTZ,
                accesos_fallidos INTEGER NOT NULL DEFAULT 0,
                activo BOOLEAN NOT NULL DEFAULT TRUE,
                fecha_alta TIMESTAMP NOT NULL DEFAULT NOW(),
                ultimo_acceso TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS roles (
                rol_id BIGSERIAL PRIMARY KEY,
                nombre TEXT NOT NULL,
                nombre_normalizado TEXT NOT NULL UNIQUE,
                descripcion TEXT NOT NULL DEFAULT '',
                concurrency_stamp TEXT
            );

            CREATE TABLE IF NOT EXISTS usuario_roles (
                usuario_id BIGINT NOT NULL REFERENCES usuarios (usuario_id) ON DELETE CASCADE,
                rol_id BIGINT NOT NULL REFERENCES roles (rol_id) ON DELETE CASCADE,
                PRIMARY KEY (usuario_id, rol_id)
            );

            CREATE TABLE IF NOT EXISTS usuario_empresas (
                usuario_id BIGINT NOT NULL REFERENCES usuarios (usuario_id) ON DELETE CASCADE,
                empresa_id BIGINT NOT NULL REFERENCES empresas (empresa_id) ON DELETE CASCADE,
                fecha_asignacion TIMESTAMP NOT NULL DEFAULT NOW(),
                PRIMARY KEY (usuario_id, empresa_id)
            );

            CREATE INDEX IF NOT EXISTS ix_usuario_empresas_empresa ON usuario_empresas (empresa_id);

            ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS debe_cambiar_password BOOLEAN NOT NULL DEFAULT FALSE;

            -- Tareas (opciones del menú) que se le quitaron al usuario; las demás de su rol las tiene.
            CREATE TABLE IF NOT EXISTS usuario_tareas_denegadas (
                usuario_id BIGINT NOT NULL REFERENCES usuarios (usuario_id) ON DELETE CASCADE,
                tarea TEXT NOT NULL,
                PRIMARY KEY (usuario_id, tarea)
            );

            INSERT INTO roles (nombre, nombre_normalizado, descripcion, concurrency_stamp) VALUES
                ('Administrador', 'ADMINISTRADOR', 'Acceso total: usuarios, empresas y todas las operaciones.', md5(random()::text)),
                ('Supervisor', 'SUPERVISOR', 'Configura y opera sus empresas asignadas.', md5(random()::text)),
                ('Contador', 'CONTADOR', 'Importa, descarga y exporta CFDI de sus empresas asignadas.', md5(random()::text)),
                ('Consulta', 'CONSULTA', 'Solo consulta y reportes de sus empresas asignadas.', md5(random()::text))
            ON CONFLICT (nombre_normalizado) DO NOTHING;", cn);

        cmd.ExecuteNonQuery();
    }

    // ---------- Usuarios ----------

    private const string SelectUsuario = @"
        SELECT u.*,
               COALESCE((SELECT r.nombre FROM usuario_roles ur JOIN roles r ON r.rol_id = ur.rol_id
                         WHERE ur.usuario_id = u.usuario_id ORDER BY r.rol_id LIMIT 1), '') AS rol,
               (SELECT COUNT(*) FROM usuario_empresas ue WHERE ue.usuario_id = u.usuario_id) AS total_empresas,
               (SELECT COUNT(*) FROM usuario_tareas_denegadas ut WHERE ut.usuario_id = u.usuario_id) AS tareas_denegadas
        FROM usuarios u";

    public List<UsuarioDto> ObtenerUsuarios()
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(SelectUsuario + " ORDER BY u.nombre_usuario", cn);
        using var dr = cmd.ExecuteReader();

        var resultado = new List<UsuarioDto>();
        while (dr.Read()) resultado.Add(MapearUsuario(dr));
        return resultado;
    }

    public UsuarioDto? ObtenerUsuarioPorId(long usuarioId) =>
        ObtenerUsuarioDonde("u.usuario_id = @valor", usuarioId);

    public UsuarioDto? ObtenerUsuarioPorNombre(string nombreUsuarioNormalizado) =>
        ObtenerUsuarioDonde("u.nombre_usuario_normalizado = @valor", nombreUsuarioNormalizado);

    private UsuarioDto? ObtenerUsuarioDonde(string condicion, object valor)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand($"{SelectUsuario} WHERE {condicion}", cn);
        cmd.Parameters.AddWithValue("valor", valor);

        using var dr = cmd.ExecuteReader();
        return dr.Read() ? MapearUsuario(dr) : null;
    }

    public int ContarUsuarios()
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM usuarios", cn);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public long CrearUsuario(UsuarioDto usuario)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO usuarios (nombre_usuario, nombre_usuario_normalizado, nombre_completo, email,
                                  password_hash, security_stamp, concurrency_stamp, lockout_habilitado,
                                  lockout_fin, accesos_fallidos, activo, debe_cambiar_password)
            VALUES (@nombre_usuario, @nombre_usuario_normalizado, @nombre_completo, @email,
                    @password_hash, @security_stamp, @concurrency_stamp, @lockout_habilitado,
                    @lockout_fin, @accesos_fallidos, @activo, @debe_cambiar_password)
            RETURNING usuario_id", cn);

        AgregarParametrosUsuario(cmd, usuario);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void ActualizarUsuario(UsuarioDto usuario)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            UPDATE usuarios SET
                nombre_usuario = @nombre_usuario,
                nombre_usuario_normalizado = @nombre_usuario_normalizado,
                nombre_completo = @nombre_completo,
                email = @email,
                password_hash = @password_hash,
                security_stamp = @security_stamp,
                concurrency_stamp = @concurrency_stamp,
                lockout_habilitado = @lockout_habilitado,
                lockout_fin = @lockout_fin,
                accesos_fallidos = @accesos_fallidos,
                activo = @activo,
                debe_cambiar_password = @debe_cambiar_password
            WHERE usuario_id = @usuario_id", cn);

        cmd.Parameters.AddWithValue("usuario_id", usuario.UsuarioId);
        AgregarParametrosUsuario(cmd, usuario);

        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("El usuario ya no existe.");
    }

    public void EliminarUsuario(long usuarioId)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand("DELETE FROM usuarios WHERE usuario_id = @usuario_id", cn);
        cmd.Parameters.AddWithValue("usuario_id", usuarioId);
        cmd.ExecuteNonQuery();
    }

    public void RegistrarAccesoUsuario(long usuarioId)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand("UPDATE usuarios SET ultimo_acceso = NOW() WHERE usuario_id = @usuario_id", cn);
        cmd.Parameters.AddWithValue("usuario_id", usuarioId);
        cmd.ExecuteNonQuery();
    }

    private static void AgregarParametrosUsuario(NpgsqlCommand cmd, UsuarioDto usuario)
    {
        cmd.Parameters.AddWithValue("nombre_usuario", usuario.NombreUsuario);
        cmd.Parameters.AddWithValue("nombre_usuario_normalizado", usuario.NombreUsuarioNormalizado);
        cmd.Parameters.AddWithValue("nombre_completo", usuario.NombreCompleto ?? "");
        cmd.Parameters.AddWithValue("email", usuario.Email ?? "");
        cmd.Parameters.AddWithValue("password_hash", (object?)usuario.PasswordHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("security_stamp", (object?)usuario.SecurityStamp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("concurrency_stamp", (object?)usuario.ConcurrencyStamp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lockout_habilitado", usuario.LockoutHabilitado);
        cmd.Parameters.AddWithValue("lockout_fin", (object?)usuario.LockoutFin?.ToUniversalTime() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("accesos_fallidos", usuario.AccesosFallidos);
        cmd.Parameters.AddWithValue("activo", usuario.Activo);
        cmd.Parameters.AddWithValue("debe_cambiar_password", usuario.DebeCambiarPassword);
    }

    private static UsuarioDto MapearUsuario(NpgsqlDataReader dr)
    {
        int lockoutFin = dr.GetOrdinal("lockout_fin");
        int ultimoAcceso = dr.GetOrdinal("ultimo_acceso");

        return new UsuarioDto
        {
            UsuarioId = dr.GetInt64(dr.GetOrdinal("usuario_id")),
            NombreUsuario = dr["nombre_usuario"].ToString() ?? "",
            NombreUsuarioNormalizado = dr["nombre_usuario_normalizado"].ToString() ?? "",
            NombreCompleto = dr["nombre_completo"].ToString() ?? "",
            Email = dr["email"].ToString() ?? "",
            PasswordHash = dr["password_hash"] as string,
            SecurityStamp = dr["security_stamp"] as string,
            ConcurrencyStamp = dr["concurrency_stamp"] as string,
            LockoutHabilitado = Convert.ToBoolean(dr["lockout_habilitado"]),
            LockoutFin = dr.IsDBNull(lockoutFin) ? null : dr.GetFieldValue<DateTimeOffset>(lockoutFin),
            AccesosFallidos = Convert.ToInt32(dr["accesos_fallidos"]),
            Activo = Convert.ToBoolean(dr["activo"]),
            DebeCambiarPassword = Convert.ToBoolean(dr["debe_cambiar_password"]),
            FechaAlta = Convert.ToDateTime(dr["fecha_alta"]),
            UltimoAcceso = dr.IsDBNull(ultimoAcceso) ? null : dr.GetDateTime(ultimoAcceso),
            Rol = dr["rol"].ToString() ?? "",
            TotalEmpresas = Convert.ToInt32(dr["total_empresas"]),
            TareasDenegadas = Convert.ToInt32(dr["tareas_denegadas"])
        };
    }

    // ---------- Roles ----------

    public List<RolDto> ObtenerRoles()
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand("SELECT * FROM roles ORDER BY rol_id", cn);
        using var dr = cmd.ExecuteReader();

        var resultado = new List<RolDto>();
        while (dr.Read()) resultado.Add(MapearRol(dr));
        return resultado;
    }

    public RolDto? ObtenerRolPorId(long rolId) =>
        ObtenerRoles().FirstOrDefault(r => r.RolId == rolId);

    public RolDto? ObtenerRolPorNombre(string nombreNormalizado) =>
        ObtenerRoles().FirstOrDefault(r => r.NombreNormalizado == nombreNormalizado);

    public long CrearRol(RolDto rol)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO roles (nombre, nombre_normalizado, descripcion, concurrency_stamp)
            VALUES (@nombre, @nombre_normalizado, @descripcion, @concurrency_stamp)
            RETURNING rol_id", cn);

        AgregarParametrosRol(cmd, rol);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void ActualizarRol(RolDto rol)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            UPDATE roles SET nombre = @nombre, nombre_normalizado = @nombre_normalizado,
                             descripcion = @descripcion, concurrency_stamp = @concurrency_stamp
            WHERE rol_id = @rol_id", cn);

        cmd.Parameters.AddWithValue("rol_id", rol.RolId);
        AgregarParametrosRol(cmd, rol);
        cmd.ExecuteNonQuery();
    }

    public void EliminarRol(long rolId)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand("DELETE FROM roles WHERE rol_id = @rol_id", cn);
        cmd.Parameters.AddWithValue("rol_id", rolId);
        cmd.ExecuteNonQuery();
    }

    private static void AgregarParametrosRol(NpgsqlCommand cmd, RolDto rol)
    {
        cmd.Parameters.AddWithValue("nombre", rol.Nombre);
        cmd.Parameters.AddWithValue("nombre_normalizado", rol.NombreNormalizado);
        cmd.Parameters.AddWithValue("descripcion", rol.Descripcion ?? "");
        cmd.Parameters.AddWithValue("concurrency_stamp", (object?)rol.ConcurrencyStamp ?? DBNull.Value);
    }

    private static RolDto MapearRol(NpgsqlDataReader dr) => new()
    {
        RolId = dr.GetInt64(dr.GetOrdinal("rol_id")),
        Nombre = dr["nombre"].ToString() ?? "",
        NombreNormalizado = dr["nombre_normalizado"].ToString() ?? "",
        Descripcion = dr["descripcion"].ToString() ?? "",
        ConcurrencyStamp = dr["concurrency_stamp"] as string
    };

    // ---------- Roles por usuario ----------

    public List<string> ObtenerRolesDeUsuario(long usuarioId)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT r.nombre FROM usuario_roles ur JOIN roles r ON r.rol_id = ur.rol_id
            WHERE ur.usuario_id = @usuario_id ORDER BY r.rol_id", cn);
        cmd.Parameters.AddWithValue("usuario_id", usuarioId);

        using var dr = cmd.ExecuteReader();
        var resultado = new List<string>();
        while (dr.Read()) resultado.Add(dr.GetString(0));
        return resultado;
    }

    public List<UsuarioDto> ObtenerUsuariosEnRol(string rolNormalizado)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand($@"{SelectUsuario}
            WHERE EXISTS (SELECT 1 FROM usuario_roles ur JOIN roles r ON r.rol_id = ur.rol_id
                          WHERE ur.usuario_id = u.usuario_id AND r.nombre_normalizado = @rol)
            ORDER BY u.nombre_usuario", cn);
        cmd.Parameters.AddWithValue("rol", rolNormalizado);

        using var dr = cmd.ExecuteReader();
        var resultado = new List<UsuarioDto>();
        while (dr.Read()) resultado.Add(MapearUsuario(dr));
        return resultado;
    }

    public void AgregarUsuarioARol(long usuarioId, string rolNormalizado)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO usuario_roles (usuario_id, rol_id)
            SELECT @usuario_id, rol_id FROM roles WHERE nombre_normalizado = @rol
            ON CONFLICT DO NOTHING", cn);
        cmd.Parameters.AddWithValue("usuario_id", usuarioId);
        cmd.Parameters.AddWithValue("rol", rolNormalizado);

        if (cmd.ExecuteNonQuery() == 0 && ObtenerRolPorNombre(rolNormalizado) is null)
            throw new InvalidOperationException($"No existe el rol {rolNormalizado}.");
    }

    public void QuitarUsuarioDeRol(long usuarioId, string rolNormalizado)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            DELETE FROM usuario_roles ur USING roles r
            WHERE r.rol_id = ur.rol_id AND ur.usuario_id = @usuario_id AND r.nombre_normalizado = @rol", cn);
        cmd.Parameters.AddWithValue("usuario_id", usuarioId);
        cmd.Parameters.AddWithValue("rol", rolNormalizado);
        cmd.ExecuteNonQuery();
    }

    // ---------- Empresas por usuario ----------

    public List<long> ObtenerEmpresasIdsDeUsuario(long usuarioId)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(
            "SELECT empresa_id FROM usuario_empresas WHERE usuario_id = @usuario_id", cn);
        cmd.Parameters.AddWithValue("usuario_id", usuarioId);

        using var dr = cmd.ExecuteReader();
        var resultado = new List<long>();
        while (dr.Read()) resultado.Add(dr.GetInt64(0));
        return resultado;
    }

    /// <summary>Empresas activas asignadas al usuario, en el mismo orden que ObtenerEmpresasActivas.</summary>
    public List<EmpresaDto> ObtenerEmpresasActivasDeUsuario(long usuarioId)
    {
        HashSet<long> asignadas = ObtenerEmpresasIdsDeUsuario(usuarioId).ToHashSet();

        return ObtenerEmpresasActivas()
            .Where(e => asignadas.Contains(e.EmpresaId))
            .ToList();
    }

    public bool UsuarioTieneEmpresa(long usuarioId, long empresaId)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT EXISTS (SELECT 1 FROM usuario_empresas
                           WHERE usuario_id = @usuario_id AND empresa_id = @empresa_id)", cn);
        cmd.Parameters.AddWithValue("usuario_id", usuarioId);
        cmd.Parameters.AddWithValue("empresa_id", empresaId);
        return Convert.ToBoolean(cmd.ExecuteScalar());
    }

    /// <summary>Reemplaza las empresas asignadas al usuario por las indicadas.</summary>
    public void GuardarEmpresasDeUsuario(long usuarioId, IEnumerable<long> empresasIds)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();
        using var tx = cn.BeginTransaction();

        using (var borrar = new NpgsqlCommand(@"
            DELETE FROM usuario_empresas
            WHERE usuario_id = @usuario_id AND NOT (empresa_id = ANY(@empresas))", cn, tx))
        {
            borrar.Parameters.AddWithValue("usuario_id", usuarioId);
            borrar.Parameters.AddWithValue("empresas", empresasIds.Distinct().ToArray());
            borrar.ExecuteNonQuery();
        }

        using (var insertar = new NpgsqlCommand(@"
            INSERT INTO usuario_empresas (usuario_id, empresa_id)
            SELECT @usuario_id, UNNEST(@empresas)
            ON CONFLICT DO NOTHING", cn, tx))
        {
            insertar.Parameters.AddWithValue("usuario_id", usuarioId);
            insertar.Parameters.AddWithValue("empresas", empresasIds.Distinct().ToArray());
            insertar.ExecuteNonQuery();
        }

        tx.Commit();
    }
}
