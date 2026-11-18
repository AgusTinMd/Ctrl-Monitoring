using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CtrlMonitor.Core.Diagnostics;
using CtrlMonitor.Core.Localization;
using CtrlMonitor.Core.Processes;

namespace CtrlMonitor.App.ViewModels;

/// <summary>
/// Una fila del administrador de procesos: un programa (grupo) o uno de sus
/// procesos. Los valores se muestran con un fondo más intenso cuanto más
/// consumen, como en el Administrador de tareas.
/// </summary>
public abstract partial class ProcessNode : ObservableObject
{
    public string Description { get; protected init; } = string.Empty;

    public string Name { get; protected init; } = string.Empty;

    public ImageSource? Icon { get; protected init; }

    public bool HasIcon => Icon is not null;

    /// <summary>Cantidad de procesos junto al nombre del programa: «(12)».</summary>
    public virtual string CountText => string.Empty;

    /// <summary>Texto del tooltip: ruta y PID.</summary>
    public abstract string Details { get; }

    public abstract bool IsProtected { get; }

    /// <summary>Nivel en el árbol: 0 para programas, 1 para sus procesos.</summary>
    public abstract int Level { get; }

    public Thickness NameIndent => new(Level * 26, 0, 0, 0);

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private string _cpuText = string.Empty;

    [ObservableProperty]
    private Brush _cpuHeat = Brushes.Transparent;

    [ObservableProperty]
    private long _memoryBytes;

    [ObservableProperty]
    private string _memoryText = string.Empty;

    [ObservableProperty]
    private Brush _memoryHeat = Brushes.Transparent;

    [ObservableProperty]
    private double _diskBytes;

    [ObservableProperty]
    private string _diskText = string.Empty;

    [ObservableProperty]
    private Brush _diskHeat = Brushes.Transparent;

    [ObservableProperty]
    private double _networkBytes;

    [ObservableProperty]
    private string _networkText = string.Empty;

    [ObservableProperty]
    private Brush _networkHeat = Brushes.Transparent;

    protected void SetValues(double? cpu, long memory, double? disk, double? network)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;

        CpuPercent = cpu ?? 0;
        CpuText = cpu is { } c ? c.ToString("0.0", culture) + " %" : "—";
        CpuHeat = Heat.For(CpuPercent / 50);

        MemoryBytes = memory;
        MemoryText = (memory / (1024d * 1024d)).ToString("#,##0.0", culture) + " MB";
        MemoryHeat = Heat.For(memory / (1536d * 1024 * 1024));

        DiskBytes = disk ?? 0;
        DiskText = disk is { } d ? (d / (1024d * 1024d)).ToString("0.0", culture) + " MB/s" : "—";
        DiskHeat = Heat.For(DiskBytes / (20d * 1024 * 1024));

        NetworkBytes = network ?? 0;
        NetworkText = network is { } n ? (n * 8 / 1_000_000d).ToString("0.0", culture) + " Mbps" : "—";
        NetworkHeat = Heat.For(NetworkBytes * 8 / 20_000_000d);
    }
}

/// <summary>Un proceso.</summary>
public sealed class ProcessLeaf : ProcessNode
{
    public ProcessLeaf(ProcessSample sample, string description, ImageSource? icon)
    {
        Id = sample.Id;
        StartTicks = sample.StartTicks;
        Name = sample.Name;
        Path = sample.Path;
        Description = description;
        Icon = icon;
        Protected = ProcessKiller.IsProtected(sample.Id, sample.Name);
        Update(sample);
    }

    public int Id { get; }

    public long StartTicks { get; }

    public string? Path { get; }

    /// <summary>
    /// Programa en el que quedó el proceso. Se fija al crearlo: a veces Windows
    /// deja leer la ruta del ejecutable en una lectura y no en la siguiente, y
    /// el proceso no debe cambiar de grupo por eso.
    /// </summary>
    public string GroupKey { get; init; } = string.Empty;

    private bool Protected { get; }

    public override bool IsProtected => Protected;

    public override int Level => 1;

    public override string Details => $"{Path ?? Name}\nPID {Id}";

    public ProcessSample Sample { get; private set; } = null!;

