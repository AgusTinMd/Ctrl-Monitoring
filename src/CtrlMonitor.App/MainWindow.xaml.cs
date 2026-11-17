using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CtrlMonitor.App.ViewModels;

namespace CtrlMonitor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
