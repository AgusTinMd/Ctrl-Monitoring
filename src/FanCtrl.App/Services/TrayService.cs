using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using FanCtrl.App.ViewModels;
using H.NotifyIcon;

namespace FanCtrl.App.Services;

/// <summary>Icono de bandeja con acceso rápido a la ventana y salida segura.</summary>
public sealed class TrayService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Action _showWindow;
    private readonly Action _exit;
    private readonly TaskbarIcon _icon;
    private bool _disposed;

    public TrayService(MainViewModel viewModel, Action showWindow, Action exit)
    {
        _viewModel = viewModel;
        _showWindow = showWindow;
        _exit = exit;

        var menu = new ContextMenu();

        var openItem = new MenuItem
        {
            Header = "Abrir FanCtrl Monitor",
            FontWeight = System.Windows.FontWeights.SemiBold
        };
        openItem.Click += (_, _) => _showWindow();

        var exitItem = new MenuItem { Header = "Salir" };
        exitItem.Click += (_, _) => _exit();

        menu.Items.Add(openItem);
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
            ToolTipText = "FanCtrl Monitor",
            ContextMenu = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(_showWindow)
        };

        _icon.TrayMouseDoubleClick += (_, _) => _showWindow();
        _icon.ForceCreate(false);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateTooltip();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CpuTemperature)
            or nameof(MainViewModel.GpuTemperature))
        {
            UpdateTooltip();
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

        _icon.ToolTipText = $"FanCtrl Monitor · {cpu} · {gpu}";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

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
