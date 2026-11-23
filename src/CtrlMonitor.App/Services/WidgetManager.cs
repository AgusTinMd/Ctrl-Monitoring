using System.ComponentModel;
using CtrlMonitor.App.ViewModels;
using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.App.Services;

/// <summary>Muestra u oculta el widget según la opción de Ajustes.</summary>
internal sealed class WidgetManager : IDisposable
{
    private readonly MainViewModel _viewModel;
    private WidgetWindow? _window;

    public WidgetManager(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Apply();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowWidget))
            Apply();
    }

    private void Apply()
    {
        try
        {
            if (_viewModel.ShowWidget)
            {
                _window ??= new WidgetWindow(_viewModel);
                _window.Show();
            }
            else
            {
                _window?.Hide();
            }
        }
        catch (Exception ex)
        {
            // El widget es opcional: si falla, la app sigue igual.
            AppLog.Error("No se pudo mostrar el widget", ex);
        }
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_window is not null)
        {
            _window.AllowClose();
            _window.Close();
            _window = null;
        }
    }
}
