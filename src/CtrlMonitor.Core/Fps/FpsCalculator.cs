using System.Globalization;

namespace CtrlMonitor.Core.Fps;

/// <summary>
/// Lectura de FPS: cuadros mostrados por segundo, si hay frame generation,
/// el tiempo medio entre cuadros (ms) y el «1 % low» de los últimos segundos.
/// </summary>
public readonly record struct FpsReading(double Fps, bool FrameGeneration, double? FrameTimeMs = null, double? Low1Fps = null, string? Runtime = null);

/// <summary>
/// Convierte la salida CSV de PresentMon en FPS. Es lógica pura para poder
/// probarla sin lanzar PresentMon: recibe la cabecera y luego cada fila.
/// </summary>
public sealed class FpsCalculator
{
    /// <summary>Ventana sobre la que se cuentan los cuadros.</summary>
    public const double WindowMs = 1000;

    /// <summary>
    /// Ventana para el «1 % low»: hace falta más historia que un segundo
    /// para que el 1 % de los cuadros más lentos sea representativo.
    /// </summary>
    public const double LowWindowMs = 10_000;

    private readonly Dictionary<string, Queue<Frame>> _frames = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private int _swapChain = -1;
    private int _time = -1;
    private int _frameType = -1;
    private int _untilDisplayed = -1;
    private int _runtime = -1;
    private int _presentMode = -1;
    private int _syncInterval = -1;
    private string? _lastModeKey;

    /// <summary>
    /// Se dispara cuando cambia cómo presenta Windows los cuadros de la cadena
    /// principal (por ejemplo «Hardware: Independent Flip» → «Composed: Flip»).
    /// Sirve para diagnosticar caídas de FPS con el overlay.
    /// </summary>
    public event Action<string>? PresentModeChanged;
    private readonly Dictionary<string, string> _runtimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _lastShown = new(StringComparer.Ordinal);
    private string? _primaryChain;

    /// <summary>
    /// Tiempo de cada cuadro mostrado de la cadena principal, en ms, apenas
    /// llega. Lo usa la grabación del benchmark. Se llama desde el hilo de PresentMon.
    /// </summary>
    public event Action<double>? FrameDisplayed;
    private int _columnCount;
    private double _latest = double.MinValue;

    /// <summary>True cuando ya llegó una cabecera con las columnas necesarias.</summary>
    public bool HasHeader => _time >= 0;

    /// <summary>Procesa una línea de la salida: la primera con "Application" es la cabecera.</summary>
    public void AddLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        string[] cells = line.Split(',');

        if (cells.Length > 1 && cells[0] == "Application")
        {
            ReadHeader(cells);
            return;
        }

        if (!HasHeader || cells.Length < _columnCount)
            return;

        if (!double.TryParse(cells[_time], NumberStyles.Float, CultureInfo.InvariantCulture, out double time))
            return;

        string swapChain = _swapChain >= 0 ? cells[_swapChain] : string.Empty;
        double? interval = null;
        string? modeChange = null;
        bool displayed = _untilDisplayed < 0 || !string.Equals(cells[_untilDisplayed], "NA", StringComparison.OrdinalIgnoreCase);
        bool generated = _frameType >= 0 && IsGenerated(cells[_frameType]);

        lock (_gate)
        {
            if (!_frames.TryGetValue(swapChain, out Queue<Frame>? queue))
            {
                queue = new Queue<Frame>();
                _frames[swapChain] = queue;
            }

            queue.Enqueue(new Frame(time, displayed, generated));

            if (displayed)
            {
                if (_lastShown.TryGetValue(swapChain, out double last) && time > last
                    && (_primaryChain is null || _primaryChain == swapChain))
                {
                    interval = time - last;
                }

                _lastShown[swapChain] = time;
            }

            if (_runtime >= 0)
                _runtimes[swapChain] = cells[_runtime];

            if (_presentMode >= 0 && (_primaryChain is null || _primaryChain == swapChain))
            {
                string key = cells[_presentMode] + (_syncInterval >= 0 ? " · SyncInterval " + cells[_syncInterval] : string.Empty);

                if (key != _lastModeKey)
                {
                    _lastModeKey = key;
                    modeChange = key;
                }
            }

            if (time > _latest)
                _latest = time;

            Trim();
        }

        if (interval is { } ms)
            FrameDisplayed?.Invoke(ms);

