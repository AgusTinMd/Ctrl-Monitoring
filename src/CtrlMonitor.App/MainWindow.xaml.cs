using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CtrlMonitor.App.Controls;
using CtrlMonitor.App.ViewModels;

namespace CtrlMonitor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;

    // Tarjeta del modo gráfico que recibió el clic; el arrastre empieza
    // recién cuando el mouse se mueve más que el umbral del sistema.
    private ChartPanelViewModel? _dragCandidate;
    private Point _dragStart;
    private Point _grabOffset;
    private DragGhostAdorner? _dragGhost;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        InputBindings.Add(new KeyBinding(
            viewModel.ToggleNavigationCommand,
            Key.B,
            ModifierKeys.Control));

        // La ventana entera recibe el arrastre para que la copia flotante siga
        // al cursor también entre tarjetas y sobre los márgenes.
        AllowDrop = true;

        // Reposo: la app mide solo mientras la ventana está en pantalla.
        IsVisibleChanged += (_, _) => ReportShown();
        PreviewDragOver += OnWindowPreviewDragOver;
        DragOver += OnWindowDragOver;

        FitToWorkArea();
    }

    public void AllowClose() => _allowClose = true;

    /// <summary>
    /// Elige un tamaño inicial que entre en el monitor actual. En pantallas
    /// verticales abre una ventana alta, pensada para apilar los gráficos.
    /// </summary>
    private void FitToWorkArea()
    {
        Rect area = SystemParameters.WorkArea;
        const double margin = 24;

        bool portrait = area.Height > area.Width;
        double preferredWidth = portrait ? 1000 : 1220;
        double preferredHeight = portrait ? 1600 : 820;
        double availableWidth = Math.Max(MinWidth, area.Width - margin * 2);
        double availableHeight = Math.Max(MinHeight, area.Height - margin * 2);

        Width = Math.Min(preferredWidth, availableWidth);
        Height = Math.Min(preferredHeight, availableHeight);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int enabled = 1;

            // DWMWA_USE_IMMERSIVE_DARK_MODE (20 en Windows 11, 19 en builds previas).
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        }
        catch
        {
            // Si falla, la ventana usa la barra de título estándar.
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && _viewModel.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized && _viewModel.MinimizeToTray)
            Hide();

        ReportShown();
    }

    private void ReportShown() =>
        _viewModel.SetWindowShown(IsVisible && WindowState != WindowState.Minimized);

    /// <summary>
    /// Arrastrar el borde izquierdo de una columna cambia solo su ancho: hacia la
    /// izquierda la agranda y Nombre ocupa lo que sobra, así nunca se desalinea.
    /// </summary>
    private void ProcessColumnThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column })
            _viewModel.Processes.ResizeColumn(column, -e.HorizontalChange);
    }

    /// <summary>
    /// El clic derecho selecciona la fila antes de abrir el menú, como en el
    /// Administrador de tareas. Fuera de una fila no se abre el menú.
    /// </summary>
    private void ProcessTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;

        while (source is not null and not TreeViewItem)
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);

        if (source is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
        else
        {
            e.Handled = true;
        }
    }

    /// <summary>El SelectedItem del TreeView es de solo lectura: se copia al view model.</summary>
    private void ProcessTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _viewModel.Processes.Selected = e.NewValue as ProcessNode;

    private void ChartCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DisarmDrag();

        if (IsInteractiveControl(e.OriginalSource as DependencyObject))
            return;

        if (sender is not FrameworkElement card || card.DataContext is not ChartPanelViewModel panel)
            return;

        // En la vista de lista el clic también elige la métrica: el evento se
        // marca como manejado y el ListBox ya no se entera.
        if (_viewModel.ChartListItems.Contains(panel))
            _viewModel.SelectedChartListItem = panel;

        _dragCandidate = panel;
        _dragStart = e.GetPosition(this);
        _grabOffset = e.GetPosition(card);

        // Resalta la tarjeta tomada y captura el mouse para enterarnos de que
        // se soltó aunque el puntero ya no esté sobre ella.
        panel.IsDragArmed = true;
        card.CaptureMouse();
        e.Handled = true;
    }

    private void ChartCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is not { } panel || sender is not FrameworkElement card)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            DisarmDrag();
            return;
        }

        Vector moved = e.GetPosition(this) - _dragStart;

        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragCandidate = null;

        // La captura se toma con la tarjeta todavía resaltada; recién después
        // la tarjeta real pasa a ser el hueco tenue donde va a caer.
        UIElement? host = Content as UIElement;
        _dragGhost = host is null ? null : DragGhostAdorner.Show(host, card, _grabOffset);
        _dragGhost?.MoveTo(e.GetPosition(host));

        card.ReleaseMouseCapture();
        panel.IsDragArmed = false;
        panel.IsDragging = true;

        try
        {
            // DoDragDrop bloquea hasta soltar; mientras tanto DragOver va
            // reordenando las tarjetas en vivo y moviendo la copia flotante.
            DragDrop.DoDragDrop(
                card,
                new DataObject(typeof(ChartPanelViewModel), panel),
                DragDropEffects.Move);
        }
        finally
        {
            panel.IsDragging = false;
            _dragGhost?.Remove();
            _dragGhost = null;
        }
    }

    private void ChartCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool wasArmed = _dragCandidate is not null;
        DisarmDrag();
        (sender as UIElement)?.ReleaseMouseCapture();

        if (wasArmed)
            e.Handled = true;
    }

    private void ChartCard_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // Alt+Tab o un diálogo pueden robar la captura antes de soltar.
        if (_dragCandidate is not null && !ReferenceEquals(Mouse.Captured, sender))
            DisarmDrag();
    }

    private void DisarmDrag()
    {
        if (_dragCandidate is { } panel)
            panel.IsDragArmed = false;

        _dragCandidate = null;
    }

    /// <summary>Mueve la copia flotante con el cursor en cualquier parte de la ventana.</summary>
    private void OnWindowPreviewDragOver(object sender, DragEventArgs e)
    {
        if (_dragGhost is not null && Content is UIElement host)
            _dragGhost.MoveTo(e.GetPosition(host));
    }

    /// <summary>Fuera de las tarjetas no se puede soltar: el cursor lo indica.</summary>
    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Handled)
            return;

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void ChartCard_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (e.Data.GetData(typeof(ChartPanelViewModel)) is not ChartPanelViewModel dragged
            || (sender as FrameworkElement)?.DataContext is not ChartPanelViewModel target)
        {
            return;
        }

        e.Effects = DragDropEffects.Move;

        // Las celdas del UniformGrid miden lo mismo, así que al intercambiar
        // posiciones la tarjeta arrastrada queda bajo el cursor y no oscila.
        if (ReferenceEquals(dragged, target))
            return;

        // Tarjetas y lista tienen colecciones separadas; solo se reordena dentro de la misma.
        if (_viewModel.ChartListItems.Contains(dragged) && _viewModel.ChartListItems.Contains(target))
            _viewModel.MoveChartListItem(dragged, _viewModel.ChartListItems.IndexOf(target));
        else if (_viewModel.ChartPanels.Contains(dragged) && _viewModel.ChartPanels.Contains(target))
            _viewModel.MoveChartPanel(dragged, _viewModel.ChartPanels.IndexOf(target));
    }

    private void ChartCard_Drop(object sender, DragEventArgs e)
    {
        // El orden ya se aplicó en DragOver; solo marcamos el evento como resuelto.
        e.Handled = true;
    }

    /// <summary>
    /// Evita iniciar un arrastre al usar el selector de métrica, la ✕ u otros
    /// controles dentro de la tarjeta.
    /// </summary>
    private static bool IsInteractiveControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or ComboBox or ComboBoxItem or TextBoxBase or ScrollBar)
                return true;

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
