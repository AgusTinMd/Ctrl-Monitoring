using System.Windows;
using System.Windows.Controls;

namespace FanCtrl.App.Controls;

/// <summary>
/// Panel que distribuye sus hijos en columnas según el ancho disponible.
/// Mantiene tarjetas del mismo ancho, con separación uniforme, y colapsa a
/// una sola columna cuando la ventana es angosta (por ejemplo en vertical).
/// </summary>
public sealed class ResponsiveGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth),
        typeof(double),
        typeof(ResponsiveGrid),
        new FrameworkPropertyMetadata(320d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns),
        typeof(int),
        typeof(ResponsiveGrid),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap),
        typeof(double),
        typeof(ResponsiveGrid),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty BalancedRowsProperty = DependencyProperty.Register(
        nameof(BalancedRows),
        typeof(bool),
        typeof(ResponsiveGrid),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>
    /// Evita filas huérfanas: con 4 tarjetas usa 4, 2 o 1 columnas en lugar de
    /// dejar una sola tarjeta en la última fila.
    /// </summary>
    public bool BalancedRows
    {
        get => (bool)GetValue(BalancedRowsProperty);
        set => SetValue(BalancedRowsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = InternalChildren.Count;

        if (count == 0)
            return default;

        double width = availableSize.Width;

        if (double.IsInfinity(width) || double.IsNaN(width))
        {
            int visibleColumns = Math.Min(count, Math.Max(1, MaxColumns));
            width = Math.Max(1, MinItemWidth) * visibleColumns + Gap * (visibleColumns - 1);
        }

        int columns = GetColumnCount(width, count);
        double childWidth = GetChildWidth(width, columns);
        double totalHeight = 0;
        int index = 0;

        while (index < count)
        {
            int rowItems = Math.Min(columns, count - index);
            double rowHeight = 0;

            for (int i = 0; i < rowItems; i++)
            {
                UIElement child = InternalChildren[index + i];
                child.Measure(new Size(childWidth, availableSize.Height));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            totalHeight += rowHeight + (index > 0 ? Gap : 0);
            index += rowItems;
        }

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = InternalChildren.Count;

        if (count == 0)
            return finalSize;

        int columns = GetColumnCount(finalSize.Width, count);
        double childWidth = GetChildWidth(finalSize.Width, columns);
        double y = 0;
        int index = 0;

        while (index < count)
        {
            int rowItems = Math.Min(columns, count - index);
            double rowHeight = 0;

            for (int i = 0; i < rowItems; i++)
                rowHeight = Math.Max(rowHeight, InternalChildren[index + i].DesiredSize.Height);

            for (int i = 0; i < rowItems; i++)
            {
                double x = i * (childWidth + Gap);
                InternalChildren[index + i].Arrange(new Rect(x, y, childWidth, rowHeight));
            }

            y += rowHeight + Gap;
            index += rowItems;
        }

        return finalSize;
    }

    private int GetColumnCount(double width, int count)
    {
        int maxColumns = Math.Max(1, Math.Min(MaxColumns, count));
        double itemWidth = Math.Max(1, MinItemWidth);
        int columns = (int)Math.Floor((Math.Max(0, width) + Gap) / (itemWidth + Gap));

        columns = Math.Clamp(columns, 1, maxColumns);

        if (BalancedRows && count > 1)
            while (columns > 1 && count % columns != 0)
                columns--;

        return columns;
    }

    private double GetChildWidth(double width, int columns) =>
        Math.Max(0, (width - Gap * (columns - 1)) / columns);
}
