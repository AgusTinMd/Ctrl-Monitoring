using CtrlMonitor.Core.Configuration;
using CtrlMonitor.Core.Fps;
using CtrlMonitor.Core.Overlay;

namespace CtrlMonitor.Core.Tests;

public class BenchmarkTests
{
    [Theory]
    [InlineData("Ctrl+Alt+O", "Ctrl+Alt+O", 0x4F)]
    [InlineData("alt+ctrl+b", "Ctrl+Alt+B", 0x42)]
    [InlineData("Ctrl+Shift+F12", "Ctrl+Shift+F12", 0x7B)]
    [InlineData("Win+Alt+5", "Alt+Win+5", 0x35)]
    public void Hotkey_ParsesAndNormalizes(string text, string expected, int virtualKey)
    {
        Assert.True(Hotkey.TryParse(text, out Hotkey hotkey));
        Assert.Equal(expected, hotkey.ToString());
        Assert.Equal(virtualKey, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData("O")]
    [InlineData("Shift+O")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+O+P")]
    [InlineData("Ctrl+Space")]
    [InlineData("")]
    public void Hotkey_RejectsUnsafeOrUnknownCombos(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void OverlayConfig_DefaultsToBarAndDefaultHotkeys()
    {
        var config = new OverlayConfig();

        Assert.Equal(OverlayConfig.ModeBar, config.Mode);
        Assert.Equal(Hotkey.DefaultToggle, config.HotkeyToggle);
        Assert.Equal(Hotkey.DefaultMode, config.HotkeyMode);
        Assert.Equal(Hotkey.DefaultRecord, config.HotkeyRecord);
    }

    [Fact]
    public void OverlayConfig_Normalize_FixesInvalidAndRepeatedHotkeys()
    {
        var config = new OverlayConfig
        {
            Mode = "otro",
            HotkeyToggle = "O",
            HotkeyMode = "ctrl+alt+o",
            HotkeyRecord = "Ctrl+Alt+O"
        };

        config.Normalize();

        Assert.Equal(OverlayConfig.ModeBar, config.Mode);
        Assert.Equal(Hotkey.DefaultToggle, config.HotkeyToggle);
        Assert.Equal(Hotkey.DefaultMode, config.HotkeyMode);
        Assert.Equal(Hotkey.DefaultRecord, config.HotkeyRecord);
    }

    [Fact]
    public void Result_UsesFramesForAverageAndLows()
    {
        // 990 cuadros de 10 ms y 10 de 40 ms.
        var frames = Enumerable.Repeat(10.0, 990).Concat(Enumerable.Repeat(40.0, 10)).ToList();
        var samples = new[]
        {
            new BenchmarkSample(1, 90, 40, 95, 60, 70),
            new BenchmarkSample(2, 110, 42, 96, 61, 71)
        };

        BenchmarkResult result = BenchmarkRecorder.Calculate(TimeSpan.FromSeconds(10.3), frames, samples, "DX12");

        Assert.Equal(1000, result.Frames);
        Assert.Equal(1000 * 1000 / 10300.0, result.AverageFps!.Value, 3);
        Assert.Equal(25, result.Low1Fps!.Value, 3);
        Assert.Equal(25, result.Low01Fps!.Value, 3);
        Assert.Equal(90, result.MinFps);
        Assert.Equal(110, result.MaxFps);
    }

    [Fact]
    public void Recorder_IgnoresFramesWhenNotRecording()
    {
        var recorder = new BenchmarkRecorder();
        recorder.AddFrame(16);

        DateTime start = DateTime.UtcNow;
        recorder.Start(start);
        recorder.AddFrame(16);
        recorder.AddFrame(17);
        (BenchmarkResult result, _) = recorder.Stop(start.AddSeconds(1), null);

        Assert.Equal(2, result.Frames);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void Csv_HasSamplesAndSummary()
    {
        var result = new BenchmarkResult(TimeSpan.FromSeconds(2), 120, 60, 58, 62, 50, 45, "VK");
        string csv = BenchmarkRecorder.ToCsv(result, new[] { new BenchmarkSample(1, 60.25, 30, 90, 55.5, 66) });

        Assert.StartsWith("Seconds,FPS,", csv);
        Assert.Contains("1.0,60.3,30,90,55.5,66.0", csv);
        Assert.Contains("API,VK", csv);
        Assert.Contains("0.1% low FPS,45.0", csv);
    }
}
