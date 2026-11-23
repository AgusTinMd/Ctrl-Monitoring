using System.Windows;
using System.Windows.Input;
using CtrlMonitor.App.ViewModels;

namespace CtrlMonitor.App;

/// <summary>Widget compacto de escritorio: CPU, GPU, RAM y red en una ventanita.</summary>
public partial class WidgetWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;

    public WidgetWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += (_, _) => PlaceOnScreen();
        IsVisibleChanged += (_, _) => _viewModel.SetWidgetShown(IsVisible);
    }

    public void AllowClose() => _allowClose = true;

    /// <summary>Posición guardada, o abajo a la derecha si no hay o quedó fuera de la pantalla.</summary>
    private void PlaceOnScreen()
    {
        Rect screen = new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        if (_viewModel.WidgetLeft is { } left && _viewModel.WidgetTop is { } top
            && screen.Contains(new Point(left + 20, top + 20)))
        {
            Left = left;
            Top = top;
            return;
        }

        Rect work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 16;
        Top = work.Bottom - ActualHeight - 16;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        DragMove();
        _viewModel.SetWidgetPosition(Left, Top);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => _viewModel.ShowWidget = false;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 solo lo oculta; se cierra de verdad al salir de la app.
        if (!_allowClose)
        {
            e.Cancel = true;
            _viewModel.ShowWidget = false;
        }

        base.OnClosing(e);
    }
}
