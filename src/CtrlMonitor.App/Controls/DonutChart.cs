using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CtrlMonitor.App.Controls;

/// <summary>
/// Gráfico de anillo para un porcentaje (por ejemplo, el espacio usado de una
/// unidad): el arco de uso sobre una pista completa y el % en el centro.
/// </summary>
public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(DonutChart),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(DonutChart),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(DonutChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(DonutChart),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(DonutChart),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Porcentaje de 0 a 100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>Color del arco de uso.</summary>
    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Color de la parte libre del anillo.</summary>
    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>Color del % del centro.</summary>
    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(80, 80);

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);

        if (size < 16)
            return;

        double thickness = Math.Min(Thickness, size / 3);
        double radius = (size - thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double value = Math.Clamp(Value, 0, 100);

        var track = new Pen(Track, thickness);
        dc.DrawEllipse(null, track, center, radius, radius);

        if (value > 0)
        {
            var arc = new Pen(Fill, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            if (value >= 99.95)
            {
                dc.DrawEllipse(null, new Pen(Fill, thickness), center, radius, radius);
            }
            else
            {
                // Arranca arriba (las 12) y avanza en sentido horario.
                double angle = value / 100 * 360;
                Point start = PointAt(center, radius, 0);
                Point end = PointAt(center, radius, angle);

                var figure = new PathFigure { StartPoint = start, IsClosed = false };
                figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > 180,
                    SweepDirection.Clockwise, isStroked: true));

                var geometry = new PathGeometry(new[] { figure });
                geometry.Freeze();
                dc.DrawGeometry(null, arc, geometry);
            }
        }

        var text = new FormattedText(
            value.ToString("0", CultureInfo.CurrentCulture) + " %",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            Math.Max(10, size * 0.2),
            Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        double radians = (degrees - 90) * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
