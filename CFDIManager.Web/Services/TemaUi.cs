using MudBlazor;

namespace CFDIManager.Web.Services;

/// <summary>
/// Modo claro/oscuro del circuito. La preferencia vive en el navegador (js/theme.js); este servicio la comparte
/// entre el botón de tema y el MudThemeProvider del layout para que los componentes de MudBlazor cambien junto con
/// Bootstrap.
/// </summary>
public sealed class TemaUi
{
    /// <summary>Oscuro mientras no se lea el navegador: es como abre la app sin preferencia guardada.</summary>
    public bool Oscuro { get; private set; } = true;

    public event Action? Cambio;

    public void Establecer(bool oscuro)
    {
        if (oscuro == Oscuro)
            return;

        Oscuro = oscuro;
        Cambio?.Invoke();
    }

    /// <summary>Colores de la app (los mismos de app.css) para los componentes de MudBlazor.</summary>
    public static readonly MudTheme Tema = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1d4ed8",
            Secondary = "#64748b",
            Background = "#e6e9ee",
            Surface = "#ffffff",
            AppbarBackground = "#f0f2f5",
            AppbarText = "#1e293b",
            DrawerBackground = "#052767",
            DrawerText = "rgba(255,255,255,0.82)",
            DrawerIcon = "rgba(255,255,255,0.82)",
            TextPrimary = "#1e293b",
            TextSecondary = "#64748b",
            LinesDefault = "#d3d9e1"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#60a5fa",
            Secondary = "#94a3b8",
            Background = "#09111f",
            Surface = "#111c2e",
            AppbarBackground = "#121c2b",
            AppbarText = "#e5edf8",
            DrawerBackground = "#04153d",
            DrawerText = "rgba(255,255,255,0.82)",
            DrawerIcon = "rgba(255,255,255,0.82)",
            TextPrimary = "#e5edf8",
            TextSecondary = "#9fb0c7",
            LinesDefault = "#263a55"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Segoe UI", "system-ui", "-apple-system", "Roboto", "Helvetica Neue", "Arial", "sans-serif"]
            }
        },
        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft = "260px",
            AppbarHeight = "4rem"
        }
    };
}
