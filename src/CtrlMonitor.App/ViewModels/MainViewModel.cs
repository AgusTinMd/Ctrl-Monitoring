using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CtrlMonitor.App.Models;
using CtrlMonitor.App.Services;
using CtrlMonitor.Core.Configuration;
using CtrlMonitor.Core.Diagnostics;
using CtrlMonitor.Core.Hardware;
using CtrlMonitor.Core.Inventory;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Opción del selector de idioma de Ajustes.</summary>
public sealed class LanguageOption : ObservableObject
{
    public LanguageOption(string code, string? nativeName)
    {
        Code = code;
        _nativeName = nativeName;
    }

    private readonly string? _nativeName;

    /// <summary>«auto», «es», «en» o «pt-BR»; es lo que se guarda en la configuración.</summary>
    public string Code { get; }

    /// <summary>
    /// Los idiomas se muestran en su propio idioma (para encontrarlos aunque la
    /// app esté en otro); «Automático» se traduce.
    /// </summary>
    public string DisplayName => _nativeName ?? Loc.T("language.auto");

    public void RefreshLanguage() => OnPropertyChanged(nameof(DisplayName));
}

/// <summary>
/// Estado de la app de monitorización. No escribe PWM: solo consume los
/// snapshots de <see cref="HardwareService"/> y los expone a la UI.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigStore _store;
    private readonly AppConfig _config;
    private readonly HardwareService _hardware;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _saveTimer;
    private readonly Dictionary<string, SensorGroupViewModel> _groupsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FanViewModel> _fansById = new(StringComparer.Ordinal);
    private readonly NetworkUsageReader _networkUsage = new();
    private readonly DiskUsageReader _diskUsage = new();
    private IReadOnlyDictionary<string, DiskActivity> _disks = new Dictionary<string, DiskActivity>();

    /// <summary>
    /// Tarjetas guardadas cuya métrica todavía no existe (discos que se
    /// detectan unos segundos después de arrancar), con su posición original.
    /// </summary>
    private readonly List<(int Index, string Key)> _pendingChartKeys = new();

    /// <summary>Métricas fijas más una por cada disco detectado.</summary>
    public IReadOnlyList<ChartMetric> AllChartMetrics { get; private set; } = ChartMetrics.Base;

    private int _lastActiveFanCount = -1;

    private MachineInventory? _inventory;

    private bool _disposed;

    public MainViewModel(ConfigStore store, AppConfig config, HardwareService hardware)
    {
        _store = store;
        _config = config;
        _hardware = hardware;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _saveTimer.Tick += OnSaveTimerTick;

        if (_config.RunAtStartup && !StartupService.IsEnabled())
            _config.RunAtStartup = false;

        VisibleFans = new ListCollectionView(Fans)
        {
            Filter = item => !ShowOnlyActiveFans || (item is FanViewModel fan && fan.Rpm > 0)
        };

        InitializeChartPanels();

        _hardware.SnapshotUpdated += OnSnapshotUpdated;

        LanguageOptions = new[] { new LanguageOption(AppLanguage.Auto, null) }
            .Concat(AppLanguage.Supported.Select(language => new LanguageOption(language.Code, language.NativeName)))
            .ToList();
        Loc.Instance.LanguageChanged += OnLanguageChanged;

        SaveStatus = Loc.T("save.loaded");
        UpdateFromSnapshot(_hardware.Latest);
        UpdateStatus();

        _ = LoadMachineInventoryAsync();

        AppLog.Info(
            $"Monitor iniciado. Equipo={_hardware.MotherboardName}, CPU={_hardware.CpuName}, GPU={_hardware.GpuName}");
    }

    public ObservableCollection<SensorGroupViewModel> Groups { get; } = new();

    /// <summary>Últimos 10 minutos de muestras para el modo gráfico.</summary>
    public ObservableCollection<ChartSample> ChartSamples { get; } = new();

    /// <summary>Tarjetas del modo gráfico; cada una puede elegir su métrica.</summary>
    public ObservableCollection<ChartPanelViewModel> ChartPanels { get; } = new();

    private IReadOnlyList<ChartMetric> _availableChartMetrics = Array.Empty<ChartMetric>();
    private bool _chartsInitialized;

    /// <summary>Métricas que todavía no están visibles y se pueden agregar.</summary>
    public IReadOnlyList<ChartMetric> AvailableChartMetrics
    {
        get => _availableChartMetrics;
        private set
        {
            if (ReferenceEquals(_availableChartMetrics, value))
                return;

            _availableChartMetrics = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAvailableChartMetrics));
        }
    }

    public bool HasAvailableChartMetrics => AvailableChartMetrics.Count > 0;

    public bool IsChartPanelsEmpty => ChartPanels.Count == 0;

    [ObservableProperty]
    private ChartMetric? _newChartMetric;

    /// <summary>Canales de ventilador detectados, con RPM y PWM del BIOS.</summary>
    public ObservableCollection<FanViewModel> Fans { get; } = new();

    /// <summary>Vista filtrada de los ventiladores según «Solo activos».</summary>
    public ListCollectionView VisibleFans { get; private set; } = null!;

    public string MotherboardName => _hardware.MotherboardName;

    /// <summary>Marca de la placa detectada a partir de su nombre, o <c>null</c>.</summary>
    private HardwareBrand? BoardBrand => BoardBrands.Detect(_hardware.MotherboardName);

    /// <summary>Logo de la marca de la placa, si hay uno en Assets.</summary>
    public string? BoardLogoUri => LogoUri(BoardBrand);

    public bool HasBoardLogo => BoardLogoUri is not null;

    /// <summary>Nombre de la marca cuando no hay logo; así no se muestra el de otra marca.</summary>
    public string? BoardBrandText => BoardBrand is { LogoFile: null } brand ? brand.Name : null;

    public bool HasBoardBrandText => BoardBrandText is not null;

    public string CpuName => _hardware.CpuName;

    /// <summary>Logo de la marca del CPU (Intel o AMD), si se reconoce.</summary>
    public string? CpuLogoUri => LogoUri(CpuBrands.Detect(_hardware.CpuName));

    public bool HasCpuLogo => CpuLogoUri is not null;

    private static string? LogoUri(HardwareBrand? brand) => brand?.LogoFile is { } file
        ? $"pack://application:,,,/Assets/{file}"
        : null;

    public string GpuName => _hardware.GpuName;

    public string SuperIoName => _hardware.SuperIoName;

    public string DriverVersion => _hardware.DriverVersion;

    public string DriverShortText => Loc.T(_hardware.PawnIoInstalled ? "driver.ready" : "driver.missing");

    public Brush DriverBrush => _hardware.PawnIoInstalled ? UiBrushes.Success : UiBrushes.Danger;

    public string AdminText => Loc.T(_hardware.IsAdministrator ? "common.yes" : "common.no");

    public string ConfigPath => _store.ConfigPath;

    public string LogDirectory => AppPaths.LogDirectory;

    /// <summary>
    /// Versión del ejecutable. La pone el tag vX.Y.Z al publicar; una
    /// compilación local queda en 0.0.0 y se muestra como «(dev)».
    /// </summary>
    public string VersionText { get; } = $"Ctrl Monitor {BuildShortVersion()}";

    /// <summary>Versión corta al final de la barra superior: «v2.8.0» o «dev».</summary>
    public string ShortVersionText { get; } = BuildShortVersion();

    private static string BuildShortVersion()
    {
        Version? version = typeof(MainViewModel).Assembly.GetName().Version;

        if (version is null || (version.Major == 0 && version.Minor == 0 && version.Build <= 0))
            return "dev";

        return $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    [ObservableProperty]
    private double? _cpuTemperature;

    [ObservableProperty]
    private double? _cpuMaxTemperature;

    [ObservableProperty]
    private double? _cpuPower;

    [ObservableProperty]
    private double? _cpuLoad;

    [ObservableProperty]
    private int? _cpuFanRpm;

    [ObservableProperty]
    private double? _gpuTemperature;

    [ObservableProperty]
    private double? _gpuHotspot;

    [ObservableProperty]
    private double? _gpuPower;

    [ObservableProperty]
    private double? _gpuLoad;

    [ObservableProperty]
    private int? _gpuFanRpm;

    [ObservableProperty]
    private double? _boardTemperature;

    [ObservableProperty]
    private double? _ramUsedGb;

    [ObservableProperty]
    private double? _ramTotalGb;

    [ObservableProperty]
    private double? _ramPercent;

    [ObservableProperty]
    private double? _vramUsedGb;

    [ObservableProperty]
    private double? _vramTotalGb;

    [ObservableProperty]
    private double? _vramPercent;

    [ObservableProperty]
    private string _vramText = "—";

    [ObservableProperty]
    private double? _netDownMbs;

    [ObservableProperty]
    private double? _netUpMbs;

    /// <summary>Frecuencia promedio de los núcleos del CPU, en GHz.</summary>
    [ObservableProperty]
    private double? _cpuClockGhz;

    [ObservableProperty]
    private string _lastUpdateText = Loc.T("chart.waiting");

    [ObservableProperty]
    private string _statusTitle = Loc.T("status.starting");

    [ObservableProperty]
    private string _statusDetail = Loc.T("status.readingSensors");

    [ObservableProperty]
    private Brush _statusBrush = UiBrushes.Muted;

    [ObservableProperty]
    private Brush _statusSoftBrush = UiBrushes.NeutralSoft;

    [ObservableProperty]
    private string _saveStatus = string.Empty;

    [ObservableProperty]
    private int _chartWindowMinutes = 5;

    [ObservableProperty]
    private string _fanSummaryText = Loc.T("fans.initial");

    /// <summary>«Solo activos» viene marcado: los canales a 0 RPM suelen ser cabezales vacíos.</summary>
    [ObservableProperty]
    private bool _showOnlyActiveFans = true;

    [ObservableProperty]
    private bool _showActiveFilterEmpty;

    [ObservableProperty]
    private string _ramText = Loc.T("common.detecting");

    [ObservableProperty]
    private string _disksText = Loc.T("common.detecting");

    [ObservableProperty]
    private string _disksTooltip = string.Empty;

    partial void OnShowOnlyActiveFansChanged(bool value)
    {
        if (value)
            VisibleFans.Refresh();

        _lastActiveFanCount = Fans.Count(fan => fan.Rpm > 0);
        ShowActiveFilterEmpty = value && Fans.Count > 0 && _lastActiveFanCount == 0;
    }

    public int RefreshIntervalMs
    {
        get => _config.RefreshIntervalMs;
        set
        {
            int clamped = Math.Clamp(value, 500, 10000);

            if (clamped == _config.RefreshIntervalMs)
                return;

            _config.RefreshIntervalMs = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RefreshIntervalText));
            _hardware.StartMonitoring(TimeSpan.FromMilliseconds(clamped));
            SaveSoon();
        }
    }

    public string RefreshIntervalText => $"{RefreshIntervalMs / 1000.0:0.#} s";

    public bool StartMinimized
    {
        get => _config.StartMinimized;
        set
        {
            if (_config.StartMinimized == value)
                return;

            _config.StartMinimized = value;
            OnPropertyChanged();
            SaveSoon();
        }
    }

    public bool MinimizeToTray
    {
        get => _config.MinimizeToTray;
        set
        {
            if (_config.MinimizeToTray == value)
                return;

            _config.MinimizeToTray = value;
            OnPropertyChanged();
            SaveSoon();
        }
    }

    public bool RunAtStartup
    {
        get => _config.RunAtStartup;
        set
        {
            if (_config.RunAtStartup == value)
                return;

            if (StartupService.SetEnabled(value, out string? error))
            {
                _config.RunAtStartup = value;
                OnPropertyChanged();
                SaveSoon();
            }
            else
            {
                SaveStatus = Loc.F("save.startupError", error);

                // El binding TwoWay ya marcó el check; forzamos que vuelva al valor real.
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Opciones del selector de idioma: automático y los idiomas soportados.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    /// <summary>
    /// Preferencia de idioma («auto», «es», «en» o «pt-BR»). Cambiarla traduce
    /// la interfaz al instante y la guarda en la configuración.
    /// </summary>
    public string Language
    {
        get => _config.Language;
        set
        {
            string normalized = AppLanguage.NormalizeSetting(value);

            if (string.Equals(_config.Language, normalized, StringComparison.Ordinal))
                return;

            _config.Language = normalized;
            OnPropertyChanged();
            Loc.Instance.SetLanguage(normalized);
            SaveSoon();
        }
    }

    public bool ShowNavigation
    {
        get => _config.ShowNavigation;
        set
        {
            if (_config.ShowNavigation == value)
                return;

            _config.ShowNavigation = value;
            OnPropertyChanged();
            SaveSoon();
        }
    }

    [RelayCommand]
    private void ToggleNavigation() => ShowNavigation = !ShowNavigation;

    [RelayCommand]
    private void ClearChart() => ChartSamples.Clear();

    [RelayCommand]
    private void AddChartPanel()
    {
        if (NewChartMetric is not { } metric)
            return;

        if (ChartPanels.Any(panel =>
                string.Equals(panel.SelectedMetric.Key, metric.Key, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        AddChartPanelCore(metric);
        ChartsChanged();
    }

    [RelayCommand]
    private void RemoveChartPanel(ChartPanelViewModel? panel)
    {
        if (panel is null)
            return;

        panel.PropertyChanged -= OnChartPanelPropertyChanged;
        ChartPanels.Remove(panel);
        ChartsChanged();
    }

    /// <summary>
    /// Mueve una tarjeta del modo gráfico a otra posición y guarda el nuevo
    /// orden. Se usa al arrastrar y soltar las tarjetas.
    /// </summary>
    public void MoveChartPanel(ChartPanelViewModel panel, int newIndex)
    {
        int oldIndex = ChartPanels.IndexOf(panel);

        if (oldIndex < 0 || newIndex < 0 || newIndex >= ChartPanels.Count || oldIndex == newIndex)
            return;

        ChartPanels.Move(oldIndex, newIndex);
        ChartsChanged();
    }

    [RelayCommand]
    private void SaveNow()
    {
        _saveTimer.Stop();

        SaveStatus = _store.Save(_config)
            ? Loc.F("save.saved", DateTime.Now.ToString("HH:mm:ss"))
            : Loc.F("save.error", _store.LastError);
    }

    [RelayCommand]
    private void OpenConfigFolder() => OpenFolder(_store.ConfigPath, selectFile: true);

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(AppPaths.LogDirectory, selectFile: false);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimerTick;
        _hardware.SnapshotUpdated -= OnSnapshotUpdated;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;

        _diskUsage.Dispose();
        SaveNow();
        AppLog.Info("Monitor de hardware cerrado.");
    }

    private async Task LoadMachineInventoryAsync()
    {
        MachineInventory inventory = await Task.Run(MachineInventoryReader.Read);

        if (_disposed)
            return;

        _inventory = inventory;
        ApplyInventoryTexts();
    }

    private void ApplyInventoryTexts()
    {
        if (_inventory is not { } inventory)
        {
            RamText = Loc.T("common.detecting");
            DisksText = Loc.T("common.detecting");
            return;
        }

        RamText = FormatMemory(inventory);
        DisksText = FormatDisks(inventory);
        DisksTooltip = FormatDiskDetails(inventory);
    }

    /// <summary>
    /// Vuelve a traducir los textos que arma el ViewModel. Los que se
    /// recalculan en cada refresco (última actualización, resumen de
    /// ventiladores, VRAM) se actualizan solos en el próximo ciclo.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        OnPropertyChanged(nameof(DriverShortText));
        OnPropertyChanged(nameof(AdminText));

        foreach (LanguageOption option in LanguageOptions)
            option.RefreshLanguage();

        foreach (ChartPanelViewModel panel in ChartPanels)
            panel.RefreshLanguage();

        foreach (FanViewModel fan in Fans)
            fan.RefreshLanguage();

        foreach (SensorGroupViewModel group in Groups)
            group.RefreshLanguage();

        ApplyInventoryTexts();

        // Solo textos: no se vuelve a leer la red ni se agrega una muestra al
        // gráfico, para no meter picos falsos.
        HardwareSnapshot latest = _hardware.Latest;
        UpdateVideoMemory(latest.Sensors
            .Where(s => s.HardwareType is "GpuAmd" or "GpuNvidia")
            .ToList());
        UpdateFans(latest.Sensors);
        UpdateStatus();
        RefreshChartPanels();
    }

    private static string FormatMemory(MachineInventory inventory)
    {
        if (inventory.TotalMemoryBytes <= 0)
            return Loc.T("common.notAvailable");

        string text = FormatBytes(inventory.TotalMemoryBytes);

        if (inventory.Modules.Count > 0)
        {
            int count = inventory.Modules.Count;
            text += " · " + Loc.F(count == 1 ? "ram.module.one" : "ram.module.many", count);

            int speed = inventory.Modules.Max(module => module.SpeedMhz);

            if (speed > 0)
                text += $" · {speed} MHz";
        }

        return text;
    }

    private static string FormatDisks(MachineInventory inventory)
    {
        if (inventory.Disks.Count == 0)
            return Loc.T("common.notAvailable");

        int count = inventory.Disks.Count;
        long total = inventory.Disks.Sum(disk => disk.SizeBytes);

        return Loc.F(count == 1 ? "disks.unit.one" : "disks.unit.many", count) + " · " + FormatBytes(total);
    }

    private static string FormatDiskDetails(MachineInventory inventory) =>
        string.Join(
            Environment.NewLine,
            inventory.Disks.Select(disk => $"{disk.Model} · {FormatBytes(disk.SizeBytes)}"));

    private static string FormatBytes(long bytes)
    {
        const double Gigabyte = 1024d * 1024d * 1024d;
        const double Terabyte = Gigabyte * 1024d;

        return bytes >= Terabyte
            ? $"{bytes / Terabyte:0.#} TB"
            : $"{bytes / Gigabyte:0.#} GB";
    }

    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        SaveNow();
    }

    private void SaveSoon()
    {
        if (_disposed)
            return;

        SaveStatus = Loc.T("save.pending");
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSnapshotUpdated(object? sender, HardwareSnapshot snapshot)
    {
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_disposed)
                return;

            UpdateFromSnapshot(snapshot);
        }));
    }

    private void UpdateFromSnapshot(HardwareSnapshot snapshot)
    {
        UpdateMemory();
        UpdateNetwork();
        UpdateDisks();

        if (snapshot.Sensors.Count == 0)
        {
            UpdateVideoMemory(Array.Empty<SensorReading>());
            RefreshChartPanels();
            LastUpdateText = Loc.T("update.noSensors");
            UpdateStatus();
            return;
        }

        var cpu = snapshot.Sensors.Where(s => s.HardwareType == "Cpu").ToList();
        var gpu = snapshot.Sensors.Where(s => s.HardwareType is "GpuAmd" or "GpuNvidia").ToList();
        var board = snapshot.Sensors
            .Where(s => s.HardwareType is "Motherboard" or "SuperIO")
            .ToList();

        CpuTemperature = FindValue(cpu, SensorKind.Temperature, "CPU Package", "Core Max")
                         ?? FirstValue(cpu, SensorKind.Temperature);
        CpuMaxTemperature = FindValue(cpu, SensorKind.Temperature, "Core Max")
                            ?? MaxValue(cpu, SensorKind.Temperature);
        CpuPower = FindValue(cpu, SensorKind.Power, "CPU Package", "Package")
                   ?? MaxValue(cpu, SensorKind.Power);
        CpuClockGhz = AverageCoreClockGhz(cpu);
        CpuLoad = FindValue(cpu, SensorKind.Load, "CPU Total", "Total")
                  ?? FirstValue(cpu, SensorKind.Load);

        GpuTemperature = FindValue(gpu, SensorKind.Temperature, "GPU Core")
                         ?? FirstValue(gpu, SensorKind.Temperature);
        GpuHotspot = FindValue(gpu, SensorKind.Temperature, "GPU Hot Spot", "Hot Spot");
        GpuPower = FindValue(gpu, SensorKind.Power, "GPU Package", "GPU Power")
                   ?? MaxValue(gpu, SensorKind.Power);
        GpuLoad = FindValue(gpu, SensorKind.Load, "GPU Core")
                  ?? FirstValue(gpu, SensorKind.Load);
        GpuFanRpm = ToInt(FindValue(gpu, SensorKind.Fan, "GPU Fan")
                          ?? FirstValue(gpu, SensorKind.Fan));

        UpdateVideoMemory(gpu);

        BoardTemperature = FindValue(board, SensorKind.Temperature, "Temperature #1", "System")
                           ?? FirstValue(board, SensorKind.Temperature);

        CpuFanRpm = FindCpuFan(snapshot);
        AppendChartSample(snapshot.TimestampUtc);
        RefreshChartPanels();

        UpdateGroups(snapshot);
        UpdateFans(snapshot.Sensors);

        LastUpdateText = Loc.F("update.updated", DateTime.Now.ToString("HH:mm:ss"));
        UpdateStatus();
    }

    private void AppendChartSample(DateTime timestampUtc)
    {
        ChartSamples.Add(new ChartSample(
            timestampUtc,
            CpuTemperature,
            GpuTemperature,
            BoardTemperature,
            GpuHotspot,
            RamPercent,
            VramPercent,
            NetDownMbs,
            NetUpMbs,
            CpuLoad,
            GpuLoad,
            _disks.Count == 0 ? null : _disks));

        // El gráfico permite ventanas de hasta 10 minutos; conservamos ese
        // margen y descartamos lo más viejo para no crecer sin límite.
        DateTime cutoff = timestampUtc - TimeSpan.FromMinutes(10);

        while (ChartSamples.Count > 1 && ChartSamples[0].TimestampUtc < cutoff)
            ChartSamples.RemoveAt(0);
    }

    private void InitializeChartPanels()
    {
        List<string> configured = _config.ChartMetrics ?? new List<string>();

        for (int i = 0; i < configured.Count; i++)
        {
            string key = configured[i];
            ChartMetric? metric = FindChartMetric(key);

            if (metric is null && key.StartsWith(ChartMetrics.DiskKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _pendingChartKeys.Add((i, key));
                continue;
            }

            if (metric is null || ChartPanels.Any(panel =>
                    string.Equals(panel.SelectedMetric.Key, metric.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddChartPanelCore(metric);
        }

        // Si la configuración traía solo claves desconocidas, restauramos el
        // conjunto principal. Un arreglo vacío sí se respeta: el usuario quitó
        // todos los gráficos a propósito.
        if (configured.Count > 0 && ChartPanels.Count == 0 && _pendingChartKeys.Count == 0)
        {
            AddChartPanelCore(ChartMetrics.Cpu);
            AddChartPanelCore(ChartMetrics.Gpu);
            AddChartPanelCore(ChartMetrics.Ram);
        }

        _chartsInitialized = true;
        RefreshAvailableChartMetrics();
    }

    private void AddChartPanelCore(ChartMetric metric, int? index = null)
    {
        var panel = new ChartPanelViewModel(this, AllChartMetrics, metric);
        panel.PropertyChanged += OnChartPanelPropertyChanged;

        if (index is { } position)
            ChartPanels.Insert(position, panel);
        else
            ChartPanels.Add(panel);
    }

    private ChartMetric? FindChartMetric(string key) =>
        AllChartMetrics.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    private void OnChartPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChartPanelViewModel.SelectedMetric))
            ChartsChanged();
    }

    private void ChartsChanged()
    {
        RefreshAvailableChartMetrics();

        if (!_chartsInitialized)
            return;

        // Las tarjetas de disco que esperan a los contadores también se guardan.
        _config.ChartMetrics = ChartPanels
            .Select(panel => panel.SelectedMetric.Key)
            .Concat(_pendingChartKeys.Select(pending => pending.Key))
            .ToList();

        SaveSoon();
    }

    private void RefreshAvailableChartMetrics()
    {
        List<ChartMetric> available = AllChartMetrics
            .Where(metric => !ChartPanels.Any(panel =>
                string.Equals(panel.SelectedMetric.Key, metric.Key, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        AvailableChartMetrics = available;

        if (NewChartMetric is null || !available.Contains(NewChartMetric))
            NewChartMetric = available.FirstOrDefault();

        OnPropertyChanged(nameof(IsChartPanelsEmpty));
    }

    private void RefreshChartPanels()
    {
        foreach (ChartPanelViewModel panel in ChartPanels)
            panel.Refresh();
    }

    private void UpdateMemory()
    {
        MemoryUsage usage = MemoryUsageReader.Read();

        if (usage.TotalBytes <= 0)
            return;

        RamTotalGb = usage.TotalGb;
        RamUsedGb = usage.UsedGb;
        RamPercent = usage.UsedPercent;
    }

    private void UpdateVideoMemory(IReadOnlyList<SensorReading> gpu)
    {
        double? usedMb = FindValue(gpu, SensorKind.Memory,
            "GPU Memory Used", "D3D Dedicated Memory Used");
        double? totalMb = FindValue(gpu, SensorKind.Memory,
            "GPU Memory Total", "D3D Dedicated Memory Total");

        if (usedMb is not { } used || totalMb is not { } total || total <= 0)
        {
            VramUsedGb = null;
            VramTotalGb = null;
            VramPercent = null;
            VramText = "—";
            return;
        }

        VramUsedGb = used / 1024d;
        VramTotalGb = total / 1024d;
        VramPercent = Math.Clamp(used / total * 100d, 0, 100);
        VramText = Loc.F("vram.text", VramUsedGb, VramTotalGb, VramPercent);
    }

    private void UpdateDisks()
    {
        IReadOnlyList<DiskActivity> disks = _diskUsage.Read();

        // Mientras los contadores se preparan la lista viene vacía: se conserva
        // la anterior para no borrar los gráficos de disco por un instante.
        if (disks.Count == 0 && _disks.Count > 0)
            return;

        bool changed = disks.Count != _disks.Count || disks.Any(disk => !_disks.ContainsKey(disk.Id));
        _disks = disks.ToDictionary(disk => disk.Id, StringComparer.Ordinal);

        if (changed)
            RebuildChartMetrics();
    }

    /// <summary>Actividad actual de un disco, o <c>null</c> si no está conectado.</summary>
    public DiskActivity? GetDisk(string diskId) =>
        _disks.TryGetValue(diskId, out DiskActivity? disk) ? disk : null;

    /// <summary>
    /// Arma la lista de métricas con un gráfico por disco y la pasa a los
    /// selectores. Las tarjetas de disco guardadas que esperaban a que se
    /// detectaran los discos se agregan ahora, en su posición original.
    /// </summary>
    private void RebuildChartMetrics()
    {
        IEnumerable<ChartMetric> diskMetrics = _disks.Keys
            .OrderBy(DiskNames.Number)
            .Select(ChartMetrics.ForDisk);

        // Un disco desconectado deja su tarjeta; su métrica sigue en la lista
        // para que el selector no pierda la elección.
        IEnumerable<ChartMetric> orphaned = ChartPanels
            .Select(panel => panel.SelectedMetric)
            .Where(metric => metric.Key.StartsWith(ChartMetrics.DiskKeyPrefix, StringComparison.Ordinal));

        AllChartMetrics = ChartMetrics.Base.Concat(diskMetrics).Concat(orphaned).Distinct().ToList();

        AppLog.Info($"Discos para gráficos: {string.Join(", ", _disks.Keys.OrderBy(DiskNames.Number))}");

        foreach (ChartPanelViewModel panel in ChartPanels)
            panel.Options = AllChartMetrics;

        foreach ((int index, string key) in _pendingChartKeys.ToList())
        {
            ChartMetric? metric = FindChartMetric(key);

            if (metric is null)
                continue;

            _pendingChartKeys.Remove((index, key));

            if (!ChartPanels.Any(panel => panel.SelectedMetric == metric))
                AddChartPanelCore(metric, Math.Min(index, ChartPanels.Count));
        }

        RefreshAvailableChartMetrics();
    }

    private void UpdateNetwork()
    {
        NetworkUsage usage = _networkUsage.Read();
        NetDownMbs = usage.DownMegaBytesPerSecond;
        NetUpMbs = usage.UpMegaBytesPerSecond;
    }

    private void UpdateGroups(HardwareSnapshot snapshot)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (IGrouping<string, SensorReading> group in snapshot.Sensors.GroupBy(s => s.HardwareId))
        {
            seen.Add(group.Key);
            SensorReading first = group.First();

            if (!_groupsById.TryGetValue(group.Key, out SensorGroupViewModel? vm))
            {
                vm = new SensorGroupViewModel(group.Key, first.HardwareName, first.HardwareType);
                _groupsById[group.Key] = vm;
                Groups.Add(vm);
            }

            vm.Update(group.ToList());
        }

        foreach (string id in _groupsById.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Groups.Remove(_groupsById[id]);
            _groupsById.Remove(id);
        }
    }

    private void UpdateFans(IReadOnlyList<SensorReading> sensors)
    {
        var pwmByChannel = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (SensorReading sensor in sensors.Where(s => s.Kind == SensorKind.Pwm))
            pwmByChannel[ChannelKey(sensor.HardwareId, sensor.Name)] = sensor.Value;

        var readings = sensors
            .Where(s => s.Kind == SensorKind.Fan)
            .OrderBy(s => s.HardwareType == "SuperIO" ? 0 : 1)
            .ThenBy(s => s.Index)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (SensorReading reading in readings)
        {
            seen.Add(reading.Id);

            string channelKey = ChannelKey(reading.HardwareId, reading.Name);
            bool hasPwm = pwmByChannel.TryGetValue(channelKey, out double pwm);

            if (_fansById.TryGetValue(reading.Id, out FanViewModel? fan))
            {
                fan.Update(reading, hasPwm ? pwm : null);
            }
            else
            {
                fan = new FanViewModel(reading);
                fan.Update(reading, hasPwm ? pwm : null);
                _fansById[reading.Id] = fan;
                Fans.Add(fan);
            }
        }

        foreach (string id in _fansById.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Fans.Remove(_fansById[id]);
            _fansById.Remove(id);
        }

        int spinning = readings.Count(s => s.Value > 0);
        int fastest = readings.Count == 0 ? 0 : (int)Math.Round(readings.Max(s => s.Value));

        FanSummaryText = readings.Count == 0
            ? Loc.T("fans.summary.none")
            : Loc.F(readings.Count == 1 ? "fans.summary.one" : "fans.summary.many",
                spinning, readings.Count, fastest);

        int activeCount = Fans.Count(fan => fan.Rpm > 0);

        if (ShowOnlyActiveFans && activeCount != _lastActiveFanCount)
            VisibleFans.Refresh();

        _lastActiveFanCount = activeCount;
        ShowActiveFilterEmpty = ShowOnlyActiveFans && Fans.Count > 0 && activeCount == 0;
    }

    private static string ChannelKey(string hardwareId, string name) =>
        $"{hardwareId}\u001F{name}";

    private void UpdateStatus()
    {
        if (_hardware.Latest.Sensors.Count == 0)
        {
            StatusTitle = Loc.T("status.noData");
            StatusDetail = Loc.T("status.noDataDetail");
            StatusBrush = UiBrushes.Danger;
            StatusSoftBrush = UiBrushes.DangerSoft;
            return;
        }

        StatusTitle = Loc.T("status.monitoring");
        StatusDetail = $"CPU {FormatTemperature(CpuTemperature)} · GPU {FormatTemperature(GpuTemperature)} · {Loc.F("status.groups", Groups.Count)}";
        StatusBrush = UiBrushes.Success;
        StatusSoftBrush = UiBrushes.SuccessSoft;
    }

    private static int? FindCpuFan(HardwareSnapshot snapshot)
    {
        var fans = snapshot.Sensors
            .Where(s => s.HardwareType == "SuperIO" && s.Kind == SensorKind.Fan)
            .ToList();

        // En la B560M-A el CPU_FAN es el canal 4 (Fan #4). Si no está,
        // usamos el ventilador que más rápido gira.
        SensorReading? cpu = fans.FirstOrDefault(s =>
                                 s.Name.Equals("Fan #4", StringComparison.OrdinalIgnoreCase))
                             ?? fans.OrderByDescending(s => s.Value).FirstOrDefault();

        return cpu is null ? null : (int)Math.Round(cpu.Value);
    }

    /// <summary>
    /// Promedio de los relojes de núcleo («Core #1», «Core #2»…), sin el reloj
    /// de bus. Es lo más parecido a la velocidad que muestra el Administrador de tareas.
    /// </summary>
    private static double? AverageCoreClockGhz(IEnumerable<SensorReading> cpu)
    {
        double[] cores = cpu
            .Where(s => s.Kind == SensorKind.Clock
                        && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)
                        && s.Value > 0)
            .Select(s => s.Value)
            .ToArray();

        return cores.Length == 0 ? null : cores.Average() / 1000d;
    }

    private static double? FindValue(
        IEnumerable<SensorReading> sensors,
        SensorKind kind,
        params string[] names)
    {
        var matching = sensors.Where(s => s.Kind == kind).ToList();

        foreach (string name in names)
        {
            SensorReading? match = matching.FirstOrDefault(s =>
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return match.Value;
        }

        return null;
    }

    private static double? FirstValue(IEnumerable<SensorReading> sensors, SensorKind kind) =>
        sensors.FirstOrDefault(s => s.Kind == kind)?.Value;

    private static double? MaxValue(IEnumerable<SensorReading> sensors, SensorKind kind)
    {
        double? max = null;

        foreach (SensorReading sensor in sensors.Where(s => s.Kind == kind))
            if (max is null || sensor.Value > max)
                max = sensor.Value;

        return max;
    }

    private static int? ToInt(double? value) => value is { } number
        ? (int)Math.Round(number)
        : null;

    private static string FormatTemperature(double? value) => value is { } number
        ? $"{number:0} °C"
        : "—";

    private void OpenFolder(string path, bool selectFile)
    {
        try
        {
            if (selectFile)
            {
                SaveNow();
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            SaveStatus = Loc.F("save.folderError", ex.Message);
        }
    }
}