    public void Update(ProcessSample sample)
    {
        Sample = sample;
        SetValues(sample.CpuPercent, sample.MemoryBytes, sample.DiskBytesPerSecond, sample.NetworkBytesPerSecond);
    }
}

/// <summary>Un programa: todos los procesos del mismo ejecutable.</summary>
public sealed partial class ProcessGroup : ProcessNode
{
    public ProcessGroup(string key, string description, string name, ImageSource? icon)
    {
        Key = key;
        Description = description;
        Name = name;
        Icon = icon;
        ChildrenView = new ListCollectionView(Children);
    }

    public string Key { get; }

    public ObservableCollection<ProcessLeaf> Children { get; } = new();

    /// <summary>Procesos del programa, ordenados igual que la lista.</summary>
    public ListCollectionView ChildrenView { get; }

    /// <summary>Solo los programas con más de un proceso se despliegan.</summary>
    public bool HasChildren => Children.Count > 1;

    public override string CountText => Children.Count > 1 ? $" ({Children.Count})" : string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    public override bool IsProtected => Children.All(child => child.IsProtected);

    public override int Level => 0;

    public override string Details => Children.Count == 1
        ? Children[0].Details
        : string.Join("\n", Children.Take(12).Select(child => $"PID {child.Id}"))
          + (Children.Count > 12 ? "\n…" : string.Empty);

    public void Recalculate()
    {
        bool anyCpu = Children.Any(child => child.Sample.CpuPercent is not null);
        bool anyDisk = Children.Any(child => child.Sample.DiskBytesPerSecond is not null);
        bool anyNet = Children.Any(child => child.Sample.NetworkBytesPerSecond is not null);

        SetValues(
            anyCpu ? Children.Sum(child => child.Sample.CpuPercent ?? 0) : null,
            Children.Sum(child => child.Sample.MemoryBytes),
            anyDisk ? Children.Sum(child => child.Sample.DiskBytesPerSecond ?? 0) : null,
            anyNet ? Children.Sum(child => child.Sample.NetworkBytesPerSecond ?? 0) : null);

        OnPropertyChanged(nameof(HasChildren));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsProtected));
        OnPropertyChanged(nameof(Details));
    }
}

/// <summary>Fondos de «calor» para el tema oscuro: de transparente a naranja.</summary>
internal static class Heat
{
    private static readonly Brush[] Steps = Enumerable.Range(0, 11).Select(Make).ToArray();

    public static Brush For(double intensity)
    {
        if (double.IsNaN(intensity) || intensity <= 0.02)
            return Brushes.Transparent;

        return Steps[(int)Math.Round(Math.Clamp(intensity, 0, 1) * 10)];
    }

