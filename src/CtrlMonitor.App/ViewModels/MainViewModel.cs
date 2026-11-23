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
using CtrlMonitor.Core.Fps;
using CtrlMonitor.Core.Hardware;
using CtrlMonitor.Core.Inventory;
using CtrlMonitor.Core.Localization;
using CtrlMonitor.Core.Overlay;

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

    private readonly List<OverlayMetricOption> _overlayMetrics = new();
    private readonly List<OverlayGroupColorOption> _overlayGroupColors = new();
    private readonly List<OverlayAnchorOption> _overlayAnchorOptions = new();
    private readonly PresentMonSession _fps = new();
    private readonly GraphicsApiMonitor _graphicsApi = new();
    private IReadOnlyList<OverlaySegmentViewModel> _overlaySegments = Array.Empty<OverlaySegmentViewModel>();
    private Brush _overlayBackgroundBrush = Brushes.Transparent;
    private bool _isOverlayEditing;
    private bool _suppressOverlayUpdates;

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
        Processes.Attach(_config, SaveSoon);
        InitializeOverlay();

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
            OnPropertyChanged(nameof(CanAddChartPanel));
        }
    }

    public bool HasAvailableChartMetrics => AvailableChartMetrics.Count > 0;

    public bool IsChartPanelsEmpty => ChartPanels.Count == 0;

    /// <summary>Una entrada por métrica para la vista de lista, como el Administrador de tareas.</summary>
    public ObservableCollection<ChartPanelViewModel> ChartListItems { get; } = new();

    [ObservableProperty]
    private ChartPanelViewModel? _selectedChartListItem;

    /// <summary>La sección de gráficos se muestra como lista con un gráfico grande.</summary>
    public bool IsChartListView
    {
        get => _config.ChartListView;
        set
        {
            if (_config.ChartListView == value)
                return;

            _config.ChartListView = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsChartCardView));
            OnPropertyChanged(nameof(CanAddChartPanel));
            OnPropertyChanged(nameof(ShowChartPanelsEmpty));
            SaveSoon();
        }
    }

    public bool IsChartCardView
    {
        get => !IsChartListView;
        set => IsChartListView = !value;
    }

    /// <summary>El selector para agregar tarjetas solo tiene sentido en la vista de tarjetas.</summary>
    public bool CanAddChartPanel => IsChartCardView && HasAvailableChartMetrics;

    public bool ShowChartPanelsEmpty => IsChartCardView && IsChartPanelsEmpty;

    [ObservableProperty]
    private ChartMetric? _newChartMetric;

    /// <summary>Canales de ventilador detectados, con RPM y PWM del BIOS.</summary>
    public ObservableCollection<FanViewModel> Fans { get; } = new();

    /// <summary>Discos físicos con sus unidades, para la tarjeta de almacenamiento de Inicio.</summary>
    public ObservableCollection<StorageDiskViewModel> StorageDisks { get; } = new();

    private bool _readingVolumes;
    private bool _volumesRequested = true;

    /// <summary>Pestaña visible; 0 es Inicio.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    // El espacio en disco cambia poco: se relee solo al volver a Inicio, no en
    // cada refresco del monitor.
    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageSubtitle));

        if (value == 0)
            RequestVolumes();

        // Los datos de los discos solo se consultan mientras se ven: en
        // Inicio, en Sensores y en Discos.
        _hardware.StorageUpdatesEnabled = value is HomeTabIndex or SensorsTabIndex or DisksTabIndex;

        if (value == DisksTabIndex)
            RequestDiskDetails();

        // El administrador de procesos solo lee mientras está a la vista.
        Processes.IsActive = _monitoring && value == ProcessesTabIndex;

        // El modo edición solo tiene sentido mientras se ve la sección Overlay.
        if (value != OverlayTabIndex)
            IsOverlayEditing = false;
    }

    /// <summary>Título y descripción de cada pestaña, en el orden de la navegación.</summary>
    private static readonly (string Title, string Subtitle)[] PageTexts =
    {
        ("nav.summary", "summary.subtitle"),
        ("chart.title", "chart.subtitle"),
        ("nav.fans", "fans.subtitle"),
        ("nav.sensors", "sensors.subtitle"),
        ("disks.title", "disks.subtitle"),
        ("processes.title", "processes.subtitle"),
        ("nav.overlay", "overlay.subtitle"),
        ("nav.settings", "settings.subtitle")
    };

    /// <summary>Título de la página visible, para la barra superior.</summary>
    public string CurrentPageTitle =>
        SelectedTabIndex >= 0 && SelectedTabIndex < PageTexts.Length ? Loc.T(PageTexts[SelectedTabIndex].Title) : string.Empty;

    /// <summary>Descripción de la página visible, para la barra superior.</summary>
    public string CurrentPageSubtitle =>
        SelectedTabIndex >= 0 && SelectedTabIndex < PageTexts.Length ? Loc.T(PageTexts[SelectedTabIndex].Subtitle) : string.Empty;

    private const int ProcessesTabIndex = 5;
    private const int OverlayTabIndex = 6;

    /// <summary>Administrador de procesos (pestaña Procesos).</summary>
    public ProcessesViewModel Processes { get; } = new();

    private const int HomeTabIndex = 0;
    private const int SensorsTabIndex = 3;
    private const int DisksTabIndex = 4;

    /// <summary>Ficha de cada disco para la sección Discos.</summary>
    public ObservableCollection<DiskDetailViewModel> DiskDetails { get; } = new();

    public bool HasDiskDetails => DiskDetails.Count > 0;

    private static readonly TimeSpan DiskDetailsInterval = TimeSpan.FromSeconds(5);
    private DateTime _diskDetailsUtc = DateTime.MinValue;
    private IReadOnlyDictionary<int, DiskCondition> _windowsDiskHealth = new Dictionary<int, DiskCondition>();
    private IReadOnlyDictionary<int, IReadOnlyList<VolumeUsage>> _diskVolumes = new Dictionary<int, IReadOnlyList<VolumeUsage>>();
    private bool _diskVolumesChanged;

    /// <summary>
    /// Al entrar a Discos se relee la salud que informa Windows (en segundo
    /// plano) y se fuerza a actualizar las fichas en el próximo refresco.
    /// </summary>
    private void RequestDiskDetails()
    {
        _diskDetailsUtc = DateTime.MinValue;

        Task.Run(() => (Disks: PhysicalDiskReader.Read(), Volumes: VolumeUsageReader.Read())).ContinueWith(
            task => _dispatcher.BeginInvoke(new Action(() =>
            {
                if (!task.IsCompletedSuccessfully || _disposed)
                    return;

                // Unidades de cada disco para los gráficos de espacio.
                _diskVolumes = task.Result.Disks.ToDictionary(
                    disk => disk.Number,
                    disk => (IReadOnlyList<VolumeUsage>)task.Result.Volumes
                        .Where(volume => disk.Letters.Contains(volume.Letter, StringComparer.OrdinalIgnoreCase))
                        .ToList());
                _diskVolumesChanged = true;

                _windowsDiskHealth = task.Result.Disks.ToDictionary(
                    disk => disk.Number,
                    disk => disk.Health switch
                    {
                        DiskHealth.Healthy => DiskCondition.Good,
                        DiskHealth.Warning => DiskCondition.Caution,
                        DiskHealth.Unhealthy => DiskCondition.Bad,
                        _ => DiskCondition.Unknown
                    });

                UpdateDiskDetails(force: true);
            })),
            TaskScheduler.Default);
    }

    private void UpdateDiskDetails(bool force = false)
    {
        if (SelectedTabIndex != DisksTabIndex)
            return;

        if (!force && DateTime.UtcNow - _diskDetailsUtc < DiskDetailsInterval)
            return;

        _diskDetailsUtc = DateTime.UtcNow;
        IReadOnlyList<DiskSmartInfo> disks = _hardware.GetDiskSmart();

        for (int i = DiskDetails.Count - 1; i >= 0; i--)
        {
            if (!disks.Any(disk => disk.Number == DiskDetails[i].Number))
                DiskDetails.RemoveAt(i);
        }

        foreach (DiskSmartInfo disk in disks)
        {
            DiskDetailViewModel? detail = DiskDetails.FirstOrDefault(item => item.Number == disk.Number);

            if (detail is null)
            {
                detail = new DiskDetailViewModel(disk.Number);
                DiskDetails.Add(detail);
                detail.SetVolumes(_diskVolumes.TryGetValue(disk.Number, out IReadOnlyList<VolumeUsage>? fresh)
                    ? fresh
                    : Array.Empty<VolumeUsage>());
            }
            else if (_diskVolumesChanged)
            {
                detail.SetVolumes(_diskVolumes.TryGetValue(disk.Number, out IReadOnlyList<VolumeUsage>? volumes)
                    ? volumes
                    : Array.Empty<VolumeUsage>());
            }

            detail.Update(disk, _windowsDiskHealth.TryGetValue(disk.Number, out DiskCondition health)
                ? health
                : DiskCondition.Unknown);
        }

        _diskVolumesChanged = false;
        OnPropertyChanged(nameof(HasDiskDetails));
    }

    private void RequestVolumes()
    {
        _volumesRequested = true;
        UpdateVolumes();
    }

    /// <summary>Vista filtrada de los ventiladores según «Solo activos».</summary>
    public ListCollectionView VisibleFans { get; private set; } = null!;

    public string MotherboardName => _hardware.MotherboardName;

    /// <summary>Marca de la placa detectada a partir de su nombre, o <c>null</c>.</summary>
    private HardwareBrand? BoardBrand => BoardBrands.Detect(_hardware.MotherboardBrandText);

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

    /// <summary>Logo de la marca de la placa de video (AMD, NVIDIA o Intel), si se reconoce.</summary>
    public string? GpuLogoUri => LogoUri(GpuBrands.Detect(_hardware.GpuName));

    public bool HasGpuLogo => GpuLogoUri is not null;

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
    [NotifyPropertyChangedFor(nameof(CpuMaxText))]
    private double? _cpuMaxTemperature;

    [ObservableProperty]
    private double? _cpuPower;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuLoadText))]
    private double? _cpuLoad;

    [ObservableProperty]
    private int? _cpuFanRpm;

    [ObservableProperty]
    private double? _gpuTemperature;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuHotspotText))]
    private double? _gpuHotspot;

    [ObservableProperty]
    private double? _gpuPower;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuLoadText))]
    private double? _gpuLoad;

    // Textos chicos de las tarjetas de Inicio, traducidos.
    public string CpuMaxText => Loc.F("summary.maxValue", FormatWhole(CpuMaxTemperature, " °C"));
    public string GpuHotspotText => Loc.F("summary.hotspotValue", FormatWhole(GpuHotspot, " °C"));
    public string CpuLoadText => Loc.F("summary.loadValue", FormatWhole(CpuLoad, " %"));
    public string GpuLoadText => Loc.F("summary.loadValue", FormatWhole(GpuLoad, " %"));

    private static string FormatWhole(double? value, string unit) =>
        value is { } number ? number.ToString("0", System.Globalization.CultureInfo.CurrentCulture) + unit : "—";

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

    /// <summary>RAM en uso, con el mismo formato que la VRAM.</summary>
    [ObservableProperty]
    private string _ramUsageText = "—";

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
            if (_monitoring)
                _hardware.StartMonitoring(TimeSpan.FromMilliseconds(clamped));

            SaveSoon();
        }
    }

    public string RefreshIntervalText => $"{RefreshIntervalMs / 1000.0:0.#} s";

    // ===================== Overlay =====================

    /// <summary>Opciones de color del selector global.</summary>
    public IReadOnlyList<OverlayColorOption> OverlayColorOptions => OverlayColorPalette.All;

    /// <summary>Colores por grupo; la primera opción es "usar el color global".</summary>
    public IReadOnlyList<OverlayColorOption> OverlayGroupColorOptions => OverlayColorPalette.WithGlobal;

    public IReadOnlyList<OverlayMetricOption> OverlayMetrics => _overlayMetrics;

    public IReadOnlyList<OverlayGroupColorOption> OverlayGroupColors => _overlayGroupColors;

    public bool OverlayEnabled
    {
        get => _config.Overlay.Enabled;
        set
        {
            if (_config.Overlay.Enabled == value)
                return;

            _config.Overlay.Enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlaySettingsEnabled));
            OnPropertyChanged(nameof(OverlayLocked));

            if (!value)
                IsOverlayEditing = false;

            SaveSoon();
        }
    }

    /// <summary>La sección de ajustes se habilita solo con el overlay activo.</summary>
    public bool OverlaySettingsEnabled => OverlayEnabled;

    public bool OverlayLocked => !OverlayEnabled;

    /// <summary>Estilo minimalista: todo en un solo color.</summary>
    public bool OverlayStyleMinimal
    {
        get => string.Equals(_config.Overlay.ColorStyle, OverlayColors.StyleMinimal, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
                SetOverlayColorStyle(OverlayColors.StyleMinimal);
        }
    }

    /// <summary>Estilo variado: un color por grupo.</summary>
    public bool OverlayStyleVaried
    {
        get => string.Equals(_config.Overlay.ColorStyle, OverlayColors.StyleVaried, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
                SetOverlayColorStyle(OverlayColors.StyleVaried);
        }
    }

    private void SetOverlayColorStyle(string style)
    {
        if (string.Equals(_config.Overlay.ColorStyle, style, StringComparison.OrdinalIgnoreCase))
            return;

        _config.Overlay.ColorStyle = style;
        OnPropertyChanged(nameof(OverlayStyleMinimal));
        OnPropertyChanged(nameof(OverlayStyleVaried));
        RebuildOverlaySegments();
        SaveSoon();
    }

    /// <summary>
    /// Colores a medida, apagados por defecto. Mientras estén apagados, la
    /// tarjeta de colores queda bloqueada y manda el estilo elegido.
    /// </summary>
    public bool OverlayCustomColors
    {
        get => _config.Overlay.CustomColors;
        set
        {
            if (_config.Overlay.CustomColors == value)
                return;

            _config.Overlay.CustomColors = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlayStylesEnabled));
            RebuildOverlaySegments();
            SaveSoon();
        }
    }

    /// <summary>Los estilos predefinidos solo mandan sin colores a medida.</summary>
    public bool OverlayStylesEnabled => !OverlayCustomColors;

    public string OverlayTextColor
    {
        get => _config.Overlay.TextColor;
        set
        {
            string normalized = OverlayColors.TryNormalize(value, out string hex)
                ? hex
                : OverlayColors.DefaultText;

            if (string.Equals(_config.Overlay.TextColor, normalized, StringComparison.Ordinal))
                return;

            _config.Overlay.TextColor = normalized;
            OnPropertyChanged();
            RebuildOverlaySegments();
            SaveSoon();
        }
    }

    public double OverlayTextSize
    {
        get => _config.Overlay.TextSize;
        set
        {
            double clamped = Math.Clamp(value, 10, 24);

            if (Math.Abs(clamped - _config.Overlay.TextSize) < 0.01)
                return;

            _config.Overlay.TextSize = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlayTextSizeText));
            SaveSoon();
        }
    }

    public string OverlayTextSizeText => $"{OverlayTextSize:0} px";

    public double OverlayBackgroundOpacity
    {
        get => _config.Overlay.BackgroundOpacity;
        set
        {
            double clamped = Math.Clamp(value, 0, 100);

            if (Math.Abs(clamped - _config.Overlay.BackgroundOpacity) < 0.01)
                return;

            _config.Overlay.BackgroundOpacity = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlayBackgroundOpacityText));
            UpdateOverlayBackgroundBrush();
            SaveSoon();
        }
    }

    public string OverlayBackgroundOpacityText => $"{OverlayBackgroundOpacity:0} %";

    /// <summary>Fondo negro con la opacidad elegida; 0 lo deja transparente.</summary>
    public Brush OverlayBackgroundBrush => _overlayBackgroundBrush;

    /// <summary>Textos y colores de la barra; la vista previa y la ventana real comparten esta lista.</summary>
    public IReadOnlyList<OverlaySegmentViewModel> OverlaySegments
    {
        get => _overlaySegments;
        private set
        {
            _overlaySegments = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasOverlayMetrics));
        }
    }

    public bool HasOverlayMetrics => _overlaySegments.Count > 0;

    /// <summary>
    /// Modo de edición: la ventana deja de ser click-through para poder
    /// arrastrarla. No se guarda; es solo una herramienta de ajuste.
    /// </summary>
    public bool IsOverlayEditing
    {
        get => _isOverlayEditing;
        set
        {
            if (_isOverlayEditing == value)
                return;

            _isOverlayEditing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlayPositionLocked));
        }
    }

    /// <summary>
    /// Inverso del modo edición, para la casilla "Bloquear overlay". Arranca
    /// siempre bloqueado: desbloquearlo es solo para elegir el lugar.
    /// </summary>
    public bool OverlayPositionLocked
    {
        get => !IsOverlayEditing;
        set => IsOverlayEditing = !value;
    }

    public string OverlayHotkeyText => "Ctrl+Alt+O";

    public double? OverlayLeft => _config.Overlay.Left;

    public double? OverlayTop => _config.Overlay.Top;

    /// <summary>Posición predefinida actual, o <c>custom</c> si se arrastró libremente.</summary>
    public string OverlayAnchor => _config.Overlay.Anchor;

    /// <summary>Las nueve posiciones predefinidas para el selector de la pestaña.</summary>
    public IReadOnlyList<OverlayAnchorOption> OverlayAnchorOptions => _overlayAnchorOptions;

    /// <summary>Bordes rectos: la barra encaja mejor contra las esquinas.</summary>
    public bool OverlaySquareCorners
    {
        get => _config.Overlay.SquareCorners;
        set
        {
            if (_config.Overlay.SquareCorners == value)
                return;

            _config.Overlay.SquareCorners = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlayCornerRadius));
            OnPropertyChanged(nameof(OverlayEditCornerRadius));
            SaveSoon();
        }
    }

    public System.Windows.CornerRadius OverlayCornerRadius => new(OverlaySquareCorners ? 0 : 7);

    public System.Windows.CornerRadius OverlayEditCornerRadius => new(OverlaySquareCorners ? 0 : 8);

    /// <summary>Elige una posición predefinida: la barra queda anclada a ese lugar.</summary>
    public void SetOverlayAnchor(string anchor)
    {
        if (!OverlayAnchors.IsKnown(anchor) || anchor == OverlayAnchors.Custom)
            return;

        _config.Overlay.Anchor = anchor;
        OnOverlayPositionChanged();
    }

    /// <summary>
    /// El arrastre guarda la posición final. Si el imán la alineó en los dos
    /// ejes, llega como ancla; si no, como posición libre.
    /// </summary>
    public void SetOverlayPosition(double left, double top, string anchor)
    {
        if (anchor != OverlayAnchors.Custom && OverlayAnchors.IsKnown(anchor))
        {
            _config.Overlay.Anchor = anchor;
        }
        else
        {
            if (!double.IsFinite(left) || !double.IsFinite(top))
                return;

            _config.Overlay.Anchor = OverlayAnchors.Custom;
            _config.Overlay.Left = left;
            _config.Overlay.Top = top;
        }

        OnOverlayPositionChanged();
    }

    [RelayCommand]
    private void ResetOverlayPosition()
    {
        _config.Overlay.Anchor = OverlayAnchors.Default;
        _config.Overlay.Left = null;
        _config.Overlay.Top = null;
        OnOverlayPositionChanged();
    }

    private void OnOverlayPositionChanged()
    {
        foreach (OverlayAnchorOption option in _overlayAnchorOptions)
            option.Refresh(_config.Overlay.Anchor);

        OnPropertyChanged(nameof(OverlayLeft));
        OnPropertyChanged(nameof(OverlayTop));
        OnPropertyChanged(nameof(OverlayAnchor));
        SaveSoon();
    }

    [RelayCommand]
    private void ResetOverlayMetrics()
    {
        var defaults = new HashSet<string>(OverlayMetricKeys.Defaults, StringComparer.OrdinalIgnoreCase);

        _suppressOverlayUpdates = true;

        try
        {
            foreach (OverlayMetricOption option in _overlayMetrics)
                option.IsSelected = defaults.Contains(option.Key);
        }
        finally
        {
            _suppressOverlayUpdates = false;
        }

        OnOverlayMetricsChanged();
    }

    private void InitializeOverlay()
    {
        var selected = new HashSet<string>(
            _config.Overlay.Metrics ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        _overlayMetrics.Clear();

        foreach (string key in OverlayMetricKeys.All)
        {
            _overlayMetrics.Add(new OverlayMetricOption(
                key,
                MetricLabelKey(key),
                selected.Contains(key),
                OnOverlayMetricsChanged));
        }

        _overlayAnchorOptions.Clear();

        foreach (string anchor in OverlayAnchors.All)
            _overlayAnchorOptions.Add(new OverlayAnchorOption(anchor, _config.Overlay.Anchor, SetOverlayAnchor));

        _overlayGroupColors.Clear();

        foreach (string group in OverlayMetricKeys.Groups)
        {
            string hex = _config.Overlay.GroupColors.TryGetValue(group, out string? value)
                ? value
                : string.Empty;

            _overlayGroupColors.Add(new OverlayGroupColorOption(
                group,
                $"overlay.group.{group}",
                hex,
                OverlayColorPalette.WithGlobal,
                OnOverlayGroupColorChanged));
        }

        UpdateOverlayBackgroundBrush();
        RebuildOverlaySegments();
    }

    private void OnOverlayMetricsChanged()
    {
        if (_suppressOverlayUpdates)
            return;

        _config.Overlay.Metrics = _overlayMetrics
            .Where(option => option.IsSelected)
            .Select(option => option.Key)
            .ToList();

        RebuildOverlaySegments();
        SaveSoon();
    }

    private void OnOverlayGroupColorChanged()
    {
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (OverlayGroupColorOption option in _overlayGroupColors)
            colors[option.GroupKey] = option.SelectedHex;

        _config.Overlay.GroupColors = colors;
        RebuildOverlaySegments();
        SaveSoon();
    }

    private void UpdateOverlayBackgroundBrush()
    {
        byte alpha = (byte)Math.Round(Math.Clamp(_config.Overlay.BackgroundOpacity, 0, 100) * 255 / 100);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        brush.Freeze();

        _overlayBackgroundBrush = brush;
        OnPropertyChanged(nameof(OverlayBackgroundBrush));
    }

    /// <summary>
    /// El administrador del overlay avisa qué proceso está en pantalla
    /// completa (0 si ninguno). PresentMon solo corre si el overlay está
    /// activo y la métrica FPS elegida, para no gastar recursos de más.
    /// </summary>
    public void UpdateFpsTarget(int processId)
    {
        bool wanted = OverlayEnabled
                      && _overlayMetrics.Any(option => option.IsSelected && OverlayMetricKeys.IsFps(option.Key));

        _fps.SetTarget(wanted ? processId : 0);

        // La API gráfica solo se busca si el usuario la eligió en el overlay.
        bool apiWanted = wanted && _overlayMetrics.Any(option => option.IsSelected && option.Key == OverlayMetricKeys.GraphicsApi);
        _graphicsApi.SetTarget(apiWanted ? processId : 0);
    }

    /// <summary>
    /// Arma la lista que consumen la vista previa y la ventana del overlay.
    /// Se llama en cada refresco de sensores y al cambiar cualquier ajuste.
    /// </summary>
    private void RebuildOverlaySegments()
    {
        if (_overlayMetrics.Count == 0)
            return;

        FpsReading? fps = _fps.Current();

        var snapshot = new OverlaySnapshot
        {
            CpuLoad = CpuLoad,
            CpuTemperature = CpuTemperature,
            CpuClockGhz = CpuClockGhz,
            CpuPower = CpuPower,
            CpuFanRpm = CpuFanRpm,
            GpuLoad = GpuLoad,
            GpuTemperature = GpuTemperature,
            GpuHotspot = GpuHotspot,
            GpuPower = GpuPower,
            GpuFanRpm = GpuFanRpm,
            VramUsedGb = VramUsedGb,
            VramTotalGb = VramTotalGb,
            RamUsedGb = RamUsedGb,
            RamTotalGb = RamTotalGb,
            RamPercent = RamPercent,
            NetDownMbs = NetDownMbs,
            NetUpMbs = NetUpMbs,
            Fps = fps?.Fps,
            FrameGeneration = fps?.FrameGeneration == true,
            FrameTimeMs = fps?.FrameTimeMs,
            FpsLow1 = fps?.Low1Fps,
            GraphicsApi = fps is null ? null : _graphicsApi.Detect(fps.Value.Runtime)
        };

        var selected = _overlayMetrics
            .Where(option => option.IsSelected)
            .Select(option => option.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(snapshot, selected);

        // Si los grupos son los mismos, solo se cambian textos y colores: así
        // los controles de la barra no se recrean y no parpadea.
        bool sameShape = _overlaySegments.Count == Math.Max(0, (segments.Count * 2) - 1);

        for (int i = 0; sameShape && i < segments.Count; i++)
            sameShape = _overlaySegments[i * 2].GroupKey == segments[i].GroupKey;

        if (sameShape)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                OverlaySegmentViewModel item = _overlaySegments[i * 2];
                item.Text = segments[i].Text;
                item.Brush = GroupBrush(segments[i].GroupKey);
            }

            return;
        }

        var result = new List<OverlaySegmentViewModel>(segments.Count * 2);

        foreach (OverlaySegment segment in segments)
        {
            if (result.Count > 0)
                result.Add(new OverlaySegmentViewModel(string.Empty, "|", OverlayBrushes.Separator, isSeparator: true));

            result.Add(new OverlaySegmentViewModel(segment.GroupKey, segment.Text, GroupBrush(segment.GroupKey)));
        }

        OverlaySegments = result;
    }

    private Brush GroupBrush(string groupKey) =>
        OverlayBrushes.FromHex(_config.Overlay.ResolveGroupColor(groupKey));

    private static string MetricLabelKey(string key) => key switch
    {
        OverlayMetricKeys.Fps => "overlay.metric.fps",
        OverlayMetricKeys.GraphicsApi => "overlay.metric.graphicsApi",
        OverlayMetricKeys.FrameTime => "overlay.metric.frameTime",
        OverlayMetricKeys.FpsLow1 => "overlay.metric.fpsLow1",
        OverlayMetricKeys.CpuLoad => "overlay.metric.cpuLoad",
        OverlayMetricKeys.CpuTemperature => "overlay.metric.cpuTemp",
        OverlayMetricKeys.CpuClock => "overlay.metric.cpuClock",
        OverlayMetricKeys.CpuPower => "overlay.metric.cpuPower",
        OverlayMetricKeys.CpuFan => "overlay.metric.cpuFan",
        OverlayMetricKeys.GpuLoad => "overlay.metric.gpuLoad",
        OverlayMetricKeys.GpuTemperature => "overlay.metric.gpuTemp",
        OverlayMetricKeys.GpuHotspot => "overlay.metric.gpuHotspot",
        OverlayMetricKeys.GpuVram => "overlay.metric.gpuVram",
        OverlayMetricKeys.GpuPower => "overlay.metric.gpuPower",
        OverlayMetricKeys.GpuFan => "overlay.metric.gpuFan",
        OverlayMetricKeys.RamUsed => "overlay.metric.ramUsed",
        OverlayMetricKeys.RamPercent => "overlay.metric.ramPercent",
        OverlayMetricKeys.NetThroughput => "overlay.metric.net",
        _ => key
    };

    /// <summary>Pregunta única sobre los informes de errores, sobre la ventana.</summary>
    public bool ShowErrorReportsPrompt => !_config.ErrorReportsAsked && ErrorReporting.IsConfigured;

    [RelayCommand]
    private void AnswerErrorReports(string? answer)
    {
        _config.ErrorReportsAsked = true;
        SendErrorReports = answer == "yes";
        OnPropertyChanged(nameof(ShowErrorReportsPrompt));
        AppLog.Info(SendErrorReports
            ? "El usuario aceptó enviar informes de errores."
            : "El usuario no quiso enviar informes de errores.");
        SaveSoon();
    }

    /// <summary>Enviar informes de errores (desactivado hasta que el usuario lo active).</summary>
    public bool SendErrorReports
    {
        get => _config.SendErrorReports;
        set
        {
            if (_config.SendErrorReports == value)
                return;

            _config.SendErrorReports = value;
            _config.ErrorReportsAsked = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowErrorReportsPrompt));
            ErrorReporting.Configure(value, _config.InstallId);
            SaveSoon();
        }
    }

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

    /// <summary>Formulario de GitHub para reportar un error, con versión, Windows y hardware completados.</summary>
    [RelayCommand]
    private void ReportBug() =>
        FeedbackLinks.ReportBug(
            ShortVersionText.TrimStart('v'),
            $"CPU: {CpuName}\nGPU: {GpuName}\nPlaca / Motherboard: {MotherboardName}");

    /// <summary>Formulario de GitHub para sugerir una mejora.</summary>
    [RelayCommand]
    private void SuggestImprovement() => FeedbackLinks.Suggest(ShortVersionText.TrimStart('v'));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimerTick;
        _trimTimer?.Stop();
        _hardware.SnapshotUpdated -= OnSnapshotUpdated;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        _fps.Dispose();
        _graphicsApi.Dispose();

        _diskUsage.Dispose();
        Processes.Dispose();
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
        OnPropertyChanged(nameof(CpuMaxText));
        OnPropertyChanged(nameof(GpuHotspotText));
        OnPropertyChanged(nameof(CpuLoadText));
        OnPropertyChanged(nameof(GpuLoadText));
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageSubtitle));

        foreach (LanguageOption option in LanguageOptions)
            option.RefreshLanguage();

        foreach (ChartPanelViewModel panel in ChartPanels)
            panel.RefreshLanguage();

        foreach (StorageDiskViewModel disk in StorageDisks)
            disk.RefreshLanguage();

        RefreshThreadsLanguage();

        foreach (DiskDetailViewModel disk in DiskDetails)
            disk.RefreshLanguage();

        Processes.RefreshLanguage();

        foreach (ChartPanelViewModel item in ChartListItems)
            item.RefreshLanguage();

        foreach (FanViewModel fan in Fans)
            fan.RefreshLanguage();

        foreach (SensorGroupViewModel group in Groups)
            group.RefreshLanguage();

        OverlayColorPalette.RefreshLanguage();

        foreach (OverlayMetricOption option in _overlayMetrics)
            option.RefreshLanguage();

        foreach (OverlayGroupColorOption option in _overlayGroupColors)
            option.RefreshLanguage();

        foreach (OverlayAnchorOption option in _overlayAnchorOptions)
            option.RefreshLanguage();

        RebuildOverlaySegments();
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

    /// <summary>
    /// Lee el espacio de las unidades si se pidió (al arrancar o al entrar a
    /// Inicio), fuera del hilo de la interfaz: una unidad extraíble lenta no
    /// debe trabar la ventana.
    /// </summary>
    private void UpdateVolumes()
    {
        if (_readingVolumes || !_volumesRequested)
            return;

        _readingVolumes = true;
        _volumesRequested = false;

        Task.Run(ReadStorage).ContinueWith(
            task => _dispatcher.BeginInvoke(new Action(() =>
            {
                _readingVolumes = false;

                if (task.IsCompletedSuccessfully && !_disposed)
                    ApplyStorage(task.Result.Disks, task.Result.Volumes);
                else if (task.Exception is { } ex)
                    AppLog.Warn($"No se pudo leer el almacenamiento: {ex.GetBaseException().Message}");
            })),
            TaskScheduler.Default);
    }

    private static (IReadOnlyList<PhysicalDiskInfo> Disks, IReadOnlyList<VolumeUsage> Volumes) ReadStorage()
    {
        IReadOnlyList<PhysicalDiskInfo> disks;

        try
        {
            disks = PhysicalDiskReader.Read();
        }
        catch (Exception ex)
        {
            // Sin la API de almacenamiento se muestran igual las unidades, sin salud.
            AppLog.Warn($"No se pudo leer la salud de los discos: {ex.Message}");
            disks = Array.Empty<PhysicalDiskInfo>();
        }

        return (disks, VolumeUsageReader.Read());
    }

    /// <summary>
    /// Agrupa las unidades por disco físico. Las que no aparecen en ningún
    /// disco (por ejemplo, si falla la API de almacenamiento) van a un grupo aparte.
    /// </summary>
    private void ApplyStorage(IReadOnlyList<PhysicalDiskInfo> disks, IReadOnlyList<VolumeUsage> volumes)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<StorageDiskViewModel>();

        foreach (PhysicalDiskInfo disk in disks)
        {
            List<VolumeViewModel> own = volumes
                .Where(volume => disk.Letters.Contains(volume.Letter, StringComparer.OrdinalIgnoreCase))
                .Select(volume => new VolumeViewModel(volume))
                .ToList();

            own.ForEach(volume => used.Add(volume.Letter));
            result.Add(new StorageDiskViewModel(disk, own));
        }

        List<VolumeViewModel> others = volumes
            .Where(volume => !used.Contains(volume.Letter))
            .Select(volume => new VolumeViewModel(volume))
            .ToList();

        if (others.Count > 0)
            result.Add(new StorageDiskViewModel(null, others));

        StorageDisks.Clear();

        foreach (StorageDiskViewModel disk in result)
            StorageDisks.Add(disk);

        UpdateStorageSensors(_hardware.Latest.Sensors);

        AppLog.Info("Almacenamiento: " + string.Join(" | ", StorageDisks.Select(disk =>
            $"{disk.Title} [{disk.Model}] {disk.HealthText} {disk.TemperatureText} {disk.LifeText} ({string.Join(", ", disk.Volumes.Select(v => v.Letter))})")));

        OnPropertyChanged(nameof(HasVolumes));
    }

    /// <summary>
    /// Cruza los sensores de disco de LibreHardwareMonitor con los discos de
    /// Windows por modelo. Con dos discos del mismo modelo se asignan en orden.
    /// </summary>
    private void UpdateStorageSensors(IReadOnlyList<SensorReading> sensors)
    {
        if (StorageDisks.Count == 0)
            return;

        var byModel = sensors
            .Where(s => s.HardwareType == "Storage")
            .GroupBy(s => s.HardwareId)
            .GroupBy(group => group.First().HardwareName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new Queue<List<SensorReading>>(group.Select(g => g.ToList())),
                StringComparer.OrdinalIgnoreCase);

        foreach (StorageDiskViewModel disk in StorageDisks.Where(d => d.Number >= 0).OrderBy(d => d.Number))
        {
            if (byModel.TryGetValue(disk.Model, out Queue<List<SensorReading>>? queue) && queue.Count > 0)
                disk.UpdateSensors(queue.Dequeue());
        }
    }

    public bool HasVolumes => StorageDisks.Count > 0;

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
        UpdateVolumes();
        UpdateStorageSensors(snapshot.Sensors);
        UpdateDiskDetails();
        UpdateProcessTotals();
        UpdateNetwork();
        UpdateDisks();
        UpdateStorageActivity();

        if (snapshot.Sensors.Count == 0)
        {
            UpdateVideoMemory(Array.Empty<SensorReading>());
            RefreshChartPanels();
            RebuildOverlaySegments();
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
        UpdateCpuThreads(cpu);
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
        RebuildOverlaySegments();

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
            _disks.Count == 0 ? null : _disks,
            CpuThreads.Count == 0 ? null : CpuThreads.Select(t => t.Load).ToArray(),
            CoreLoads.Count == 0 ? null : CoreLoads.ToArray()));

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

            if (metric is null && (key.StartsWith(ChartMetrics.DiskKeyPrefix, StringComparison.OrdinalIgnoreCase)
                                   || key.StartsWith(ChartMetrics.CoreKeyPrefix, StringComparison.OrdinalIgnoreCase)))
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
        SyncChartListItems();
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
        OnPropertyChanged(nameof(ShowChartPanelsEmpty));
    }

    /// <summary>Agrega a la vista de lista las métricas nuevas (por ejemplo, discos recién detectados).</summary>
    private void SyncChartListItems()
    {
        foreach (ChartMetric metric in AllChartMetrics)
        {
            if (ChartListItems.Any(item => item.SelectedMetric == metric))
                continue;

            ChartListItems.Add(new ChartPanelViewModel(this, AllChartMetrics, metric));
        }

        // Aplica el orden guardado; las métricas sin posición guardada (por
        // ejemplo, un disco nuevo) quedan al final en su orden natural.
        List<string> order = _config.ChartListOrder ?? new List<string>();
        List<ChartPanelViewModel> sorted = ChartListItems
            .Select((item, index) => (item, index))
            .OrderBy(entry => OrderIndex(order, entry.item.SelectedMetric.Key))
            .ThenBy(entry => entry.index)
            .Select(entry => entry.item)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            int current = ChartListItems.IndexOf(sorted[i]);

            if (current != i)
                ChartListItems.Move(current, i);
        }

        SelectedChartListItem ??= ChartListItems.FirstOrDefault();
    }

    private static int OrderIndex(List<string> order, string key)
    {
        int index = order.FindIndex(item => item.Equals(key, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>Mueve una métrica de la vista de lista al arrastrarla y guarda el orden.</summary>
    public void MoveChartListItem(ChartPanelViewModel item, int newIndex)
    {
        int oldIndex = ChartListItems.IndexOf(item);

        if (oldIndex < 0 || newIndex < 0 || newIndex >= ChartListItems.Count || oldIndex == newIndex)
            return;

        ChartListItems.Move(oldIndex, newIndex);
        _config.ChartListOrder = ChartListItems.Select(entry => entry.SelectedMetric.Key).ToList();
        SaveSoon();
    }

    private void RefreshChartPanels()
    {
        foreach (ChartPanelViewModel panel in ChartPanels)
            panel.Refresh();

        // En la vista de lista se actualizan todas: cada entrada tiene su mini gráfico y su resumen.
        foreach (ChartPanelViewModel item in ChartListItems)
            item.Refresh();
    }

    private void UpdateMemory()
    {
        MemoryUsage usage = MemoryUsageReader.Read();

        if (usage.TotalBytes <= 0)
            return;

        RamTotalGb = usage.TotalGb;
        RamUsedGb = usage.UsedGb;
        RamPercent = usage.UsedPercent;
        RamUsageText = Loc.F("vram.text", RamUsedGb, RamTotalGb, RamPercent);
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

        // Un gráfico por núcleo, en orden, entre las métricas fijas y los discos.
        IEnumerable<ChartMetric> coreMetrics = Enumerable.Range(1, CoreLoads.Count).Select(ChartMetrics.ForCore);

        IEnumerable<ChartMetric> orphanedCores = ChartPanels
            .Select(panel => panel.SelectedMetric)
            .Where(metric => metric.Key.StartsWith(ChartMetrics.CoreKeyPrefix, StringComparison.Ordinal));

        AllChartMetrics = ChartMetrics.Base.Concat(coreMetrics).Concat(orphanedCores)
            .Concat(diskMetrics).Concat(orphaned).Distinct().ToList();

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
        SyncChartListItems();
    }

    /// <summary>Totales del equipo para los encabezados del administrador de procesos.</summary>
    private void UpdateProcessTotals()
    {
        if (!Processes.IsActive)
            return;

        double? disk = _disks.Count > 0 ? _disks.Values.Max(d => d.ActivePercent) : null;
        double? network = null;

        if (_networkUsage.LinkBitsPerSecond > 0 && NetDownMbs is { } down && NetUpMbs is { } up)
        {
            double bits = (down + up) * 1024 * 1024 * 8;
            network = Math.Clamp(bits / _networkUsage.LinkBitsPerSecond * 100, 0, 100);
        }

        Processes.SetTotals(CpuLoad, RamPercent, disk, network);
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
