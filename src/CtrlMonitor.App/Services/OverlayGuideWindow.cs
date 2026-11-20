using System.Windows;
using System.Windows.Media;

namespace CtrlMonitor.App.Services;

/// <summary>
/// Grilla de ayuda que cubre el monitor mientras el overlay está desbloqueado:
/// marca los bordes, los tercios y el centro para encuadrar la barra. Es
/// transparente a los clics; solo se dibuja.
/// </summary>
internal sealed class OverlayGuideWindow : Window
{
    public OverlayGuideWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = false;
        IsHitTestVisible = false;
        Content = new GuideCanvas();
    }

    private sealed class GuideCanvas : FrameworkElement
    {
        private static readonly Pen EdgePen = MakePen("#CC3B82F6", 2);
        private static readonly Pen CenterPen = MakePen("#B33B82F6", 1.5, dashed: true);
        private static readonly Pen ThirdPen = MakePen("#59FFFFFF", 1, dashed: true);
        private static readonly Brush Tint = MakeBrush("#1A000000");

        protected override void OnRender(DrawingContext context)
        {
            double width = ActualWidth;
            double height = ActualHeight;

            if (width <= 0 || height <= 0)
                return;

            context.DrawRectangle(Tint, null, new Rect(0, 0, width, height));

            for (int i = 1; i <= 2; i++)
            {
                double x = Math.Round(width * i / 3) + 0.5;
                double y = Math.Round(height * i / 3) + 0.5;
                context.DrawLine(ThirdPen, new Point(x, 0), new Point(x, height));
                context.DrawLine(ThirdPen, new Point(0, y), new Point(width, y));
            }

            double centerX = Math.Round(width / 2) + 0.5;
            double centerY = Math.Round(height / 2) + 0.5;
            context.DrawLine(CenterPen, new Point(centerX, 0), new Point(centerX, height));
            context.DrawLine(CenterPen, new Point(0, centerY), new Point(width, centerY));

            context.DrawRectangle(null, EdgePen, new Rect(1, 1, width - 2, height - 2));
        }

        private static Pen MakePen(string color, double thickness, bool dashed = false)
        {
            var pen = new Pen(MakeBrush(color), thickness);

            if (dashed)
                pen.DashStyle = new DashStyle(new double[] { 6, 4 }, 0);

            pen.Freeze();
            return pen;
        }

        private static Brush MakeBrush(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
            brush.Freeze();
            return brush;
        }
    }
}
