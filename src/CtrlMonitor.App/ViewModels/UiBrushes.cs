using System.Windows.Media;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Colores compartidos por los view models, congelados para usarlos desde cualquier hilo.</summary>
internal static class UiBrushes
{
    public static readonly Brush Accent = Frozen("#3AD1C0");
    public static readonly Brush AccentSoft = Frozen("#123330");
    public static readonly Brush Success = Frozen("#8BD46A");
    public static readonly Brush SuccessSoft = Frozen("#1B2A17");
    public static readonly Brush Warning = Frozen("#F4C152");
    public static readonly Brush WarningSoft = Frozen("#302712");
    public static readonly Brush Danger = Frozen("#FF4D6D");
    public static readonly Brush DangerSoft = Frozen("#3A1520");
    public static readonly Brush Muted = Frozen("#868D98");
    public static readonly Brush NeutralSoft = Frozen("#2A2F37");
    public static readonly Brush TextPrimary = Frozen("#ECEEF1");
    public static readonly Brush TextSecondary = Frozen("#A3A9B3");
    public static readonly Brush ChartCpu = Frozen("#FFA477");
    public static readonly Brush ChartGpu = Frozen("#F2D58A");
    public static readonly Brush ChartBoard = Frozen("#E0B88A");
    public static readonly Brush ChartHotspot = Frozen("#FF8C8C");
    public static readonly Brush ChartRam = Frozen("#5AB8F5");
    public static readonly Brush ChartVram = Frozen("#8FA0FF");
    public static readonly Brush ChartNetDown = Frozen("#4FD8E8");
    public static readonly Brush ChartNetUp = Frozen("#8FB3FF");
    public static readonly Brush ChartDiskRead = Frozen("#A6E3A1");
    public static readonly Brush ChartDiskWrite = Frozen("#C9D66A");

    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
