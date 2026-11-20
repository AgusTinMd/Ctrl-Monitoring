using System.Windows.Controls;

namespace CtrlMonitor.App.Controls;

/// <summary>
/// Barra de métricas compartida por la vista previa y la ventana del overlay.
/// Solo presenta datos: no lee sensores ni conoce el proceso del juego.
/// </summary>
public partial class OverlayBar : UserControl
{
    public OverlayBar()
    {
        InitializeComponent();
    }
}
