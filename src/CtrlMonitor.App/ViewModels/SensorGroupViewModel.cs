using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CtrlMonitor.Core.Hardware;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Grupo de sensores de un mismo componente (CPU, GPU, placa, Super I/O).</summary>
public sealed partial class SensorGroupViewModel : ObservableObject
{
    private readonly Dictionary<string, SensorRowViewModel> _rowsById = new(StringComparer.Ordinal);

    public SensorGroupViewModel(string hardwareId, string name, string hardwareType)
    {
        HardwareId = hardwareId;
        Name = name;
        HardwareType = hardwareType;
    }

    public string HardwareId { get; }

    public string Name { get; }

    public string TypeText => HardwareType switch
    {
        "Cpu" => Loc.T("hw.cpu"),
        "GpuAmd" or "GpuNvidia" => Loc.T("hw.gpu"),
        "Motherboard" => Loc.T("hw.board"),
        "SuperIO" => Loc.T("hw.superio"),
        _ => HardwareType
    };

    public string HardwareType { get; }

    public string IconKey => HardwareType switch
    {
        "Cpu" => "Cpu",
        "GpuAmd" or "GpuNvidia" => "Gpu",
        "Storage" => "Disk",
        _ => "Board"
    };

    public ObservableCollection<SensorRowViewModel> Rows { get; } = new();

    public string CountText => Loc.F("sensors.count", Rows.Count);

    /// <summary>Vuelve a traducir los textos después de cambiar de idioma.</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>
    /// Indica si la tarjeta del grupo muestra sus filas de sensores. Arranca
    /// replegada para que la página se vea completa de un vistazo.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Actualiza las filas existentes por identificador y agrega o quita las que
    /// cambian. Evita recrear la lista entera en cada refresco.
    /// </summary>
    public void Update(IReadOnlyList<SensorReading> readings)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (SensorReading reading in readings)
        {
            seen.Add(reading.Id);

            if (_rowsById.TryGetValue(reading.Id, out SensorRowViewModel? row))
            {
                row.Update(reading);
            }
            else
            {
                row = new SensorRowViewModel(reading);
                _rowsById[reading.Id] = row;
                Rows.Add(row);
            }
        }

        foreach (string id in _rowsById.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Rows.Remove(_rowsById[id]);
            _rowsById.Remove(id);
        }

        OnPropertyChanged(nameof(CountText));
    }
}
