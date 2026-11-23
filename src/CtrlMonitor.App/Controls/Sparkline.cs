using System.Windows;
using System.Windows.Media;

namespace CtrlMonitor.App.Controls;

/// <summary>
/// Mini gráfico de dos series (por ejemplo, lectura y escritura de un disco)
/// al estilo btop: sin ejes, comparten la escala y el más reciente va a la derecha.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty FirstValuesProperty = DependencyProperty.Register(
        nameof(FirstValues), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondValuesProperty = DependencyProperty.Register(
        nameof(SecondValues), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FirstBrushProperty = DependencyProperty.Register(
        nameof(FirstBrush), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondBrushProperty = DependencyProperty.Register(
        nameof(SecondBrush), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Goldenrod, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Capacidad de la ventana: cantidad de muestras que entran a lo ancho.</summary>
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(40, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Escala mínima, para que el ruido de pocos KB/s no llene el gráfico.</summary>
    public static readonly DependencyProperty MinimumScaleProperty = DependencyProperty.Register(
        nameof(MinimumScale), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? FirstValues
    {
        get => (IReadOnlyList<double>?)GetValue(FirstValuesProperty);
        set => SetValue(FirstValuesProperty, value);
    }

    public IReadOnlyList<double>? SecondValues
    {
        get => (IReadOnlyList<double>?)GetValue(SecondValuesProperty);
        set => SetValue(SecondValuesProperty, value);
    }

    public Brush FirstBrush
    {
        get => (Brush)GetValue(FirstBrushProperty);
        set => SetValue(FirstBrushProperty, value);
    }

    public Brush SecondBrush
    {
        get => (Brush)GetValue(SecondBrushProperty);
        set => SetValue(SecondBrushProperty, value);
    }

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public double MinimumScale
    {
        get => (double)GetValue(MinimumScaleProperty);
        set => SetValue(MinimumScaleProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 2 || height <= 2)
            return;

        // Fondo transparente: recibe el mouse para el tooltip.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        IReadOnlyList<double> first = FirstValues ?? Array.Empty<double>();
        IReadOnlyList<double> second = SecondValues ?? Array.Empty<double>();
        double max = MinimumScale;

        foreach (double v in first)
            max = Math.Max(max, v);

        foreach (double v in second)
            max = Math.Max(max, v);

        int capacity = Math.Max(2, Capacity);
        double step = width / (capacity - 1);

        Draw(dc, first, FirstBrush, max, step, width, height, capacity);
        Draw(dc, second, SecondBrush, max, step, width, height, capacity);
    }

    private static void Draw(DrawingContext dc, IReadOnlyList<double> values, Brush brush,
        double max, double step, double width, double height, int capacity)
    {
        if (values.Count < 2)
            return;

        int count = Math.Min(values.Count, capacity);
        int start = values.Count - count;
        double x0 = width - (count - 1) * step;

        var line = new StreamGeometry();
        var area = new StreamGeometry();

        using (StreamGeometryContext l = line.Open())
        using (StreamGeometryContext a = area.Open())
        {
            a.BeginFigure(new Point(x0, height), true, true);

            for (int i = 0; i < count; i++)
            {
                double value = Math.Max(0, values[start + i]);
                var point = new Point(x0 + i * step, height - 1 - value / max * (height - 2));

                if (i == 0)
                    l.BeginFigure(point, false, false);
                else
                    l.LineTo(point, true, true);

                a.LineTo(point, true, true);
            }

            a.LineTo(new Point(width, height), true, true);
        }

        line.Freeze();
        area.Freeze();

        Brush fill = brush.Clone();
        fill.Opacity = 0.18;
        fill.Freeze();

        var pen = new Pen(brush, 1.4) { LineJoin = PenLineJoin.Round };
        pen.Freeze();

        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, line);
    }
}
