using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CtrlMonitor.App.Controls;

/// <summary>
/// Barra de métricas compartida por la vista previa y la ventana del overlay.
/// Solo presenta datos: no lee sensores ni conoce el proceso del juego.
/// </summary>
public partial class OverlayBar : UserControl
{
    /// <summary>
    /// Un trozo puede achicarse recién después de este tiempo siendo más
    /// angosto. Así la barra no "respira" cuando un valor pasa de 99 a 100 y
    /// vuelve, y la ventana no cambia de tamaño a cada lectura.
    /// </summary>
    private static readonly TimeSpan ShrinkDelay = TimeSpan.FromSeconds(5);

    private static readonly DependencyPropertyDescriptor TextDescriptor =
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

    private static readonly DependencyPropertyDescriptor FontSizeDescriptor =
        DependencyPropertyDescriptor.FromProperty(TextBlock.FontSizeProperty, typeof(TextBlock));

    private readonly Dictionary<TextBlock, DateTime> _wideSince = new();

    public OverlayBar()
    {
        InitializeComponent();
    }

    private void OnSegmentLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock block || _wideSince.ContainsKey(block))
            return;

        _wideSince[block] = DateTime.UtcNow;
        TextDescriptor.AddValueChanged(block, OnSegmentTextChanged);
        FontSizeDescriptor.AddValueChanged(block, OnSegmentFontChanged);
        Stabilize(block, reset: true);
    }

    private void OnSegmentUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock block || !_wideSince.Remove(block))
            return;

        TextDescriptor.RemoveValueChanged(block, OnSegmentTextChanged);
        FontSizeDescriptor.RemoveValueChanged(block, OnSegmentFontChanged);
    }

    private void OnSegmentTextChanged(object? sender, EventArgs e)
    {
        if (sender is TextBlock block)
            Stabilize(block, reset: false);
    }

    private void OnSegmentFontChanged(object? sender, EventArgs e)
    {
        // Con otro tamaño de letra el ancho anterior ya no sirve.
        if (sender is TextBlock block)
            Stabilize(block, reset: true);
    }

    /// <summary>
    /// Crece al instante si el texto lo necesita y solo se achica si estuvo
    /// más angosto durante <see cref="ShrinkDelay"/>.
    /// </summary>
    private void Stabilize(TextBlock block, bool reset)
    {
        double natural = NaturalWidth(block);
        DateTime now = DateTime.UtcNow;

        if (reset || natural >= block.MinWidth)
        {
            block.MinWidth = Math.Ceiling(natural);
            _wideSince[block] = now;
        }
        else if (now - _wideSince[block] > ShrinkDelay)
        {
            block.MinWidth = Math.Ceiling(natural);
            _wideSince[block] = now;
        }
    }

    private static double NaturalWidth(TextBlock block)
    {
        var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
        var text = new FormattedText(
            block.Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            block.FlowDirection,
            typeface,
            block.FontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(block).PixelsPerDip);

        return text.WidthIncludingTrailingWhitespace;
    }
}
