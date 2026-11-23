using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CtrlMonitor.App.Services;
using CtrlMonitor.Core.Diagnostics;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Aviso opcional de versión nueva (desactivado por defecto).</summary>
public partial class MainViewModel
{
    /// <summary>Cada cuánto se vuelve a consultar mientras la app sigue abierta.</summary>
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(12);

    private DispatcherTimer? _updateTimer;
    private ReleaseInfo? _latestRelease;
    private bool _checkingUpdates;

    /// <summary>Se dispara una vez por versión nueva, para el aviso de la bandeja.</summary>
    public event EventHandler<ReleaseInfo>? UpdateFound;

    /// <summary>Avisar cuando haya una versión nueva en GitHub.</summary>
    public bool CheckForUpdates
    {
        get => _config.CheckForUpdates;
        set
        {
            if (_config.CheckForUpdates == value)
                return;

            _config.CheckForUpdates = value;
            OnPropertyChanged();
            SaveSoon();

            if (value)
                _ = CheckForUpdatesAsync();
            else
                ClearUpdate();
        }
    }

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _updateText = string.Empty;

    /// <summary>Consulta GitHub si el usuario lo activó. Nunca lanza excepciones.</summary>
    public async Task CheckForUpdatesAsync()
    {
        EnsureUpdateTimer();

        if (!_config.CheckForUpdates || _checkingUpdates || _disposed)
            return;

        _checkingUpdates = true;

        try
        {
            ReleaseInfo? latest = await UpdateService.GetLatestAsync();

            if (_disposed || !_config.CheckForUpdates)
                return;

            if (latest is null || !UpdateCheck.IsNewer(latest.Version, UpdateService.CurrentVersion))
            {
                ClearUpdate();
                return;
            }

            _latestRelease = latest;
            UpdateAvailable = true;
            RefreshUpdateText();
            AppLog.Info($"Hay una versión nueva: {latest.Tag}.");

            // El aviso de la bandeja sale una sola vez por versión.
            if (!string.Equals(_config.LastNotifiedUpdate, latest.Tag, StringComparison.OrdinalIgnoreCase))
            {
                _config.LastNotifiedUpdate = latest.Tag;
                SaveSoon();
                UpdateFound?.Invoke(this, latest);
            }
        }
        finally
        {
            _checkingUpdates = false;
        }
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        if (_latestRelease is { } release)
            UpdateService.OpenReleasePage(release.Url);
    }

    private void ClearUpdate()
    {
        _latestRelease = null;
        UpdateAvailable = false;
        UpdateText = string.Empty;
    }

    private void RefreshUpdateText()
    {
        if (_latestRelease is { } release)
            UpdateText = Loc.F("settings.updateAvailable", UpdateCheck.Display(release.Version));
    }

    private void EnsureUpdateTimer()
    {
        if (_updateTimer is not null)
            return;

        _updateTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = UpdateInterval };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync();
        _updateTimer.Start();
    }

    private void StopUpdateTimer() => _updateTimer?.Stop();
}
