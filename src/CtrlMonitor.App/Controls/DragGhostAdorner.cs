using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace CtrlMonitor.App.Controls;

/// <summary>
/// Copia flotante de una tarjeta que sigue al cursor mientras se arrastra.
/// Es una captura fija de la tarjeta al empezar el arrastre: se dibuja sobre
/// toda la ventana y no intercepta el mouse, así el drop llega a las tarjetas.
/// </summary>
internal sealed class DragGhostAdorner : Adorner
{
    private readonly ImageSource _snapshot;
    private readonly Size _size;
    private readonly Point _grabOffset;
    private Point _position;

    private DragGhostAdorner(UIElement adornedElement, ImageSource snapshot, Size size, Point grabOffset)
        : base(adornedElement)
    {
        _snapshot = snapshot;
        _size = size;
        _grabOffset = grabOffset;
        IsHitTestVisible = false;

        // Sombra amplia para que la copia se vea «levantada» sobre el resto.
        Effect = new DropShadowEffect
        {
            Color = Color.FromRgb(0x05, 0x0A, 0x14),
            BlurRadius = 28,
            ShadowDepth = 8,
            Opacity = 0.65
        };
    }

    /// <summary>
    /// Crea la copia de <paramref name="card"/> sobre la capa de adornos de
    /// <paramref name="host"/>. Devuelve <c>null</c> si no hay capa disponible.
    /// </summary>
    /// <param name="grabOffset">Punto de la tarjeta donde se hizo clic.</param>
    public static DragGhostAdorner? Show(UIElement host, FrameworkElement card, Point grabOffset)
    {
        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(host);

        if (layer is null || card.ActualWidth < 1 || card.ActualHeight < 1)
            return null;

        DpiScale dpi = VisualTreeHelper.GetDpi(card);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(card.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(card.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        // RenderTargetBitmap.Render(card) arrastraría el Margin de la tarjeta;
        // con un VisualBrush sobre un DrawingVisual la captura queda alineada.
        var size = new Size(card.ActualWidth, card.ActualHeight);
        var visual = new DrawingVisual();

        using (DrawingContext context = visual.RenderOpen())
            context.DrawRectangle(new VisualBrush(card) { Stretch = Stretch.None }, null, new Rect(size));

        bitmap.Render(visual);
        bitmap.Freeze();

        var adorner = new DragGhostAdorner(host, bitmap, size, grabOffset);

        layer.Add(adorner);
        return adorner;
    }

    /// <summary>Mueve la copia para que el punto agarrado quede bajo el cursor.</summary>
    /// <param name="position">Posición del cursor relativa al elemento anfitrión.</param>
    public void MoveTo(Point position)
    {
        _position = position;
        InvalidateVisual();
    }

    public void Remove() => AdornerLayer.GetAdornerLayer(AdornedElement)?.Remove(this);

    protected override void OnRender(DrawingContext dc)
    {
        // Leve escala para reforzar la sensación de tarjeta tomada con la mano.
        const double Scale = 1.03;

        var origin = new Point(
            _position.X - _grabOffset.X * Scale,
            _position.Y - _grabOffset.Y * Scale);

        dc.PushOpacity(0.92);
        dc.DrawImage(_snapshot, new Rect(origin, new Size(_size.Width * Scale, _size.Height * Scale)));
        dc.Pop();
    }
}
