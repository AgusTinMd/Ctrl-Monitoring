using System.Windows.Media;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Colores compartidos por los view models, congelados para usarlos desde cualquier hilo.</summary>
internal static class UiBrushes
{
    public static readonly Brush Accent = Frozen("#3B82F6");
    public static readonly Brush AccentSoft = Frozen("#16294A");
    public static readonly Brush Success = Frozen("#22C55E");
    public static readonly Brush SuccessSoft = Frozen("#102A1B");
    public static readonly Brush Warning = Frozen("#F59E0B");
    public static readonly Brush WarningSoft = Frozen("#2E2410");
    public static readonly Brush Danger = Frozen("#EF4444");
    public static readonly Brush DangerSoft = Frozen("#2E1216");
    public static readonly Brush Muted = Frozen("#647696");
    public static readonly Brush NeutralSoft = Frozen("#1E2C45");
    public static readonly Brush TextPrimary = Frozen("#E8EEF9");
    public static readonly Brush TextSecondary = Frozen("#93A5C1");
    public static readonly Brush ChartCpu = Frozen("#5B9DFF");
    public static readonly Brush ChartGpu = Frozen("#3DDC97");
    public static readonly Brush ChartBoard = Frozen("#F5B84C");
    public static readonly Brush ChartHotspot = Frozen("#FF6B6B");
    public static readonly Brush ChartRam = Frozen("#A78BFA");
    public static readonly Brush ChartVram = Frozen("#F472B6");
    public static readonly Brush ChartNetDown = Frozen("#22D3EE");
    public static readonly Brush ChartNetUp = Frozen("#FB923C");

    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
