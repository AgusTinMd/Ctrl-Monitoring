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

/// <summary>Un proceso en la lista del administrador de procesos.</summary>
public sealed partial class ProcessRowViewModel : ObservableObject
{
    public ProcessRowViewModel(ProcessSample sample, string description, ImageSource? icon)
    {
        Id = sample.Id;
        StartTicks = sample.StartTicks;
        Name = sample.Name;
        Path = sample.Path;
        Description = description;
        Icon = icon;
        IsProtected = ProcessKiller.IsProtected(sample.Id, sample.Name);
        Update(sample);
    }

    public int Id { get; }

    public long StartTicks { get; }

    public string Name { get; }

    public string? Path { get; }

    /// <summary>Descripción del ejecutable («Google Chrome»), o el nombre si no tiene.</summary>
    public string Description { get; }

    public ImageSource? Icon { get; }

    public bool HasIcon => Icon is not null;

    /// <summary>Proceso crítico de Windows o la propia app: no se puede finalizar.</summary>
    public bool IsProtected { get; }

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private string _cpuText = "—";

    [ObservableProperty]
    private long _memoryBytes;

    [ObservableProperty]
    private string _memoryText = string.Empty;

    public void Update(ProcessSample sample)
    {
        CpuPercent = sample.CpuPercent ?? 0;
        CpuText = sample.CpuPercent is { } cpu ? cpu.ToString("0.0", CultureInfo.CurrentCulture) + " %" : "—";
        MemoryBytes = sample.MemoryBytes;
        MemoryText = FormatMemory(sample.MemoryBytes);
    }

    private static string FormatMemory(long bytes)
    {
        double megabytes = bytes / (1024d * 1024d);
        return megabytes >= 1024
            ? (megabytes / 1024d).ToString("0.0", CultureInfo.CurrentCulture) + " GB"
            : megabytes.ToString("0.0", CultureInfo.CurrentCulture) + " MB";
    }
}