    private static Brush Make(int step)
    {
        double t = step / 10d;

        // Ámbar suave que va hacia naranja y gana opacidad con el consumo.
        byte r = 245;
        byte g = (byte)(180 - 60 * t);
        byte b = (byte)(40 - 20 * t);
        byte a = (byte)(28 + 150 * t);
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Administrador de procesos: lista los programas y sus procesos con CPU,
/// memoria, disco y red, y permite finalizarlos. Solo lee mientras la pestaña
/// está a la vista.
/// </summary>
public sealed partial class ProcessesViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly ProcessNetworkMonitor _network = new();
    private readonly ProcessSampler _sampler;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<(int, long), ProcessLeaf> _leaves = new();
    private readonly Dictionary<string, ProcessGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (string Description, ImageSource? Icon)> _fileInfo =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _reading;

    public ProcessesViewModel()
    {
        _sampler = new ProcessSampler(_network);
        View = new ListCollectionView(Groups) { Filter = Matches, IsLiveSorting = true };
        ApplySort();

        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += (_, _) => Refresh();
    }

    public ObservableCollection<ProcessGroup> Groups { get; } = new();

    /// <summary>Programas ordenados y filtrados.</summary>
    public ListCollectionView View { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EndTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndTreeCommand))]
    private ProcessNode? _selected;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Columna de orden: «Name», «Cpu», «Memory», «Disk» o «Network».</summary>
    [ObservableProperty]
    private string _sortColumn = "Cpu";

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private string _nameArrow = string.Empty;

    [ObservableProperty]
    private string _cpuArrow = string.Empty;

    [ObservableProperty]
    private string _memoryArrow = string.Empty;

    [ObservableProperty]
    private string _diskArrow = string.Empty;

    [ObservableProperty]
    private string _networkArrow = string.Empty;

    // Totales del equipo para los encabezados, como el Administrador de tareas.
    [ObservableProperty]
    private string _cpuTotal = "—";

    [ObservableProperty]
    private string _memoryTotal = "—";

    [ObservableProperty]
    private string _diskTotal = "—";

    [ObservableProperty]
    private string _networkTotal = "—";

    // Anchos de las columnas de valores; el nombre ocupa el resto. Los separadores
    // del encabezado los cambian y todas las filas los siguen.
    [ObservableProperty]
    private GridLength _cpuWidth = new(96);

    [ObservableProperty]
    private GridLength _memoryWidth = new(116);

    [ObservableProperty]
    private GridLength _diskWidth = new(100);

    [ObservableProperty]
    private GridLength _networkWidth = new(96);

    private Core.Configuration.AppConfig? _config;
    private Action? _save;

    /// <summary>Toma los anchos guardados y la forma de guardarlos.</summary>
    public void Attach(Core.Configuration.AppConfig config, Action save)
    {
        _config = config;
        List<double> widths = config.ProcessColumnWidths;
        CpuWidth = new GridLength(widths[0]);
        MemoryWidth = new GridLength(widths[1]);
        DiskWidth = new GridLength(widths[2]);
        NetworkWidth = new GridLength(widths[3]);
        _save = save;
    }

    partial void OnCpuWidthChanged(GridLength value) => SaveWidths();

    partial void OnMemoryWidthChanged(GridLength value) => SaveWidths();

    partial void OnDiskWidthChanged(GridLength value) => SaveWidths();

    partial void OnNetworkWidthChanged(GridLength value) => SaveWidths();

    /// <summary>Cambia el ancho de una columna de valores, con un mínimo y un máximo razonables.</summary>
    public void ResizeColumn(string column, double delta)
    {
        static GridLength Grow(GridLength width, double change) =>
            new(Math.Clamp(width.Value + change, 56, 420));

        switch (column)
        {
            case "Cpu": CpuWidth = Grow(CpuWidth, delta); break;
            case "Memory": MemoryWidth = Grow(MemoryWidth, delta); break;
            case "Disk": DiskWidth = Grow(DiskWidth, delta); break;
            case "Network": NetworkWidth = Grow(NetworkWidth, delta); break;
        }
    }

    private void SaveWidths()
    {
        if (_config is null || _save is null)
            return;

        _config.ProcessColumnWidths = new List<double>
        {
            Math.Round(CpuWidth.Value), Math.Round(MemoryWidth.Value),
            Math.Round(DiskWidth.Value), Math.Round(NetworkWidth.Value)
        };
        _save();
    }

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>La pestaña está a la vista: mide y refresca; si no, no lee nada.</summary>
    public bool IsActive
    {
        get => _timer.IsEnabled;
        set
        {
            if (value == _timer.IsEnabled)
                return;

            if (value)
            {
                _network.Start();

                if (_network.IsRunning)
                    AppLog.Info("Medición de red por proceso activa (ETW).");
                else
                    AppLog.Warn($"Sin medición de red por proceso: {_network.LastError}");

                _timer.Start();
                Refresh();
            }
            else
            {
                _timer.Stop();
                _network.Stop();
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Totales del equipo que ya mide la ventana principal.</summary>
    public void SetTotals(double? cpu, double? memory, double? disk, double? network)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CpuTotal = cpu is { } c ? c.ToString("0", culture) + " %" : "—";
        MemoryTotal = memory is { } m ? m.ToString("0", culture) + " %" : "—";
        DiskTotal = disk is { } d ? d.ToString("0", culture) + " %" : "—";
        NetworkTotal = network is { } n ? n.ToString("0", culture) + " %" : "—";
    }

    partial void OnSearchTextChanged(string value) => View.Refresh();

    partial void OnSortColumnChanged(string value) => ApplySort();

    partial void OnSortDescendingChanged(bool value) => ApplySort();

    /// <summary>Clic en un encabezado: ordena por esa columna o invierte el orden.</summary>
    [RelayCommand]
    private void SortBy(string column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
            return;
        }

        // Los consumos se miran de mayor a menor; el nombre, de la A a la Z.
        SortDescending = column != "Name";
        SortColumn = column;
    }

    private void ApplySort()
    {
        string arrow = SortDescending ? " ▾" : " ▴";
        NameArrow = SortColumn == "Name" ? arrow : string.Empty;
        CpuArrow = SortColumn == "Cpu" ? arrow : string.Empty;
        MemoryArrow = SortColumn == "Memory" ? arrow : string.Empty;
        DiskArrow = SortColumn == "Disk" ? arrow : string.Empty;
        NetworkArrow = SortColumn == "Network" ? arrow : string.Empty;

        Sort(View);

        foreach (ProcessGroup group in Groups)
            Sort(group.ChildrenView);
    }

    private void Sort(ListCollectionView view)
    {
        string property = SortColumn switch
        {
            "Name" => nameof(ProcessNode.Description),
            "Memory" => nameof(ProcessNode.MemoryBytes),
            "Disk" => nameof(ProcessNode.DiskBytes),
            "Network" => nameof(ProcessNode.NetworkBytes),
            _ => nameof(ProcessNode.CpuPercent)
        };

        var direction = SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;

        using (view.DeferRefresh())
        {
            view.IsLiveSorting = true;
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(property, direction));
            view.LiveSortingProperties.Clear();
            view.LiveSortingProperties.Add(property);
        }
    }

    private bool Matches(object item)
    {
        if (item is not ProcessGroup group || string.IsNullOrWhiteSpace(SearchText))
            return true;

        string text = SearchText.Trim();
        return group.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
               || group.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
               || group.Children.Any(child =>
                   child.Id.ToString(CultureInfo.InvariantCulture).StartsWith(text, StringComparison.Ordinal));
    }

    /// <summary>Lee los procesos en segundo plano y actualiza la lista sin recrearla.</summary>
    private void Refresh()
    {
        if (_reading)
            return;

        _reading = true;
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

        Task.Run(() =>
        {
            IReadOnlyList<ProcessSample> samples = _sampler.Read();

            // Descripción e ícono se leen una vez por ejecutable y quedan en caché.
            List<string> newPaths = samples
                .Select(sample => sample.Path)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !_fileInfo.ContainsKey(path))
                .ToList();

            if (newPaths.Count > 0)
                ReadFileInfoOnStaThread(newPaths);

            return samples;
        }).ContinueWith(task => dispatcher.BeginInvoke(new Action(() =>
        {
            _reading = false;

            if (task.IsCompletedSuccessfully)
                Apply(task.Result);
            else if (task.Exception is { } ex)
                AppLog.Warn($"No se pudieron leer los procesos: {ex.GetBaseException().Message}");
        })), TaskScheduler.Default);
    }

    private void Apply(IReadOnlyList<ProcessSample> samples)
    {
        var seen = new HashSet<(int, long)>();
        var touched = new HashSet<ProcessGroup>();

        foreach (ProcessSample sample in samples)
        {
            seen.Add(sample.Key);

            if (_leaves.TryGetValue(sample.Key, out ProcessLeaf? leaf))
            {
                leaf.Update(sample);

                if (_groups.TryGetValue(leaf.GroupKey, out ProcessGroup? current))
                    touched.Add(current);

                continue;
            }

            (string description, ImageSource? icon) = sample.Path is { } path && _fileInfo.TryGetValue(path, out var info)
                ? info
                : (string.Empty, null);

            string label = description.Length > 0 ? description : sample.Name;
            string key = GroupKey(sample);
            leaf = new ProcessLeaf(sample, label, icon) { GroupKey = key };
            _leaves[sample.Key] = leaf;

            if (!_groups.TryGetValue(key, out ProcessGroup? group))
            {
                group = new ProcessGroup(key, label, sample.Name, icon);
                _groups[key] = group;
                Sort(group.ChildrenView);
                Groups.Add(group);
            }

            group.Children.Add(leaf);
            touched.Add(group);
        }

        foreach ((int, long) key in _leaves.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            ProcessLeaf leaf = _leaves[key];
            _leaves.Remove(key);

            if (_groups.TryGetValue(leaf.GroupKey, out ProcessGroup? group))
            {
                group.Children.Remove(leaf);
                touched.Add(group);

                if (group.Children.Count == 0)
                {
                    Groups.Remove(group);
                    _groups.Remove(group.Key);
                    touched.Remove(group);
                }
            }
        }

        foreach (ProcessGroup group in touched)
            group.Recalculate();

        Summary = Loc.F("processes.summary", samples.Count, Groups.Count);
    }

    // Los procesos del mismo ejecutable forman un programa; los que no tienen
    // ruta legible (procesos del sistema) se agrupan por nombre.
    private static string GroupKey(ProcessSample sample) => sample.Path ?? "name:" + sample.Name;

    private bool CanEnd() => Selected is { IsProtected: false };

    [RelayCommand(CanExecute = nameof(CanEnd))]
    private void EndTask() => End(entireTree: false);

    [RelayCommand(CanExecute = nameof(CanEnd))]
    private void EndTree() => End(entireTree: true);

    private void End(bool entireTree)
    {
        if (Selected is not { } node)
            return;

        List<ProcessLeaf> targets = node switch
        {
            ProcessGroup group => group.Children.Where(child => !child.IsProtected).ToList(),
            ProcessLeaf leaf => new List<ProcessLeaf> { leaf },
            _ => new List<ProcessLeaf>()
        };

        if (targets.Count == 0)
            return;

        string question = targets.Count > 1
            ? Loc.F(entireTree ? "processes.confirmGroupTree" : "processes.confirmGroup", node.Description, targets.Count)
            : Loc.F(entireTree ? "processes.confirmTree" : "processes.confirm", node.Description, targets[0].Id);

        if (MessageBox.Show(question, "Ctrl Monitor", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        var results = targets.Select(target => ProcessKiller.Kill(target.Id, target.StartTicks, target.Name, entireTree)).ToList();
        AppLog.Info($"Finalizar {node.Name} ({targets.Count} procesos), árbol={entireTree}: {string.Join(", ", results)}");

        // Con varios procesos, «ya no estaba» es normal: al cerrar uno se cierran sus hijos.
        KillResult result = results.Any(r => r == KillResult.Done)
            ? KillResult.Done
            : results.FirstOrDefault(r => r != KillResult.NotFound, KillResult.NotFound);

        Status = result switch
        {
            KillResult.Done => Loc.F("processes.ended", node.Description),
            KillResult.NotFound => Loc.T("processes.notFound"),
            KillResult.Protected => Loc.T("processes.protected"),
            KillResult.AccessDenied => Loc.T("processes.accessDenied"),
            _ => Loc.T("processes.failed")
        };

        Refresh();
    }

    /// <summary>Vuelve a armar los textos después de cambiar de idioma.</summary>
    public void RefreshLanguage()
    {
        if (_leaves.Count > 0)
            Refresh();
    }

    /// <summary>
    /// Lee descripciones e íconos en un hilo STA propio: SHGetFileInfo necesita
    /// COM inicializado, y llamarlo desde un hilo común puede hacer que una
    /// extensión del Explorador cierre la app sin dejar rastro.
    /// </summary>
    private void ReadFileInfoOnStaThread(List<string> paths)
    {
        var thread = new Thread(() =>
        {
            foreach (string path in paths)
            {
                try
                {
                    _fileInfo[path] = ReadFileInfo(path);
                }
                catch (Exception ex)
                {
                    _fileInfo[path] = (string.Empty, null);
                    AppLog.Warn($"No se pudo leer el ícono de {path}: {ex.Message}");
                }
            }
        })
        {
            IsBackground = true,
            Name = "CtrlMonitor íconos"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static (string Description, ImageSource? Icon) ReadFileInfo(string path)
    {
        string description = string.Empty;

        try
        {
            description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Sin descripción se muestra el nombre del proceso.
        }

        return (description, ShellIcon.Small(path));
    }

    public void Dispose()
    {
        _timer.Stop();
        _network.Dispose();
    }
}

/// <summary>Ícono chico de un ejecutable, como lo muestra el Explorador.</summary>
internal static class ShellIcon
{
    private const uint GetIcon = 0x100;
    private const uint SmallIcon = 0x1;

    public static ImageSource? Small(string path)
    {
        var info = new ShFileInfo();
        IntPtr result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), GetIcon | SmallIcon);

        if (result == IntPtr.Zero || info.Icon == IntPtr.Zero)
            return null;

        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);
}
