using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FanCtrl.Core.Hardware;

namespace FanCtrl.App.ViewModels;

/// <summary>Fila de un sensor dentro de la tabla de monitorización.</summary>
public sealed partial class SensorRowViewModel : ObservableObject
{
    public SensorRowViewModel(SensorReading reading)
    {
        Id = reading.Id;
        Kind = reading.Kind;
        Name = reading.Name;
        UnitText = reading.Unit;
        Update(reading);
    }

    public string Id { get; }

    public SensorKind Kind { get; }

    public string Name { get; }

    public string UnitText { get; }

    /// <summary>Temperatura, carga y PWM se representan con una barra; RPM, W y V no.</summary>
    public bool ShowsBar => Kind is SensorKind.Temperature or SensorKind.Load or SensorKind.Pwm;

    [ObservableProperty]
    private string _valueText = "—";

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private Brush _valueBrush = UiBrushes.TextSecondary;

    public void Update(SensorReading reading)
    {
        ValueText = reading.ValueText;

        if (ShowsBar)
            Percent = Math.Clamp(reading.Value, 0, 100);

        ValueBrush = Kind switch
        {
            SensorKind.Temperature => reading.Value >= 85
                ? UiBrushes.Danger
                : reading.Value >= 70
                    ? UiBrushes.Warning
                    : UiBrushes.Success,
            SensorKind.Load or SensorKind.Pwm => UiBrushes.Accent,
            _ => UiBrushes.TextPrimary
        };
    }
}
