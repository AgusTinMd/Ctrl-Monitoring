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
    private readonly MenuItem _widgetItem;
    private bool _disposed;
    private string? _tooltip;
    private System.Drawing.Icon? _drawnIcon;
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

        // Atajo para mostrar u ocultar el widget de escritorio.
        _widgetItem = new MenuItem { Header = Loc.T("tray.widget"), IsCheckable = true, IsChecked = _viewModel.ShowWidget };
        _widgetItem.Click += (_, _) => _viewModel.ShowWidget = _widgetItem.IsChecked;
        menu.Items.Add(_widgetItem);

        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/Assets/fan.ico");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();

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
        _viewModel.UpdateFound += OnUpdateFound;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        UpdateTooltip();
        UpdateIcon();
    }

    /// <summary>Aviso de Windows desde la bandeja cuando hay una versión nueva.</summary>
    private void OnUpdateFound(object? sender, CtrlMonitor.Core.Diagnostics.ReleaseInfo release)
    {
        try
        {
            _icon.ShowNotification(
                "Ctrl Monitor",
                Loc.F("tray.updateAvailable", CtrlMonitor.Core.Diagnostics.UpdateCheck.Display(release.Version)));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Bandeja: no se pudo mostrar el aviso de versión nueva: " + ex.Message);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _openItem.Header = Loc.T("tray.open");
        _widgetItem.Header = Loc.T("tray.widget");
        _exitItem.Header = Loc.T("tray.exit");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowWidget))
            _widgetItem.IsChecked = _viewModel.ShowWidget;

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

        // Al volver al logo con la opción apagada no hace falta tocar nada si
        // nunca se cambió el ícono.
        if (!show && _iconKey is null)
        {
            _iconKey = key;
            return;
        }

        try
        {
            // H.NotifyIcon no acepta imágenes generadas en memoria como
            // IconSource: se le pasa un ícono de Windows ya armado.
            System.Drawing.Icon next = show
                ? TrayTemperatureIcon.RenderIcon(temperature!.Value)
                : TrayTemperatureIcon.DefaultIcon();

            System.Drawing.Icon? previous = _drawnIcon;
            _icon.Icon = next;
            _drawnIcon = next;
            previous?.Dispose();

            _iconKey = key;
            _iconFailing = false;
        }
        catch (Exception ex)
        {
            // Un ícono que no se puede dibujar nunca debe cerrar la app.
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
        _viewModel.UpdateFound -= OnUpdateFound;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;

        try
        {
            _icon.Dispose();
            _drawnIcon?.Dispose();
        }
        catch
        {
            // best effort
        }
    }
}
