using ClosedXML.Excel;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services.Seguridad;

/// <summary>Excel "Empresas asignadas por usuario": hoja por usuario y hoja por empresa.</summary>
public sealed class ReporteAsignacionesService(PostgreSqlRepository repo)
{
    private const string TodasLasEmpresas = "Todas las empresas (Administrador)";

    /// <summary>Usuarios asignados a cada empresa (sin Administradores, que ven todas).</summary>
    public Dictionary<long, List<AsignacionEmpresaDto>> UsuariosPorEmpresa() =>
        repo.ObtenerAsignacionesEmpresas()
            .Where(a => a.EmpresaId is not null && !a.EsAdministrador)
            .GroupBy(a => a.EmpresaId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Usuario, StringComparer.OrdinalIgnoreCase).ToList());

    public (byte[] Contenido, string Nombre) GenerarExcel()
    {
        List<AsignacionEmpresaDto> asignaciones = repo.ObtenerAsignacionesEmpresas();
        List<EmpresaDto> empresas = repo.ObtenerEmpresas()
            .OrderBy(e => e.NombreParaMostrar, StringComparer.OrdinalIgnoreCase).ToList();

        using var libro = new XLWorkbook();

        // ---- Por usuario ----
        IXLWorksheet porUsuario = libro.AddWorksheet("Por usuario");
        Encabezados(porUsuario, "Usuario", "Nombre", "Rol", "Activo", "RFC", "Empresa", "Asignada el");
        int fila = 2;

        foreach (IGrouping<long, AsignacionEmpresaDto> usuario in asignaciones.GroupBy(a => a.UsuarioId))
        {
            AsignacionEmpresaDto u = usuario.First();
            List<AsignacionEmpresaDto> conEmpresa = usuario.Where(a => a.EmpresaId is not null).ToList();

            // El Administrador ve todas; un usuario sin asignaciones no ve ninguna.
            IEnumerable<(string Rfc, string Empresa, DateTime? Fecha)> renglones =
                u.EsAdministrador ? [("", TodasLasEmpresas, null)]
                : conEmpresa.Count == 0 ? [("", "Sin empresas asignadas", null)]
                : conEmpresa.Select(a => (a.Rfc, a.Empresa, a.FechaAsignacion));

            foreach ((string rfc, string empresa, DateTime? fecha) in renglones)
            {
                porUsuario.Cell(fila, 1).Value = u.Usuario;
                porUsuario.Cell(fila, 2).Value = u.NombreCompleto;
                porUsuario.Cell(fila, 3).Value = u.Rol;
                porUsuario.Cell(fila, 4).Value = u.Activo ? "Sí" : "No";
                porUsuario.Cell(fila, 5).Value = rfc;
                porUsuario.Cell(fila, 6).Value = empresa;

                if (fecha is DateTime f)
                {
                    porUsuario.Cell(fila, 7).Value = f;
                    porUsuario.Cell(fila, 7).Style.DateFormat.Format = "dd/MM/yyyy";
                }

                if (rfc == "")
                    porUsuario.Range(fila, 5, fila, 6).Style.Font.Italic = true;

                fila++;
            }
        }

        Terminar(porUsuario, fila - 1, 7);

        // ---- Por empresa ----
        IXLWorksheet porEmpresa = libro.AddWorksheet("Por empresa");
        Encabezados(porEmpresa, "RFC", "Empresa", "Activa", "Usuarios asignados", "Usuarios", "Administradores (ven todas)");

        string administradores = string.Join(", ", asignaciones.Where(a => a.EsAdministrador && a.Activo)
            .Select(a => a.Usuario).Distinct(StringComparer.OrdinalIgnoreCase));

        fila = 2;

        foreach (EmpresaDto e in empresas)
        {
            List<AsignacionEmpresaDto> usuarios = asignaciones
                .Where(a => a.EmpresaId == e.EmpresaId && !a.EsAdministrador)
                .OrderBy(a => a.Usuario, StringComparer.OrdinalIgnoreCase).ToList();

            porEmpresa.Cell(fila, 1).Value = e.Rfc;
            porEmpresa.Cell(fila, 2).Value = e.NombreParaMostrar;
            porEmpresa.Cell(fila, 3).Value = e.Activa ? "Sí" : "No";
            porEmpresa.Cell(fila, 4).Value = usuarios.Count;
            porEmpresa.Cell(fila, 5).Value = usuarios.Count == 0
                ? "Ninguno"
                : string.Join(", ", usuarios.Select(a => $"{a.Usuario} ({a.Rol}{(a.Activo ? "" : ", inactivo")})"));
            porEmpresa.Cell(fila, 6).Value = administradores;

            if (usuarios.Count == 0)
                porEmpresa.Cell(fila, 5).Style.Font.Italic = true;

            fila++;
        }

        Terminar(porEmpresa, fila - 1, 6);

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return (memoria.ToArray(), $"Empresas_asignadas_por_usuario_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    private static void Encabezados(IXLWorksheet hoja, params string[] titulos)
    {
        for (int i = 0; i < titulos.Length; i++)
            hoja.Cell(1, i + 1).Value = titulos[i];

        IXLRange encabezado = hoja.Range(1, 1, 1, titulos.Length);
        encabezado.Style.Font.Bold = true;
        encabezado.Style.Font.FontColor = XLColor.White;
        encabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#052767");
    }

    private static void Terminar(IXLWorksheet hoja, int ultimaFila, int columnas)
    {
        if (ultimaFila >= 2)
            hoja.Range(1, 1, ultimaFila, columnas).SetAutoFilter();

        hoja.SheetView.FreezeRows(1);
        hoja.Columns(1, columnas).AdjustToContents(1, Math.Max(ultimaFila, 1), 8, 70);
    }
}
