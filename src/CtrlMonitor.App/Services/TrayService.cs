using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using CtrlMonitor.App.ViewModels;
using CtrlMonitor.Core.Diagnostics;
using CtrlMonitor.Core.Localization;
using H.NotifyIcon;

namespace CtrlMonitor.App.Services;

/// <summary>Icono de bandeja con acceso rápido a la ventana y salida segura.</summary>
public sealed class TrayService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Action _showWindow;
    private readonly Action _exit;
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _openItem;
    private readonly MenuItem _exitItem;
    private bool _disposed;
    private string? _tooltip;
    private readonly ImageSource _defaultIcon;
    private string? _iconKey;
    private bool _iconFailing;
    private bool _tooltipFailing;

    public TrayService(MainViewModel viewModel, Action showWindow, Action exit)
    {
        _viewModel = viewModel;
        _showWindow = showWindow;
        _exit = exit;

        var menu = new ContextMenu();

        _openItem = new MenuItem
        {
            Header = Loc.T("tray.open"),
            FontWeight = System.Windows.FontWeights.SemiBold
        };
        _openItem.Click += (_, _) => _showWindow();

        _exitItem = new MenuItem { Header = Loc.T("tray.exit") };
        _exitItem.Click += (_, _) => _exit();

        MenuItem openItem = _openItem;
        MenuItem exitItem = _exitItem;

        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/Assets/fan.ico");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();

        _defaultIcon = image;

        _icon = new TaskbarIcon
        {
            IconSource = image,
            ToolTipText = "Ctrl Monitor",
            ContextMenu = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(_showWindow)
        };

        _icon.TrayMouseDoubleClick += (_, _) => _showWindow();

        // En reposo, pasar el mouse por el icono relee los sensores para que
        // el tooltip no muestre temperaturas viejas.
        _icon.TrayMouseMove += (_, _) => _viewModel.PeekSensors();
        _icon.ForceCreate(false);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        UpdateTooltip();
        UpdateIcon();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _openItem.Header = Loc.T("tray.open");
        _exitItem.Header = Loc.T("tray.exit");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CpuTemperature)
            or nameof(MainViewModel.GpuTemperature))
        {
            UpdateTooltip();
            UpdateIcon();
        }
        else if (e.PropertyName is nameof(MainViewModel.TrayShowTemperature)
                 or nameof(MainViewModel.TrayTemperatureIsCpu)
                 or nameof(MainViewModel.TrayTemperatureIsGpu))
        {
            UpdateIcon();
        }
    }

    private void UpdateTooltip()
    {
        string cpu = _viewModel.CpuTemperature is { } cpuValue
            ? $"CPU {cpuValue:0} °C"
            : "CPU —";

        string gpu = _viewModel.GpuTemperature is { } gpuValue
            ? $"GPU {gpuValue:0} °C"
            : "GPU —";

        string text = $"Ctrl Monitor · {cpu} · {gpu}";

        if (_disposed || text == _tooltip)
            return;

        // Windows actualiza el icono de la bandeja a través del Explorador. Si
        // está ocupado o reiniciándose, Shell_NotifyIcon falla y H.NotifyIcon
        // lanza «UpdateToolTip failed». Un tooltip viejo no justifica cerrar la
        // app: se ignora y se reintenta con la próxima lectura.
        try
        {
            _icon.ToolTipText = text;
            _tooltip = text;

            if (_tooltipFailing)
            {
                _tooltipFailing = false;
                AppLog.Info("Bandeja: el tooltip volvió a actualizarse.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException
                                       or System.ComponentModel.Win32Exception)
        {
            if (!_tooltipFailing)
            {
                _tooltipFailing = true;
                AppLog.Warn("Bandeja: no se pudo actualizar el tooltip: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Con la opción activa, el ícono muestra la temperatura del CPU o la GPU
    /// en el color de la escala térmica. Solo se redibuja si cambia el número
    /// o el color, y un error del Explorador no cierra la app.
    /// </summary>
    private void UpdateIcon()
    {
        if (_disposed)
            return;

        double? temperature = _viewModel.TrayTemperatureIsGpu ? _viewModel.GpuTemperature : _viewModel.CpuTemperature;
        bool show = _viewModel.TrayShowTemperature && temperature is not null;
        string key = show ? TrayTemperatureIcon.Key(temperature!.Value) : "default";

        if (key == _iconKey)
            return;

        try
        {
            _icon.IconSource = show ? TrayTemperatureIcon.Render(temperature!.Value) : _defaultIcon;
            _iconKey = key;
            _iconFailing = false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException
                                       or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            if (!_iconFailing)
            {
                _iconFailing = true;
                AppLog.Warn("Bandeja: no se pudo actualizar el ícono: " + ex.Message);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;

        try
        {
            _icon.Dispose();
        }
        catch
        {
            // best effort
        }
    }
}
