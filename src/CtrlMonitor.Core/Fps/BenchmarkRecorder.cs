using System.Globalization;
using System.Text;

namespace CtrlMonitor.Core.Fps;

/// <summary>Una muestra por segundo durante la grabación.</summary>
public readonly record struct BenchmarkSample(
    double Seconds, double? Fps, double? CpuLoad, double? GpuLoad, double? CpuTemperature, double? GpuTemperature);

/// <summary>Resultado de una sesión de benchmark.</summary>
public sealed record BenchmarkResult(
    TimeSpan Duration,
    int Frames,
    double? AverageFps,
    double? MinFps,
    double? MaxFps,
    double? Low1Fps,
    double? Low01Fps,
    string? GraphicsApi);

/// <summary>
/// Graba una sesión de benchmark, como el de Afterburner: el tiempo de cada
/// cuadro mostrado y una muestra de los sensores por segundo. Al terminar
/// calcula promedio, mínimo, máximo, 1 % low y 0,1 % low.
/// </summary>
public sealed class BenchmarkRecorder
{
    private readonly object _gate = new();
    private readonly List<double> _frames = new();
    private readonly List<BenchmarkSample> _samples = new();
    private DateTime _startUtc;

    public bool IsRecording { get; private set; }

    public TimeSpan Elapsed(DateTime nowUtc) => IsRecording ? nowUtc - _startUtc : TimeSpan.Zero;

    public void Start(DateTime nowUtc)
    {
        lock (_gate)
        {
            _frames.Clear();
            _samples.Clear();
            _startUtc = nowUtc;
            IsRecording = true;
        }
    }

    /// <summary>Tiempo de un cuadro mostrado, en ms. Lo llama el hilo de PresentMon.</summary>
    public void AddFrame(double intervalMs)
    {
        if (!IsRecording || intervalMs <= 0 || intervalMs > 5000)
            return;

        lock (_gate)
        {
            if (IsRecording)
                _frames.Add(intervalMs);
        }
    }

    public void AddSample(DateTime nowUtc, double? fps, double? cpuLoad, double? gpuLoad, double? cpuTemperature, double? gpuTemperature)
    {
        lock (_gate)
        {
            if (IsRecording)
                _samples.Add(new BenchmarkSample((nowUtc - _startUtc).TotalSeconds, fps, cpuLoad, gpuLoad, cpuTemperature, gpuTemperature));
        }
    }

    public (BenchmarkResult Result, IReadOnlyList<BenchmarkSample> Samples) Stop(DateTime nowUtc, string? graphicsApi)
    {
        lock (_gate)
        {
            IsRecording = false;
            var frames = _frames.ToArray();
            var samples = _samples.ToArray();
            return (Calculate(nowUtc - _startUtc, frames, samples, graphicsApi), samples);
        }
    }

    public static BenchmarkResult Calculate(TimeSpan duration, IReadOnlyList<double> framesMs, IReadOnlyList<BenchmarkSample> samples, string? graphicsApi)
    {
        double total = framesMs.Sum();
        double[] fps = samples.Where(s => s.Fps is > 0).Select(s => s.Fps!.Value).ToArray();

        return new BenchmarkResult(
            duration,
            framesMs.Count,
            total > 0 ? framesMs.Count * 1000 / total : null,
            fps.Length > 0 ? fps.Min() : null,
            fps.Length > 0 ? fps.Max() : null,
            LowFps(framesMs, 0.01),
            LowFps(framesMs, 0.001),
            graphicsApi);
    }

    /// <summary>FPS equivalentes al promedio de la fracción de cuadros más lentos.</summary>
    public static double? LowFps(IReadOnlyList<double> framesMs, double fraction)
    {
        if (framesMs.Count < 2)
            return null;

        int count = Math.Max(1, (int)Math.Ceiling(framesMs.Count * fraction));
        double slowest = framesMs.OrderByDescending(v => v).Take(count).Average();
        return slowest > 0 ? 1000 / slowest : null;
    }

    /// <summary>CSV con una fila por segundo y el resumen al final, con punto decimal.</summary>
    public static string ToCsv(BenchmarkResult result, IReadOnlyList<BenchmarkSample> samples)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        string F(double? v, string format = "0.0") => v is { } x ? x.ToString(format, c) : string.Empty;

        var csv = new StringBuilder();
        csv.AppendLine("Seconds,FPS,CPU load %,GPU load %,CPU °C,GPU °C");

        foreach (BenchmarkSample s in samples)
            csv.AppendLine(string.Join(',', F(s.Seconds), F(s.Fps), F(s.CpuLoad, "0"), F(s.GpuLoad, "0"), F(s.CpuTemperature), F(s.GpuTemperature)));

        csv.AppendLine();
        csv.AppendLine("Summary,Value");
        csv.AppendLine($"API,{result.GraphicsApi ?? string.Empty}");
        csv.AppendLine($"Duration (s),{result.Duration.TotalSeconds.ToString("0.0", c)}");
        csv.AppendLine($"Frames,{result.Frames}");
        csv.AppendLine($"Average FPS,{F(result.AverageFps)}");
        csv.AppendLine($"Min FPS,{F(result.MinFps)}");
        csv.AppendLine($"Max FPS,{F(result.MaxFps)}");
        csv.AppendLine($"1% low FPS,{F(result.Low1Fps)}");
        csv.AppendLine($"0.1% low FPS,{F(result.Low01Fps)}");
        return csv.ToString();
    }
}
