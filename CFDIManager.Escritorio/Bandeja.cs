using System.Drawing;
using Forms = System.Windows.Forms;

namespace CFDIManager.Escritorio;

/// <summary>Ícono en la bandeja de Windows: menú y avisos (en Windows 10 y 11 salen como notificación).</summary>
public sealed class Bandeja : IDisposable
{
    private readonly Forms.NotifyIcon _icono;
    private readonly Forms.ToolStripMenuItem _actualizar;
    private Action? _alHacerClic;
    private Action? _instalarActualizacion;

    public Bandeja(Action abrir, Action recargar, Action configurar, Action buscarActualizaciones, Action salir)
    {
        var menu = new Forms.ContextMenuStrip();
        _actualizar = new Forms.ToolStripMenuItem("", null, (_, _) => _instalarActualizacion?.Invoke()) { Visible = false };
        menu.Items.Add(_actualizar);
        menu.Items.Add("Abrir CFDI Manager", null, (_, _) => abrir());
        menu.Items.Add("Recargar", null, (_, _) => recargar());
        menu.Items.Add("Configuración…", null, (_, _) => configurar());
        menu.Items.Add("Buscar actualizaciones", null, (_, _) => buscarActualizaciones());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => salir());

        _icono = new Forms.NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = $"CFDI Manager {Actualizaciones.VersionActual}",
            ContextMenuStrip = menu,
            Visible = true
        };

        _icono.DoubleClick += (_, _) => abrir();
        _icono.BalloonTipClicked += (_, _) => _alHacerClic?.Invoke();
    }

    /// <summary>Muestra un aviso; <paramref name="alHacerClic"/> se ejecuta si el usuario le da clic.</summary>
    public void Avisar(string titulo, string texto, Action? alHacerClic = null)
    {
        _alHacerClic = alHacerClic;
        _icono.ShowBalloonTip(5000, titulo, texto, Forms.ToolTipIcon.None);
    }

    /// <summary>Agrega "Instalar la versión X" arriba del menú.</summary>
    public void MostrarActualizacion(string version, Action instalar)
    {
        _instalarActualizacion = instalar;
        _actualizar.Text = $"Instalar la versión {version} y reiniciar";
        _actualizar.Font = new Font(_actualizar.Font, System.Drawing.FontStyle.Bold);
        _actualizar.Visible = true;
    }

    public void Dispose()
    {
        _icono.Visible = false;
        _icono.Dispose();
    }
}