        if (modeChange is not null)
            PresentModeChanged?.Invoke(modeChange);
    }

    /// <summary>
    /// FPS del último segundo, tomando la cadena de presentación con más
    /// cuadros (un juego puede tener otras ventanas o capas que también
    /// presentan). Null si todavía no hay cuadros.
    /// </summary>
    public FpsReading? Current()
    {
        lock (_gate)
        {
            FpsReading? best = null;
            int bestCount = 0;

            foreach ((string chain, Queue<Frame> queue) in _frames)
            {
                int displayed = 0;
                bool generated = false;
                double from = _latest - WindowMs;
                var intervals = new List<double>();
                var recentIntervals = new List<double>();
                double? previous = null;

                foreach (Frame frame in queue)
                {
                    if (!frame.Displayed)
                        continue;

                    if (previous is { } last && frame.Time > last)
                    {
                        double gap = frame.Time - last;
                        intervals.Add(gap);

                        if (frame.Time > from)
                            recentIntervals.Add(gap);
                    }

                    previous = frame.Time;

                    if (frame.Time > from)
                    {
                        displayed++;
                        generated |= frame.Generated;
                    }
                }

                if (displayed > bestCount)
                {
                    bestCount = displayed;
                    _primaryChain = chain;
                    best = new FpsReading(
                        displayed * 1000 / WindowMs,
                        generated,
                        recentIntervals.Count > 0 ? recentIntervals.Average() : null,
                        Low1(intervals),
                        _runtimes.TryGetValue(chain, out string? runtime) ? runtime : null);
                }
            }

            return best;
        }
    }

    /// <summary>Tiempos de los últimos cuadros mostrados de la cadena principal, en ms.</summary>
    public IReadOnlyList<double> RecentFrameTimes(int count)
    {
        lock (_gate)
        {
            if (_primaryChain is null || !_frames.TryGetValue(_primaryChain, out Queue<Frame>? queue))
                return Array.Empty<double>();

            var result = new List<double>(count);
            double? previous = null;

            foreach (Frame frame in queue)
            {
                if (!frame.Displayed)
                    continue;

                if (previous is { } last && frame.Time > last)
                    result.Add(frame.Time - last);

                previous = frame.Time;
            }

            return result.Count > count ? result.GetRange(result.Count - count, count) : result;
        }
    }

    /// <summary>
    /// «1 % low»: los FPS equivalentes al promedio del 1 % de los cuadros más
    /// lentos. Muestra los tirones que el promedio de FPS esconde.
    /// </summary>
    public static double? Low1(IReadOnlyList<double> intervalsMs)
    {
        if (intervalsMs.Count < 2)
            return null;

        int count = Math.Max(1, (int)Math.Ceiling(intervalsMs.Count * 0.01));
        double slowest = intervalsMs.OrderByDescending(v => v).Take(count).Average();

        return slowest > 0 ? 1000 / slowest : null;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _frames.Clear();
            _runtimes.Clear();
            _lastShown.Clear();
            _primaryChain = null;
            _latest = double.MinValue;
        }
    }

    /// <summary>
    /// PresentMon escribe "Application" para cuadros del juego e
    /// "Intel XeSS-FG" o "AMD AFMF" para los generados por el driver o el SDK.
    /// </summary>
    public static bool IsGenerated(string frameType)
    {
        string value = frameType.Trim();

        return value.Length > 0
               && !string.Equals(value, "Application", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(value, "NotSet", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(value, "Repeated", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(value, "NA", StringComparison.OrdinalIgnoreCase);
    }

    private void ReadHeader(string[] cells)
    {
        int Find(params string[] names)
        {
            foreach (string name in names)
            {
                int index = Array.FindIndex(cells, cell => string.Equals(cell.Trim(), name, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                    return index;
            }

            return -1;
        }

        lock (_gate)
        {
            _swapChain = Find("SwapChainAddress");
            _time = Find("TimeInMs", "CPUStartTimeInMs", "CPUStartTime");
            _frameType = Find("FrameType");
            _untilDisplayed = Find("MsUntilDisplayed", "DisplayedTime");
            _runtime = Find("PresentRuntime", "Runtime");
            _presentMode = Find("PresentMode");
            _syncInterval = Find("SyncInterval");
            _lastModeKey = null;
            _columnCount = new[] { _swapChain, _time, _frameType, _untilDisplayed, _runtime, _presentMode, _syncInterval }.Max() + 1;
            _runtimes.Clear();
            _frames.Clear();
            _latest = double.MinValue;
        }
    }

    private void Trim()
    {
        double from = _latest - LowWindowMs;

        foreach (Queue<Frame> queue in _frames.Values)
        {
            while (queue.Count > 0 && queue.Peek().Time <= from)
                queue.Dequeue();
        }
    }

    private readonly record struct Frame(double Time, bool Displayed, bool Generated);
}