/// <summary>
/// Administrador de procesos: lista los procesos con su uso de CPU y memoria y
/// permite finalizarlos. Solo lee mientras la pestaña está a la vista.
/// </summary>
public sealed partial class ProcessesViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly ProcessSampler _sampler = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<(int, long), ProcessRowViewModel> _rows = new();
    private readonly ConcurrentDictionary<string, (string Description, ImageSource? Icon)> _fileInfo =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _reading;

    public ProcessesViewModel()
    {
        View = (ListCollectionView)CollectionViewSource.GetDefaultView(Processes);
        View.Filter = Matches;
        View.IsLiveSorting = true;
        View.IsLiveFiltering = false;
        ApplySort();

        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += (_, _) => Refresh();
    }

    public ObservableCollection<ProcessRowViewModel> Processes { get; } = new();

    /// <summary>Vista ordenada y filtrada que muestra la lista.</summary>
    public ListCollectionView View { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EndTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndTreeCommand))]
    private ProcessRowViewModel? _selected;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Columna de orden: «Name», «Id», «Cpu» o «Memory».</summary>
    [ObservableProperty]
    private string _sortColumn = "Cpu";

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private string _nameArrow = string.Empty;

    [ObservableProperty]
    private string _idArrow = string.Empty;

    [ObservableProperty]
    private string _cpuArrow = string.Empty;

    [ObservableProperty]
    private string _memoryArrow = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>La pestaña está a la vista: refresca; si no, no lee nada.</summary>
    public bool IsActive
    {
        get => _timer.IsEnabled;
        set
        {
            if (value == _timer.IsEnabled)
                return;

            if (value)
            {
                _timer.Start();
                Refresh();
            }
            else
            {
                _timer.Stop();
            }

            OnPropertyChanged();
        }
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

        // CPU y memoria se miran de mayor a menor; nombre y PID, de menor a mayor.
        SortDescending = column is "Cpu" or "Memory";
        SortColumn = column;
    }

    private void ApplySort()
    {
        string property = SortColumn switch
        {
            "Name" => nameof(ProcessRowViewModel.Description),
            "Id" => nameof(ProcessRowViewModel.Id),
            "Memory" => nameof(ProcessRowViewModel.MemoryBytes),
            _ => nameof(ProcessRowViewModel.CpuPercent)
        };

        var direction = SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;

        // Flecha junto al encabezado de la columna por la que se ordena.
        string arrow = SortDescending ? " ▾" : " ▴";
        NameArrow = SortColumn == "Name" ? arrow : string.Empty;
        IdArrow = SortColumn == "Id" ? arrow : string.Empty;
        CpuArrow = SortColumn == "Cpu" ? arrow : string.Empty;
        MemoryArrow = SortColumn == "Memory" ? arrow : string.Empty;

        using (View.DeferRefresh())
        {
            View.SortDescriptions.Clear();
            View.SortDescriptions.Add(new SortDescription(property, direction));
            View.LiveSortingProperties.Clear();
            View.LiveSortingProperties.Add(property);
        }
    }

    private bool Matches(object item)
    {
        if (item is not ProcessRowViewModel row || string.IsNullOrWhiteSpace(SearchText))
            return true;

        string text = SearchText.Trim();
        return row.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
               || row.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
               || row.Id.ToString(CultureInfo.InvariantCulture).StartsWith(text, StringComparison.Ordinal);
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
            foreach (ProcessSample sample in samples)
            {
                if (sample.Path is { } path && !_fileInfo.ContainsKey(path))
                    _fileInfo[path] = ReadFileInfo(path);
            }

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

        foreach (ProcessSample sample in samples)
        {
            seen.Add(sample.Key);

            if (_rows.TryGetValue(sample.Key, out ProcessRowViewModel? row))
            {
                row.Update(sample);
                continue;
            }

            (string description, ImageSource? icon) = sample.Path is { } path && _fileInfo.TryGetValue(path, out var info)
                ? info
                : (string.Empty, null);

            row = new ProcessRowViewModel(sample, description.Length > 0 ? description : sample.Name, icon);
            _rows[sample.Key] = row;
            Processes.Add(row);
        }

        foreach ((int, long) key in _rows.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            Processes.Remove(_rows[key]);
            _rows.Remove(key);
        }

        double cpu = samples.Sum(sample => sample.CpuPercent ?? 0);
        long memory = samples.Sum(sample => sample.MemoryBytes);
        Summary = Loc.F("processes.summary", samples.Count, Math.Min(cpu, 100), memory / (1024d * 1024d * 1024d));
    }

    private bool CanEnd() => Selected is { IsProtected: false };

    [RelayCommand(CanExecute = nameof(CanEnd))]
    private void EndTask() => End(entireTree: false);

    [RelayCommand(CanExecute = nameof(CanEnd))]
    private void EndTree() => End(entireTree: true);

    private void End(bool entireTree)
    {
        if (Selected is not { } row)
            return;

        string question = Loc.F(entireTree ? "processes.confirmTree" : "processes.confirm", row.Description, row.Id);

        if (MessageBox.Show(question, "Ctrl Monitor", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        KillResult result = ProcessKiller.Kill(row.Id, row.StartTicks, row.Name, entireTree);
        AppLog.Info($"Finalizar proceso {row.Name} ({row.Id}), árbol={entireTree}: {result}");

        Status = result switch
        {
            KillResult.Done => Loc.F("processes.ended", row.Description),
            KillResult.NotFound => Loc.T("processes.notFound"),
            KillResult.Protected => Loc.T("processes.protected"),
            KillResult.AccessDenied => Loc.T("processes.accessDenied"),
            _ => Loc.T("processes.failed")
        };

        Refresh();
    }

    /// <summary>Vuelve a armar el resumen después de cambiar de idioma.</summary>
    public void RefreshLanguage()
    {
        if (_rows.Count > 0)
            Refresh();
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

    public void Dispose() => _timer.Stop();
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
