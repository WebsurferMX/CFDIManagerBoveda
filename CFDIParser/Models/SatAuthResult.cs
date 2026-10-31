namespace CFDIManager.Web.Models;

public class SatAuthResult
{
    public bool ConfiguracionCompleta { get; set; }

    public string? RutaCer { get; set; }

    public string? RutaKey { get; set; }

    public string? PasswordKey { get; set; }

    public List<string> Errores { get; set; } = new();
}
