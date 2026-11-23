using System.Globalization;

namespace CtrlMonitor.Core.Overlay;

/// <summary>Texto de un grupo de la barra, por ejemplo <c>CPU 12% 52°C</c>.</summary>
public readonly record struct OverlaySegment(string GroupKey, string Text);

/// <summary>
/// Arma el texto de la barra a partir de los valores y de las métricas
/// elegidas. Es lógica pura para poder probarla sin WPF.
/// </summary>
public static class OverlayTextFormatter
{
    public static IReadOnlyList<OverlaySegment> Format(
        OverlaySnapshot snapshot,
        IReadOnlyCollection<string> selectedMetrics,
        CultureInfo? culture = null)
    {
        var selected = new HashSet<string>(selectedMetrics ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        CultureInfo text = culture ?? CultureInfo.CurrentCulture;
        var result = new List<OverlaySegment>(5);

        // FPS primero, como en las barras de rendimiento habituales. Con frame
        // generation se rotula "FG" para no confundirlo con cuadros reales.
        if (selected.Any(OverlayMetricKeys.IsFps))
        {
            var parts = new List<string>(3);

            if (selected.Contains(OverlayMetricKeys.Fps))
            {
                string label = snapshot.FrameGeneration ? "FG" : "FPS";
                parts.Add(label + " " + (snapshot.Fps is { } fps ? fps.ToString("0", text) : "--"));
            }

            if (selected.Contains(OverlayMetricKeys.FrameTime))
                parts.Add((snapshot.FrameTimeMs is { } ms ? ms.ToString("0.0", text) : "--") + " ms");

            if (selected.Contains(OverlayMetricKeys.FpsLow1))
                parts.Add("1% " + (snapshot.FpsLow1 is { } low ? low.ToString("0", text) : "--"));

            result.Add(new OverlaySegment(OverlayMetricKeys.FpsGroup, string.Join(" ", parts)));
        }

        if (selected.Any(OverlayMetricKeys.IsCpu))
            result.Add(new OverlaySegment(OverlayMetricKeys.CpuGroup, FormatCpu(snapshot, selected, text)));

        if (selected.Any(OverlayMetricKeys.IsGpu))
            result.Add(new OverlaySegment(OverlayMetricKeys.GpuGroup, FormatGpu(snapshot, selected, text)));

        if (selected.Any(OverlayMetricKeys.IsRam))
            result.Add(new OverlaySegment(OverlayMetricKeys.RamGroup, FormatRam(snapshot, selected, text)));

        if (selected.Any(OverlayMetricKeys.IsNet))
            result.Add(new OverlaySegment(OverlayMetricKeys.NetGroup, FormatNet(snapshot, selected, text)));

        return result;
    }

    private static string FormatCpu(OverlaySnapshot s, HashSet<string> selected, CultureInfo c)
    {
        var parts = new List<string>(5);

        if (selected.Contains(OverlayMetricKeys.CpuLoad) && s.CpuLoad is { } load)
            parts.Add(load.ToString("0", c) + "%");

        if (selected.Contains(OverlayMetricKeys.CpuTemperature) && s.CpuTemperature is { } temperature)
            parts.Add(temperature.ToString("0", c) + "°C");

        if (selected.Contains(OverlayMetricKeys.CpuClock) && s.CpuClockGhz is { } clock)
            parts.Add(clock.ToString("0.00", c) + "GHz");

        if (selected.Contains(OverlayMetricKeys.CpuPower) && s.CpuPower is { } power)
            parts.Add(power.ToString("0", c) + "W");

        if (selected.Contains(OverlayMetricKeys.CpuFan) && s.CpuFanRpm is { } fan)
            parts.Add(fan.ToString(c) + "RPM");

        return "CPU " + Join(parts);
    }

    private static string FormatGpu(OverlaySnapshot s, HashSet<string> selected, CultureInfo c)
    {
        var parts = new List<string>(6);

        if (selected.Contains(OverlayMetricKeys.GpuLoad) && s.GpuLoad is { } load)
            parts.Add(load.ToString("0", c) + "%");

        bool wantTemperature = selected.Contains(OverlayMetricKeys.GpuTemperature);
        bool wantHotspot = selected.Contains(OverlayMetricKeys.GpuHotspot);

        if (wantTemperature && s.GpuTemperature is { } temperature)
        {
            parts.Add(wantHotspot && s.GpuHotspot is { } hotspot
                ? temperature.ToString("0", c) + "/" + hotspot.ToString("0", c) + "°C"
                : temperature.ToString("0", c) + "°C");
        }
        else if (wantHotspot && s.GpuHotspot is { } onlyHotspot)
        {
            parts.Add(onlyHotspot.ToString("0", c) + "°C");
        }

        if (selected.Contains(OverlayMetricKeys.GpuVram))
        {
            if (s.VramUsedGb is { } used && s.VramTotalGb is { } total)
                parts.Add(used.ToString("0.0", c) + "/" + FormatGb(total, c));
            else if (s.VramUsedGb is { } onlyUsed)
                parts.Add(FormatGb(onlyUsed, c));
            else if (s.VramTotalGb is { } onlyTotal)
                parts.Add(FormatGb(onlyTotal, c));
        }

        if (selected.Contains(OverlayMetricKeys.GpuPower) && s.GpuPower is { } power)
            parts.Add(power.ToString("0", c) + "W");

        if (selected.Contains(OverlayMetricKeys.GpuFan) && s.GpuFanRpm is { } fan)
            parts.Add(fan.ToString(c) + "RPM");

        return "GPU " + Join(parts);
    }

    private static string FormatRam(OverlaySnapshot s, HashSet<string> selected, CultureInfo c)
    {
        var parts = new List<string>(2);

        if (selected.Contains(OverlayMetricKeys.RamUsed))
        {
            if (s.RamUsedGb is { } used && s.RamTotalGb is { } total)
                parts.Add(used.ToString("0.0", c) + "/" + FormatGb(total, c));
            else if (s.RamUsedGb is { } onlyUsed)
                parts.Add(FormatGb(onlyUsed, c));
            else if (s.RamTotalGb is { } onlyTotal)
                parts.Add(FormatGb(onlyTotal, c));
        }

        if (selected.Contains(OverlayMetricKeys.RamPercent) && s.RamPercent is { } percent)
            parts.Add(percent.ToString("0", c) + "%");

        return "RAM " + Join(parts);
    }

    private static string FormatNet(OverlaySnapshot s, HashSet<string> selected, CultureInfo c)
    {
        var parts = new List<string>(1);

        if (selected.Contains(OverlayMetricKeys.NetThroughput))
        {
            if (s.NetDownMbs is { } down && s.NetUpMbs is { } up)
                parts.Add("↓" + down.ToString("0.0", c) + " ↑" + up.ToString("0.0", c) + "MB/s");
            else if (s.NetDownMbs is { } onlyDown)
                parts.Add("↓" + onlyDown.ToString("0.0", c) + "MB/s");
            else if (s.NetUpMbs is { } onlyUp)
                parts.Add("↑" + onlyUp.ToString("0.0", c) + "MB/s");
        }

        return "NET " + Join(parts);
    }

    private static string Join(List<string> parts) =>
        parts.Count == 0 ? "--" : string.Join(" ", parts);

    private static string FormatGb(double value, CultureInfo culture) =>
        value.ToString("0.0", culture) + "GB";
}
