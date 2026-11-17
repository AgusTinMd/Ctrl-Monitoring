using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        InputBindings.Add(new KeyBinding(
            viewModel.ToggleNavigationCommand,
            Key.B,
            ModifierKeys.Control));

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
    }

    private void ChartCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;

        if (IsInteractiveControl(e.OriginalSource as DependencyObject))
            return;

        if ((sender as FrameworkElement)?.DataContext is ChartPanelViewModel panel)
        {
            _dragCandidate = panel;
            _dragStart = e.GetPosition(this);
        }
    }

    private void ChartCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is not { } panel || e.LeftButton != MouseButtonState.Pressed)
            return;

        Vector moved = e.GetPosition(this) - _dragStart;

        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragCandidate = null;
        panel.IsDragging = true;

        try
        {
            // DoDragDrop bloquea hasta soltar; mientras tanto DragOver va
            // reordenando las tarjetas en vivo.
            DragDrop.DoDragDrop(
                (DependencyObject)sender,
                new DataObject(typeof(ChartPanelViewModel), panel),
                DragDropEffects.Move);
        }
        finally
        {
            panel.IsDragging = false;
        }
    }

    private void ChartCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _dragCandidate = null;

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
        if (!ReferenceEquals(dragged, target))
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
