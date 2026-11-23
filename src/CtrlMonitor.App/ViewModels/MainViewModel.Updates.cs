using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CtrlMonitor.App.Services;
using CtrlMonitor.Core.Diagnostics;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Aviso de versión nueva (activado por defecto; se puede apagar en Ajustes).</summary>
public partial class MainViewModel
{
    /// <summary>
    /// Cada cuánto se revisa si toca consultar: la consulta en sí sale como
    /// mucho una vez por semana (<see cref="UpdateCheck.Interval"/>).
    /// </summary>
    private static readonly TimeSpan UpdateTimerInterval = TimeSpan.FromHours(6);

    private DispatcherTimer? _updateTimer;
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

            // Al activarla se consulta enseguida; después, una vez por semana.
            if (value)
                _ = CheckForUpdatesAsync(force: true);
            else
                ShowKnownUpdate();
        }
    }

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _updateText = string.Empty;

    /// <summary>
    /// Consulta GitHub si el usuario lo activó y pasó una semana desde la
    /// última consulta (o si <paramref name="force"/>). Nunca lanza excepciones.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool force = false)
    {
        EnsureUpdateTimer();
        ShowKnownUpdate();

        if (!_config.CheckForUpdates || _checkingUpdates || _disposed)
            return;

        if (!force && !UpdateCheck.IsDue(_config.LastUpdateCheckUtc, DateTime.UtcNow))
            return;

        _checkingUpdates = true;

        try
        {
            ReleaseInfo? latest = await UpdateService.GetLatestAsync();

            if (_disposed || !_config.CheckForUpdates)
                return;

            _config.LastUpdateCheckUtc = DateTime.UtcNow;

            if (latest is not null)
            {
                _config.LatestUpdateTag = latest.Tag;
                _config.LatestUpdateUrl = latest.Url;
            }

            SaveSoon();
            ShowKnownUpdate();

            if (latest is null || !UpdateCheck.IsNewer(latest.Version, UpdateService.CurrentVersion))
                return;

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
        if (KnownUpdate() is { } release)
            UpdateService.OpenReleasePage(release.Url);
    }

    /// <summary>
    /// Entre consulta y consulta se muestra la última versión conocida, si es
    /// más nueva que la instalada: así el aviso de Ajustes no desaparece al
    /// reiniciar la app.
    /// </summary>
    private void ShowKnownUpdate()
    {
        ReleaseInfo? release = _config.CheckForUpdates ? KnownUpdate() : null;

        UpdateAvailable = release is not null;
        UpdateText = release is null ? string.Empty : Loc.F("settings.updateAvailable", UpdateCheck.Display(release.Version));
    }

    private ReleaseInfo? KnownUpdate()
    {
        Version? version = UpdateCheck.ParseTag(_config.LatestUpdateTag);

        return version is not null
               && UpdateCheck.IsNewer(version, UpdateService.CurrentVersion)
               && UpdateCheck.IsRepositoryUrl(_config.LatestUpdateUrl)
            ? new ReleaseInfo(version, _config.LatestUpdateTag, _config.LatestUpdateUrl)
            : null;
    }

    private void RefreshUpdateText() => ShowKnownUpdate();

    private void EnsureUpdateTimer()
    {
        if (_updateTimer is not null)
            return;

        _updateTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = UpdateTimerInterval };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync();
        _updateTimer.Start();
    }

    private void StopUpdateTimer() => _updateTimer?.Stop();
}
