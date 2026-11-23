using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CtrlMonitor.Core.Hardware;
using CtrlMonitor.Core.Inventory;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Barra de un hilo del CPU en la tarjeta del procesador.</summary>
public sealed partial class ThreadLoadViewModel : ObservableObject
{
    public ThreadLoadViewModel(int index, int core, int thread)
    {
        Index = index;
        Core = core;
        Thread = thread;
    }

    /// <summary>Número de hilo lógico, desde 1, como en el Administrador de tareas.</summary>
    public int Index { get; }

    public int Core { get; }

    public int Thread { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fraction))]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    [NotifyPropertyChangedFor(nameof(LoadText))]
    private double _load;

    /// <summary>Etiqueta corta: «H1», «T1»…</summary>
    public string Label => Thread == 0 ? Loc.F("summary.coreShort", Core) : Loc.F("summary.threadShort", Index);

    public string LoadText => Load.ToString("0") + "%";

    /// <summary>De 0 a 1, para escalar la barra.</summary>
    public double Fraction => Math.Clamp(Load / 100, 0, 1);

    public string Tooltip => Thread == 0
        ? Loc.F("summary.coreTooltip", Core, Load.ToString("0"))
        : Loc.F("summary.threadTooltip", Core, Thread, Load.ToString("0"));

    /// <summary>Muestras para el mini gráfico del hilo (las últimas <see cref="HistorySize"/>).</summary>
    public const int HistorySize = 60;

    private readonly List<double> _samples = new();

    [ObservableProperty]
    private IReadOnlyList<double> _history = Array.Empty<double>();

    public void Push(double load)
    {
        Load = load;
        _samples.Add(load);

        if (_samples.Count > HistorySize)
            _samples.RemoveAt(0);

        History = _samples.ToArray();
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Tooltip));
    }
}

/// <summary>Hilos del CPU y lectura/escritura de los discos en Inicio.</summary>
public partial class MainViewModel
{
    /// <summary>Carga de cada hilo del CPU, en orden de núcleo e hilo.</summary>
    public ObservableCollection<ThreadLoadViewModel> CpuThreads { get; } = new();

    public bool HasCpuThreads => CpuThreads.Count > 0;

    /// <summary>Hilo más cargado, para el gráfico de hilos.</summary>
    public double? BusiestThreadLoad => CpuThreads.Count == 0 ? null : CpuThreads.Max(t => t.Load);

    /// <summary>La sección de hilos de Inicio está desplegada; se recuerda entre sesiones.</summary>
    public bool CpuThreadsExpanded
    {
        get => _config.CpuThreadsExpanded;
        set
        {
            if (_config.CpuThreadsExpanded == value)
                return;

            _config.CpuThreadsExpanded = value;
            OnPropertyChanged();
            SaveSoon();
        }
    }

    public string CpuThreadsTitle => Loc.F("summary.threads", CpuThreads.Count);

    private void UpdateCpuThreads(IReadOnlyList<SensorReading> cpu)
    {
        IReadOnlyList<CpuThreadLoad> loads = CpuThreads_From(cpu);

        bool sameShape = loads.Count == CpuThreads.Count
                         && loads.Select(l => (l.Core, l.Thread))
                             .SequenceEqual(CpuThreads.Select(t => (t.Core, t.Thread)));

        // Se actualizan en el lugar para no recrear las barras en cada lectura.
        if (!sameShape)
        {
            CpuThreads.Clear();

            for (int i = 0; i < loads.Count; i++)
                CpuThreads.Add(new ThreadLoadViewModel(i + 1, loads[i].Core, loads[i].Thread));

            OnPropertyChanged(nameof(HasCpuThreads));
            OnPropertyChanged(nameof(CpuThreadsTitle));
        }

        for (int i = 0; i < loads.Count; i++)
            CpuThreads[i].Push(loads[i].Load);

        // Carga de cada núcleo: el promedio de sus hilos.
        List<double> cores = CpuThreads
            .GroupBy(t => t.Core)
            .OrderBy(g => g.Key)
            .Select(g => g.Average(t => t.Load))
            .ToList();

        bool coreCountChanged = cores.Count != CoreLoads.Count;
        CoreLoads = cores;

        if (coreCountChanged)
        {
            CpuCores.Clear();

            for (int i = 0; i < cores.Count; i++)
                CpuCores.Add(new ThreadLoadViewModel(i + 1, i + 1, 0));
        }

        for (int i = 0; i < cores.Count; i++)
            CpuCores[i].Push(cores[i]);

        if (coreCountChanged)
            RebuildChartMetrics();
    }

    /// <summary>Carga de cada núcleo con su historial, para el gráfico «Núcleos CPU».</summary>
    public ObservableCollection<ThreadLoadViewModel> CpuCores { get; } = new();

    /// <summary>Núcleo más cargado, para el gráfico de núcleos.</summary>
    public double? BusiestCoreLoad => CoreLoads.Count == 0 ? null : CoreLoads.Max();

    /// <summary>Carga actual de cada núcleo, desde el núcleo 1.</summary>
    public IReadOnlyList<double> CoreLoads { get; private set; } = Array.Empty<double>();

    /// <summary>Carga de un núcleo (desde 1), o null si no existe.</summary>
    public double? GetCoreLoad(int core) =>
        core >= 1 && core <= CoreLoads.Count ? CoreLoads[core - 1] : null;

    private static IReadOnlyList<CpuThreadLoad> CpuThreads_From(IReadOnlyList<SensorReading> cpu) =>
        Core.Hardware.CpuThreads.From(cpu);

    /// <summary>Pasa la lectura y escritura de cada disco a su bloque en Inicio.</summary>
    private void UpdateStorageActivity()
    {
        foreach (StorageDiskViewModel disk in StorageDisks)
        {
            if (disk.Number < 0)
                continue;

            DiskActivity? activity = _disks.Values.FirstOrDefault(d => DiskNames.Number(d.Id) == disk.Number);

            if (activity is not null)
                disk.UpdateActivity(activity.ReadMegaBytesPerSecond, activity.WriteMegaBytesPerSecond);
        }
    }

    private void RefreshThreadsLanguage()
    {
        OnPropertyChanged(nameof(CpuThreadsTitle));

        foreach (ThreadLoadViewModel thread in CpuThreads.Concat(CpuCores))
            thread.RefreshLanguage();
    }
}
