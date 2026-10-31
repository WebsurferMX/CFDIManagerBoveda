using Npgsql;

namespace CFDIParser.Services;

// Tareas (opciones del menú) quitadas a cada usuario; la tabla se crea en AsegurarSeguridad.
public partial class PostgreSqlRepository
{
    public List<string> ObtenerTareasDenegadas(long usuarioId)
    {
        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();

        using var cmd = new NpgsqlCommand(
            "SELECT tarea FROM usuario_tareas_denegadas WHERE usuario_id = @usuario_id", cn);
        cmd.Parameters.AddWithValue("usuario_id", usuarioId);

        using var dr = cmd.ExecuteReader();
        var resultado = new List<string>();
        while (dr.Read()) resultado.Add(dr.GetString(0));
        return resultado;
    }

    /// <summary>Reemplaza las tareas quitadas al usuario por las indicadas.</summary>
    public void GuardarTareasDenegadas(long usuarioId, IEnumerable<string> tareas)
    {
        string[] lista = tareas.Distinct().ToArray();

        using var cn = new NpgsqlConnection(_connectionString);
        cn.Open();
        using var tx = cn.BeginTransaction();

        using (var borrar = new NpgsqlCommand(
            "DELETE FROM usuario_tareas_denegadas WHERE usuario_id = @usuario_id", cn, tx))
        {
            borrar.Parameters.AddWithValue("usuario_id", usuarioId);
            borrar.ExecuteNonQuery();
        }

        using (var insertar = new NpgsqlCommand(@"
            INSERT INTO usuario_tareas_denegadas (usuario_id, tarea)
            SELECT @usuario_id, UNNEST(@tareas)", cn, tx))
        {
            insertar.Parameters.AddWithValue("usuario_id", usuarioId);
            insertar.Parameters.AddWithValue("tareas", lista);
            insertar.ExecuteNonQuery();
        }

        tx.Commit();
    }
}
