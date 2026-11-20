using System.Globalization;

namespace CtrlMonitor.Core.Fps;

/// <summary>Lectura de FPS: cuadros mostrados por segundo y si hay frame generation.</summary>
public readonly record struct FpsReading(double Fps, bool FrameGeneration);

/// <summary>
/// Convierte la salida CSV de PresentMon en FPS. Es lógica pura para poder
/// probarla sin lanzar PresentMon: recibe la cabecera y luego cada fila.
/// </summary>
public sealed class FpsCalculator
{
    /// <summary>Ventana sobre la que se cuentan los cuadros.</summary>
    public const double WindowMs = 1000;

    private readonly Dictionary<string, Queue<Frame>> _frames = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private int _swapChain = -1;
    private int _time = -1;
    private int _frameType = -1;
    private int _untilDisplayed = -1;
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

            if (time > _latest)
                _latest = time;

            Trim();
        }
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

            foreach (Queue<Frame> queue in _frames.Values)
            {
                int displayed = 0;
                bool generated = false;

                foreach (Frame frame in queue)
                {
                    if (frame.Displayed)
                        displayed++;

                    generated |= frame.Generated && frame.Displayed;
                }

                if (displayed > bestCount)
                {
                    bestCount = displayed;
                    best = new FpsReading(displayed * 1000 / WindowMs, generated);
                }
            }

            return best;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _frames.Clear();
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
            _columnCount = new[] { _swapChain, _time, _frameType, _untilDisplayed }.Max() + 1;
            _frames.Clear();
            _latest = double.MinValue;
        }
    }

    private void Trim()
    {
        double from = _latest - WindowMs;

        foreach (Queue<Frame> queue in _frames.Values)
        {
            while (queue.Count > 0 && queue.Peek().Time <= from)
                queue.Dequeue();
        }
    }

    private readonly record struct Frame(double Time, bool Displayed, bool Generated);
}
