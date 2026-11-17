using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FanCtrl.Core.Hardware;

namespace FanCtrl.App.ViewModels;

/// <summary>Grupo de sensores de un mismo componente (CPU, GPU, placa, Super I/O).</summary>
public sealed partial class SensorGroupViewModel : ObservableObject
{
    private readonly Dictionary<string, SensorRowViewModel> _rowsById = new(StringComparer.Ordinal);

    public SensorGroupViewModel(string hardwareId, string name, string hardwareType)
    {
        HardwareId = hardwareId;
        Name = name;
        HardwareType = hardwareType;
        TypeText = hardwareType switch
        {
            "Cpu" => "Procesador",
            "GpuAmd" or "GpuNvidia" => "Tarjeta gráfica",
            "Motherboard" => "Placa madre",
            "SuperIO" => "Super I/O",
            _ => hardwareType
        };
    }

    public string HardwareId { get; }

    public string Name { get; }

    public string TypeText { get; }

    public string HardwareType { get; }

    public string IconKey => HardwareType switch
    {
        "Cpu" => "Cpu",
        "GpuAmd" or "GpuNvidia" => "Gpu",
        _ => "Board"
    };

    public ObservableCollection<SensorRowViewModel> Rows { get; } = new();

    public string CountText => $"{Rows.Count} sensores";

    /// <summary>Indica si la tarjeta del grupo muestra sus filas de sensores.</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

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
