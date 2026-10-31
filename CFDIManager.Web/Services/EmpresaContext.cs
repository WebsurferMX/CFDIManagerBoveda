using CFDIParser.Models;

namespace CFDIManager.Web.Services;

public class EmpresaContext
{
    public EmpresaDto? EmpresaActiva
    {
        get;
        private set;
    }

    public event Action? OnChange;

    public bool TieneEmpresaActiva =>
        EmpresaActiva is not null;

    public long EmpresaId =>
        EmpresaActiva?.EmpresaId ?? 0;

    public string Rfc =>
        EmpresaActiva?.Rfc ?? "";

    public string NombreEmpresa =>
        EmpresaActiva?.NombreParaMostrar ?? "";

    public void Seleccionar(EmpresaDto empresa)
{
    EmpresaActiva = empresa;

    OnChange?.Invoke();
}

    public void Limpiar()
    {
        if (EmpresaActiva is null)
        {
            return;
        }

        EmpresaActiva =
            null;

        OnChange?.Invoke();
    }
}