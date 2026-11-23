using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CtrlMonitor.Core.Hardware;
using CtrlMonitor.Core.Inventory;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Barra de un hilo del CPU en la tarjeta del procesador.</summary>
public sealed partial class ThreadLoadViewModel : ObservableObject
{
    public ThreadLoadViewModel(int core, int thread)
    {
        Core = core;
        Thread = thread;
    }

    public int Core { get; }

    public int Thread { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fraction))]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    private double _load;

    /// <summary>De 0 a 1, para escalar la barra.</summary>
    public double Fraction => Math.Clamp(Load / 100, 0, 1);

    public string Tooltip => Loc.F("summary.threadTooltip", Core, Thread, Load.ToString("0"));

    public void RefreshLanguage() => OnPropertyChanged(nameof(Tooltip));
}

/// <summary>Hilos del CPU y lectura/escritura de los discos en Inicio.</summary>
public partial class MainViewModel
{
    /// <summary>Carga de cada hilo del CPU, en orden de núcleo e hilo.</summary>
    public ObservableCollection<ThreadLoadViewModel> CpuThreads { get; } = new();

    public bool HasCpuThreads => CpuThreads.Count > 0;

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

            foreach (CpuThreadLoad load in loads)
                CpuThreads.Add(new ThreadLoadViewModel(load.Core, load.Thread));

            OnPropertyChanged(nameof(HasCpuThreads));
            OnPropertyChanged(nameof(CpuThreadsTitle));
        }

        for (int i = 0; i < loads.Count; i++)
            CpuThreads[i].Load = loads[i].Load;
    }

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

        foreach (ThreadLoadViewModel thread in CpuThreads)
            thread.RefreshLanguage();
    }
}
