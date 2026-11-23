using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CtrlMonitor.Core.Configuration;
using CtrlMonitor.Core.Diagnostics;
using CtrlMonitor.Core.Fps;
using CtrlMonitor.Core.Localization;
using CtrlMonitor.Core.Overlay;

namespace CtrlMonitor.App.ViewModels;

/// <summary>
/// Modo Benchmark del overlay: un panel vertical con gráficos (frametime,
/// FPS y carga de CPU y GPU) y la grabación de sesiones con resultados.
/// </summary>
public partial class MainViewModel
{
    private const int HistorySize = 90;
    private const int FrameTimeSamples = 240;

    private readonly BenchmarkRecorder _recorder = new();
    private readonly List<double> _fpsHistory = new();
    private readonly List<double> _cpuLoadHistory = new();
    private readonly List<double> _gpuLoadHistory = new();
    private bool _benchmarkHooked;

    /// <summary>Se dispara cuando cambia algún atajo, para volver a registrarlos.</summary>
    public event EventHandler? HotkeysChanged;

    // ===================== Modo =====================

    public bool IsBenchmarkMode
    {
        get => _config.Overlay.Mode == OverlayConfig.ModeBenchmark;
        set
        {
            string mode = value ? OverlayConfig.ModeBenchmark : OverlayConfig.ModeBar;

            if (_config.Overlay.Mode == mode)
                return;

            _config.Overlay.Mode = mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBarMode));
            SaveSoon();
        }
    }

    public bool IsBarMode
    {
        get => !IsBenchmarkMode;
        set => IsBenchmarkMode = !value;
    }

    public void ToggleOverlayMode() => IsBenchmarkMode = !IsBenchmarkMode;

    // ===================== Atajos =====================

    public string OverlayHotkeyText => _config.Overlay.HotkeyToggle;

    public string OverlayModeHotkeyText => _config.Overlay.HotkeyMode;

    public string OverlayRecordHotkeyText => _config.Overlay.HotkeyRecord;

    [ObservableProperty]
    private string _hotkeyError = string.Empty;

    /// <summary>
    /// Cambia un atajo («toggle», «mode» o «record»). Devuelve false si el
    /// atajo no es válido o ya lo usa otra acción.
    /// </summary>
    public bool TrySetHotkey(string action, string text)
    {
        if (!Hotkey.TryParse(text, out Hotkey hotkey))
        {
            HotkeyError = Loc.T("overlay.hotkeyInvalid");
            return false;
        }

        string value = hotkey.ToString();
        OverlayConfig overlay = _config.Overlay;

        var others = action switch
        {
            "toggle" => new[] { overlay.HotkeyMode, overlay.HotkeyRecord },
            "mode" => new[] { overlay.HotkeyToggle, overlay.HotkeyRecord },
            _ => new[] { overlay.HotkeyToggle, overlay.HotkeyMode }
        };

        if (others.Any(other => string.Equals(other, value, StringComparison.OrdinalIgnoreCase)))
        {
            HotkeyError = Loc.T("overlay.hotkeyRepeated");
            return false;
        }

        switch (action)
        {
            case "toggle": overlay.HotkeyToggle = value; break;
            case "mode": overlay.HotkeyMode = value; break;
            default: overlay.HotkeyRecord = value; break;
        }

        HotkeyError = string.Empty;
        OnPropertyChanged(nameof(OverlayHotkeyText));
        OnPropertyChanged(nameof(OverlayModeHotkeyText));
        OnPropertyChanged(nameof(OverlayRecordHotkeyText));
        SaveSoon();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>El atajo no se pudo registrar en Windows (otra app ya lo usa).</summary>
    public void ReportHotkeyConflict(string hotkey) =>
        HotkeyError = Loc.F("overlay.hotkeyTaken", hotkey);

    // ===================== Datos del panel =====================

    [ObservableProperty] private string _benchApi = string.Empty;
    [ObservableProperty] private string _benchFpsText = "--";
    [ObservableProperty] private string _benchFpsLabel = "FPS";
    [ObservableProperty] private string _benchLowText = string.Empty;
    [ObservableProperty] private string _benchFrameTimeText = string.Empty;
    [ObservableProperty] private string _benchGpuText = string.Empty;
    [ObservableProperty] private string _benchCpuText = string.Empty;
    [ObservableProperty] private string _benchRamText = string.Empty;
    [ObservableProperty] private string _benchVramText = string.Empty;
    [ObservableProperty] private IReadOnlyList<double> _benchFrameTimes = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> _benchFpsHistory = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> _benchCpuHistory = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> _benchGpuHistory = Array.Empty<double>();
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _recordingText = string.Empty;
    [ObservableProperty] private bool _hasBenchResult;
    [ObservableProperty] private string _benchResultText = string.Empty;
    [ObservableProperty] private string _benchResultFile = string.Empty;

    /// <summary>Actualiza el panel; se llama con cada lectura de sensores.</summary>
    private void UpdateBenchmark(FpsReading? fps, string? api)
    {
        if (!_benchmarkHooked)
        {
            _fps.FrameDisplayed += _recorder.AddFrame;
            _benchmarkHooked = true;
        }

        CultureInfo c = CultureInfo.CurrentCulture;
        string F(double? v, string format) => v is { } x ? x.ToString(format, c) : "—";

        BenchApi = api ?? string.Empty;
        BenchFpsLabel = fps?.FrameGeneration == true ? "FG" : "FPS";
        BenchFpsText = fps is { } f ? f.Fps.ToString("0", c) : "--";
        BenchLowText = fps?.Low1Fps is { } low ? "1% " + low.ToString("0", c) : string.Empty;
        BenchFrameTimeText = fps?.FrameTimeMs is { } ms ? ms.ToString("0.0", c) + " ms" : string.Empty;
        BenchGpuText = $"{F(GpuLoad, "0")} %   {F(GpuTemperature, "0")} °C   {F(GpuPower, "0")} W";
        BenchCpuText = $"{F(CpuLoad, "0")} %   {F(CpuTemperature, "0")} °C   {F(CpuClockGhz, "0.0")} GHz";
        BenchRamText = $"{F(RamUsedGb, "0.0")} / {F(RamTotalGb, "0.0")} GB";
        BenchVramText = $"{F(VramUsedGb, "0.0")} / {F(VramTotalGb, "0.0")} GB";

        Push(_fpsHistory, fps?.Fps ?? 0);
        Push(_cpuLoadHistory, CpuLoad ?? 0);
        Push(_gpuLoadHistory, GpuLoad ?? 0);
        BenchFpsHistory = _fpsHistory.ToArray();
        BenchCpuHistory = _cpuLoadHistory.ToArray();
        BenchGpuHistory = _gpuLoadHistory.ToArray();
        BenchFrameTimes = _fps.RecentFrameTimes(FrameTimeSamples);

        if (_recorder.IsRecording)
        {
            DateTime now = DateTime.UtcNow;
            _recorder.AddSample(now, fps?.Fps, CpuLoad, GpuLoad, CpuTemperature, GpuTemperature);
            TimeSpan elapsed = _recorder.Elapsed(now);
            RecordingText = $"● REC {(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        }
    }

    private static void Push(List<double> history, double value)
    {
        history.Add(value);

        if (history.Count > HistorySize)
            history.RemoveAt(0);
    }

    // ===================== Grabación =====================

    /// <summary>Inicia o detiene la grabación (atajo o botón).</summary>
    public void ToggleBenchmarkRecording()
    {
        if (!_benchmarkHooked)
        {
            _fps.FrameDisplayed += _recorder.AddFrame;
            _benchmarkHooked = true;
        }

        DateTime now = DateTime.UtcNow;

        if (!_recorder.IsRecording)
        {
            _recorder.Start(now);
            IsRecording = true;
            RecordingText = "● REC 0:00";
            AppLog.Info("Benchmark: grabación iniciada.");
            return;
        }

        (BenchmarkResult result, IReadOnlyList<BenchmarkSample> samples) = _recorder.Stop(now, BenchApi.Length > 0 ? BenchApi : null);
        IsRecording = false;
        RecordingText = string.Empty;
        ShowBenchmarkResult(result);
        SaveBenchmark(result, samples);
    }

    private void ShowBenchmarkResult(BenchmarkResult result)
    {
        CultureInfo c = CultureInfo.CurrentCulture;
        string F(double? v) => v is { } x ? x.ToString("0", c) : "—";
        string duration = $"{(int)result.Duration.TotalMinutes}:{result.Duration.Seconds:00}";

        BenchResultText = Loc.F("benchmark.result", duration, F(result.AverageFps), F(result.Low1Fps), F(result.Low01Fps))
                          + " · " + Loc.F("benchmark.minMax", F(result.MinFps), F(result.MaxFps));
        HasBenchResult = true;
        AppLog.Info("Benchmark: " + BenchResultText);
    }

    private void SaveBenchmark(BenchmarkResult result, IReadOnlyList<BenchmarkSample> samples)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ctrl Monitor", "Benchmarks");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"benchmark-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            File.WriteAllText(file, BenchmarkRecorder.ToCsv(result, samples));
            BenchResultFile = Loc.F("benchmark.saved", Path.GetFileName(file));
            AppLog.Info("Benchmark guardado en " + file);
        }
        catch (Exception ex)
        {
            BenchResultFile = string.Empty;
            AppLog.Warn("Benchmark: no se pudo guardar el CSV: " + ex.Message);
        }
    }
}
