using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CtrlMonitor.Core.Hardware;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Un canal de ventilador con su velocidad actual y el PWM que aplica el BIOS.</summary>
public sealed partial class FanViewModel : ObservableObject
{
    public FanViewModel(SensorReading reading)
    {
        Id = reading.Id;
        HardwareId = reading.HardwareId;
        HardwareName = reading.HardwareName;
        HardwareType = reading.HardwareType;
        Name = reading.Name;
        ChannelText = reading.Name;
        Update(reading, null);
    }

    public string Id { get; }

    public string HardwareId { get; }

    public string HardwareName { get; }

    public string HardwareType { get; }

    public string Name { get; }

    public string ChannelText { get; }

    public string HardwareText => HardwareType switch
    {
        "SuperIO" => Loc.T("hw.superio"),
        "GpuAmd" or "GpuNvidia" => Loc.T("hw.gpu"),
        "Cpu" => Loc.T("hw.cpu"),
        _ => HardwareName
    };

    public string DetailText => $"{HardwareName} · {HardwareText}";

    [ObservableProperty]
    private int _rpm;

    [ObservableProperty]
    private string _rpmText = "—";

    [ObservableProperty]
    private double? _pwm;

    [ObservableProperty]
    private string _pwmText = "—";

    [ObservableProperty]
    private double _pwmPercent;

    [ObservableProperty]
    private bool _hasPwm;

    [ObservableProperty]
    private string _statusText = Loc.T("fan.stopped");

    [ObservableProperty]
    private Brush _statusBrush = UiBrushes.Muted;

    [ObservableProperty]
    private Brush _rpmBrush = UiBrushes.TextPrimary;

    public void Update(SensorReading reading, double? pwm)
    {
        Rpm = (int)Math.Round(reading.Value);
        RpmText = Rpm.ToString("0");
        Pwm = pwm;
        HasPwm = pwm is not null;
        PwmText = pwm is { } percent ? $"{percent:0} %" : "—";
        PwmPercent = Math.Clamp(pwm ?? 0, 0, 100);

        if (Rpm > 0)
        {
            StatusBrush = UiBrushes.Success;
            RpmBrush = UiBrushes.TextPrimary;
        }
        else
        {
            StatusBrush = UiBrushes.Muted;
            RpmBrush = UiBrushes.Muted;
        }

        StatusText = Loc.T(Rpm > 0 ? "fan.spinning" : "fan.stopped");
    }

    /// <summary>Vuelve a traducir los textos fijos después de cambiar de idioma.</summary>
    public void RefreshLanguage()
    {
        StatusText = Loc.T(Rpm > 0 ? "fan.spinning" : "fan.stopped");
        OnPropertyChanged(nameof(HardwareText));
        OnPropertyChanged(nameof(DetailText));
    }
}
