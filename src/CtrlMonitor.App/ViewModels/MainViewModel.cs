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

namespace CtrlMonitor.App.ViewModels;

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

    private int _lastActiveFanCount = -1;

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

        SaveStatus = "Configuración cargada";
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

    public string CpuName => _hardware.CpuName;

    public string GpuName => _hardware.GpuName;

    public string SuperIoName => _hardware.SuperIoName;

    public string DriverVersion => _hardware.DriverVersion;

    public string DriverShortText => _hardware.PawnIoInstalled ? "PawnIO listo" : "PawnIO no detectado";

    public Brush DriverBrush => _hardware.PawnIoInstalled ? UiBrushes.Success : UiBrushes.Danger;

    public string AdminText => _hardware.IsAdministrator ? "Sí" : "No";

    public string ConfigPath => _store.ConfigPath;

    public string LogDirectory => AppPaths.LogDirectory;

    public string VersionText => "Ctrl Monitor 2.6.0";

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

    [ObservableProperty]
    private string _lastUpdateText = "Esperando datos…";

    [ObservableProperty]
    private string _statusTitle = "Iniciando";

    [ObservableProperty]
    private string _statusDetail = "Leyendo sensores…";

    [ObservableProperty]
    private Brush _statusBrush = UiBrushes.Muted;

    [ObservableProperty]
    private Brush _statusSoftBrush = UiBrushes.NeutralSoft;

    [ObservableProperty]
    private string _saveStatus = string.Empty;

    [ObservableProperty]
    private int _chartWindowMinutes = 5;

    [ObservableProperty]
    private string _fanSummaryText = "Sin ventiladores detectados";

    [ObservableProperty]
    private bool _showOnlyActiveFans;

    [ObservableProperty]
    private bool _showActiveFilterEmpty;

    [ObservableProperty]
    private string _ramText = "Detectando…";

    [ObservableProperty]
    private string _disksText = "Detectando…";

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
                SaveStatus = $"No se pudo configurar el inicio automático: {error}";

                // El binding TwoWay ya marcó el check; forzamos que vuelva al valor real.
                OnPropertyChanged();
            }
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

    [RelayCommand]
    private void SaveNow()
    {
        _saveTimer.Stop();

        SaveStatus = _store.Save(_config)
            ? $"Guardado {DateTime.Now:HH:mm:ss}"
            : $"Error al guardar: {_store.LastError}";
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

        SaveNow();
        AppLog.Info("Monitor de hardware cerrado.");
    }

    private async Task LoadMachineInventoryAsync()
    {
        MachineInventory inventory = await Task.Run(MachineInventoryReader.Read);

        if (_disposed)
            return;

        RamText = FormatMemory(inventory);
        DisksText = FormatDisks(inventory);
        DisksTooltip = FormatDiskDetails(inventory);
    }

    private static string FormatMemory(MachineInventory inventory)
    {
        if (inventory.TotalMemoryBytes <= 0)
            return "No disponible";

        string text = FormatBytes(inventory.TotalMemoryBytes);

        if (inventory.Modules.Count > 0)
        {
            int count = inventory.Modules.Count;
            text += $" · {count} {(count == 1 ? "módulo" : "módulos")}";

            int speed = inventory.Modules.Max(module => module.SpeedMhz);

            if (speed > 0)
                text += $" · {speed} MHz";
        }

        return text;
    }

    private static string FormatDisks(MachineInventory inventory)
    {
        if (inventory.Disks.Count == 0)
            return "No disponible";

        int count = inventory.Disks.Count;
        long total = inventory.Disks.Sum(disk => disk.SizeBytes);

        return $"{count} {(count == 1 ? "unidad" : "unidades")} · {FormatBytes(total)}";
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

        SaveStatus = "Cambios sin guardar";
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

        if (snapshot.Sensors.Count == 0)
        {
            UpdateVideoMemory(Array.Empty<SensorReading>());
            RefreshChartPanels();
            LastUpdateText = "Sin sensores disponibles";
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

        LastUpdateText = $"Actualizado {DateTime.Now:HH:mm:ss}";
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
            NetUpMbs));

        // El gráfico permite ventanas de hasta 10 minutos; conservamos ese
        // margen y descartamos lo más viejo para no crecer sin límite.
        DateTime cutoff = timestampUtc - TimeSpan.FromMinutes(10);

        while (ChartSamples.Count > 1 && ChartSamples[0].TimestampUtc < cutoff)
            ChartSamples.RemoveAt(0);
    }

    private void InitializeChartPanels()
    {
        List<string> configured = _config.ChartMetrics ?? new List<string>();

        foreach (string key in configured)
        {
            ChartMetric? metric = ChartMetrics.All.FirstOrDefault(item =>
                item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

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
        if (configured.Count > 0 && ChartPanels.Count == 0)
        {
            AddChartPanelCore(ChartMetrics.Cpu);
            AddChartPanelCore(ChartMetrics.Gpu);
            AddChartPanelCore(ChartMetrics.Ram);
        }

        _chartsInitialized = true;
        RefreshAvailableChartMetrics();
    }

    private void AddChartPanelCore(ChartMetric metric)
    {
        var panel = new ChartPanelViewModel(this, ChartMetrics.All, metric);
        panel.PropertyChanged += OnChartPanelPropertyChanged;
        ChartPanels.Add(panel);
    }

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

        _config.ChartMetrics = ChartPanels
            .Select(panel => panel.SelectedMetric.Key)
            .ToList();

        SaveSoon();
    }

    private void RefreshAvailableChartMetrics()
    {
        List<ChartMetric> available = ChartMetrics.All
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
        VramText = $"{VramUsedGb:0.0} GB de {VramTotalGb:0.0} GB · {VramPercent:0} %";
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

        string channelWord = readings.Count == 1 ? "canal" : "canales";
        FanSummaryText = readings.Count == 0
            ? "Sin canales de ventilador detectados."
            : $"{spinning} de {readings.Count} {channelWord} girando · máximo {fastest} RPM";

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
            StatusTitle = "Sin datos";
            StatusDetail = "No se pudieron leer los sensores. Verificá PawnIO y los permisos.";
            StatusBrush = UiBrushes.Danger;
            StatusSoftBrush = UiBrushes.DangerSoft;
            return;
        }

        StatusTitle = "Monitorizando";
        StatusDetail = $"CPU {FormatTemperature(CpuTemperature)} · GPU {FormatTemperature(GpuTemperature)} · {Groups.Count} grupos";
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
            SaveStatus = $"No se pudo abrir la carpeta: {ex.Message}";
        }
    }
}
