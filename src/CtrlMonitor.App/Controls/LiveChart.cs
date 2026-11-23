using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CtrlMonitor.App.Models;
using CtrlMonitor.App.ViewModels;
using CtrlMonitor.Core.Inventory;
using CtrlMonitor.Core.Charting;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.Controls;

/// <summary>
/// Gráfico de líneas en vivo al estilo de MSI Afterburner. Puede dibujar una
/// serie independiente con escala automática o varias superpuestas sobre una
/// ventana temporal. Se redibuja cuando cambia la colección de muestras.
/// </summary>
public sealed class LiveChart : FrameworkElement
{
    private const double MinPlotLeft = 28;
    private const double PlotTop = 14;
    private const double PlotRight = 14;
    private const double PlotBottom = 28;

    private INotifyCollectionChanged? _observable;

    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples),
        typeof(IEnumerable),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSamplesChanged));

    public static readonly DependencyProperty WindowMinutesProperty = DependencyProperty.Register(
        nameof(WindowMinutes),
        typeof(double),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(5d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(string),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AutoScaleProperty = DependencyProperty.Register(
        nameof(AutoScale),
        typeof(bool),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowCpuProperty = DependencyProperty.Register(
        nameof(ShowCpu),
        typeof(bool),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGpuProperty = DependencyProperty.Register(
        nameof(ShowGpu),
        typeof(bool),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowBoardProperty = DependencyProperty.Register(
        nameof(ShowBoard),
        typeof(bool),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowHotspotProperty = DependencyProperty.Register(
        nameof(ShowHotspot),
        typeof(bool),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisMinProperty = DependencyProperty.Register(
        nameof(AxisMin),
        typeof(double),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisMaxProperty = DependencyProperty.Register(
        nameof(AxisMax),
        typeof(double),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisStepProperty = DependencyProperty.Register(
        nameof(AxisStep),
        typeof(double),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit),
        typeof(string),
        typeof(LiveChart),
        new FrameworkPropertyMetadata("°", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact),
        typeof(bool),
        typeof(LiveChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Mini gráfico para la vista de lista: ocupa todo el control, sin grilla,
    /// ejes ni textos.
    /// </summary>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public IEnumerable? Samples
    {
        get => (IEnumerable?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public double WindowMinutes
    {
        get => (double)GetValue(WindowMinutesProperty);
        set => SetValue(WindowMinutesProperty, value);
    }

    /// <summary>
    /// Serie única a dibujar: «Cpu», «Gpu», «Board», «Hotspot», «Ram», «Vram»
    /// o «Network» (bajada y subida). Vacío dibuja todas las series superpuestas.
    /// </summary>
    public string Series
    {
        get => (string)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>Ajusta el eje vertical al rango de la serie en la ventana visible.</summary>
    public bool AutoScale
    {
        get => (bool)GetValue(AutoScaleProperty);
        set => SetValue(AutoScaleProperty, value);
    }

    public bool ShowCpu
    {
        get => (bool)GetValue(ShowCpuProperty);
        set => SetValue(ShowCpuProperty, value);
    }

    public bool ShowGpu
    {
        get => (bool)GetValue(ShowGpuProperty);
        set => SetValue(ShowGpuProperty, value);
    }

    public bool ShowBoard
    {
        get => (bool)GetValue(ShowBoardProperty);
        set => SetValue(ShowBoardProperty, value);
    }

    public bool ShowHotspot
    {
        get => (bool)GetValue(ShowHotspotProperty);
        set => SetValue(ShowHotspotProperty, value);
    }

    /// <summary>Valor mínimo del eje cuando no hay datos o al recortar la escala.</summary>
    public double AxisMin
    {
        get => (double)GetValue(AxisMinProperty);
        set => SetValue(AxisMinProperty, value);
    }

    /// <summary>Valor máximo del eje cuando no hay datos o al recortar la escala.</summary>
    public double AxisMax
    {
        get => (double)GetValue(AxisMaxProperty);
        set => SetValue(AxisMaxProperty, value);
    }

    /// <summary>Separación entre líneas del eje vertical (temperatura, vatios o %).</summary>
    public double AxisStep
    {
        get => (double)GetValue(AxisStepProperty);
        set => SetValue(AxisStepProperty, value);
    }

    /// <summary>Sufijo de las etiquetas del eje: «°», «%» o «W».</summary>
    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double width = ActualWidth;
        double height = ActualHeight;

        if (IsThreadMap)
        {
            RenderThreadMap(dc, width, height);
            return;
        }

        if (Compact)
        {
            RenderCompact(dc, width, height);
            return;
        }

        if (width < 120 || height < 100)
            return;

        Brush gridBrush = Resource("BorderBrush", Brushes.DimGray);
        Brush textBrush = Resource("TextMutedBrush", Brushes.Gray);
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double plotHeight = Math.Max(20, height - PlotTop - PlotBottom);

        double windowMinutes = Math.Clamp(WindowMinutes, 0.5, 60);
        var samples = Samples?.OfType<ChartSample>().ToList() ?? new List<ChartSample>();
        DateTime end = samples.Count > 0 ? samples[^1].TimestampUtc : DateTime.UtcNow;
        DateTime start = end - TimeSpan.FromMinutes(windowMinutes);

        List<ChartSeries> series = GetActiveSeries();

        double step = Math.Max(1, AxisStep);
        double min = Math.Min(AxisMin, AxisMax - step);
        double max = Math.Max(AxisMax, min + step);

        // Las áreas en % no usan el eje: no entran en la escala automática.
        var lineSeries = series.Where(item => !item.PercentArea).ToList();

        if (AutoScale)
            (min, max) = GetAutoScaleRange(samples, lineSeries, start);

        // El paso base sirve para rangos chicos; con rangos grandes (red a
        // 100 MB/s con paso 1) se agranda para que las etiquetas no se encimen.
        AxisTicks ticks = AxisTicks.Compute(min, max, step, plotHeight);

        if (AutoScale && ticks.GridStep > step)
        {
            double ceiling = Math.Max(AxisMax, AxisMin + step);
            min = Math.Floor(min / ticks.GridStep) * ticks.GridStep;
            max = Math.Min(ceiling, Math.Ceiling(max / ticks.GridStep) * ticks.GridStep);
            min = Math.Max(Math.Min(AxisMin, AxisMax - step), min);
            ticks = AxisTicks.Compute(min, max, step, plotHeight);
        }

        // El margen izquierdo se ajusta a la etiqueta más ancha que se va a
        // dibujar («40°» o «1250 MB/s»), sin reservar lugar de más.
        string unit = Unit ?? string.Empty;
        double axisLabelWidth = Math.Max(
            Text($"{min:0}{unit}", typeface, 10.5, textBrush, dpi).Width,
            Text($"{max:0}{unit}", typeface, 10.5, textBrush, dpi).Width);
        double plotLeft = Math.Max(MinPlotLeft, axisLabelWidth + 12);

        var plot = new Rect(
            plotLeft,
            PlotTop,
            Math.Max(20, width - plotLeft - PlotRight),
            plotHeight);

        DrawGrid(dc, plot, gridBrush, textBrush, typeface, dpi, min, max, windowMinutes, ticks, Unit ?? string.Empty);

        bool hasData = samples.Any(sample =>
            sample.TimestampUtc >= start
            && series.Any(item => item.Selector(sample) is not null));

        if (!hasData)
        {
            FormattedText waiting = Text(
                Loc.T(samples.Count == 0 ? "chart.waiting" : "chart.noData"),
                typeface,
                12,
                textBrush,
                dpi);

            dc.DrawText(waiting, new Point(
                plot.Left + (plot.Width - waiting.Width) / 2,
                plot.Top + (plot.Height - waiting.Height) / 2));
            return;
        }

        dc.PushClip(new RectangleGeometry(plot));

        foreach (ChartSeries item in series.Where(item => item.PercentArea))
            DrawPercentArea(dc, samples, item.Selector, item.Brush, plot, start, end);

        foreach (ChartSeries item in lineSeries)
            DrawSeries(dc, samples, item.Selector, item.Brush, plot, start, end, min, max);

        dc.Pop();
    }

    private void RenderCompact(DrawingContext dc, double width, double height)
    {
        if (width < 8 || height < 8)
            return;

        var plot = new Rect(1, 2, width - 2, height - 4);
        double windowMinutes = Math.Clamp(WindowMinutes, 0.5, 60);
        var samples = Samples?.OfType<ChartSample>().ToList() ?? new List<ChartSample>();

        if (samples.Count == 0)
            return;

        DateTime end = samples[^1].TimestampUtc;
        DateTime start = end - TimeSpan.FromMinutes(windowMinutes);
        List<ChartSeries> series = GetActiveSeries();
        var lineSeries = series.Where(item => !item.PercentArea).ToList();

        (double min, double max) = AutoScale
            ? GetAutoScaleRange(samples, lineSeries, start)
            : (Math.Min(AxisMin, AxisMax - 1), Math.Max(AxisMax, AxisMin + 1));

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));

        foreach (ChartSeries item in series.Where(item => item.PercentArea))
            DrawPercentArea(dc, samples, item.Selector, item.Brush, plot, start, end);

        foreach (ChartSeries item in lineSeries)
            DrawSeries(dc, samples, item.Selector, item.Brush, plot, start, end, min, max, compact: true);

        dc.Pop();
    }

    private bool IsThreadMap => string.Equals(Series?.Trim(), "CpuThreads", StringComparison.OrdinalIgnoreCase);

    private readonly Dictionary<int, Brush> _threadCellBrushes = new();

    /// <summary>
    /// Mapa de calor de los hilos: una fila por hilo y el tiempo a lo ancho.
    /// Cuanto más intenso el color, más carga. Escala igual con 4 o con 64 hilos.
    /// </summary>
    private void RenderThreadMap(DrawingContext dc, double width, double height)
    {
        if (width < 20 || height < 20)
            return;

        Brush textBrush = Resource("TextMutedBrush", Brushes.Gray);
        Brush trackBrush = Resource("TrackBrush", Brushes.DimGray);
        Brush baseBrush = Resource("ChartCpuBrush", Brushes.DodgerBlue);
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double windowMinutes = Math.Clamp(WindowMinutes, 0.5, 60);
        var samples = Samples?.OfType<ChartSample>().Where(s => s.Threads is { Count: > 0 }).ToList()
                      ?? new List<ChartSample>();

        int threads = samples.Count > 0 ? samples[^1].Threads!.Count : 0;
        bool labels = !Compact;
        double labelWidth = labels ? 30 : 0;
        var plot = Compact
            ? new Rect(1, 2, width - 2, height - 4)
            : new Rect(labelWidth, PlotTop, Math.Max(20, width - labelWidth - PlotRight), Math.Max(20, height - PlotTop - PlotBottom));

        if (threads == 0)
        {
            if (!Compact)
            {
                FormattedText waiting = Text(Loc.T("chart.waiting"), typeface, 12, textBrush, dpi);
                dc.DrawText(waiting, new Point(plot.Left + (plot.Width - waiting.Width) / 2, plot.Top + (plot.Height - waiting.Height) / 2));
            }

            return;
        }

        DateTime end = samples[^1].TimestampUtc;
        DateTime start = end - TimeSpan.FromMinutes(windowMinutes);
        double rowHeight = plot.Height / threads;
        double gap = rowHeight >= 6 ? 1 : 0;

        dc.DrawRectangle(trackBrush, null, plot);
        dc.PushClip(new RectangleGeometry(plot));

        for (int i = 0; i < samples.Count; i++)
        {
            ChartSample sample = samples[i];
            DateTime next = i + 1 < samples.Count ? samples[i + 1].TimestampUtc : sample.TimestampUtc + TimeSpan.FromSeconds(1);

            if (next < start)
                continue;

            double x0 = plot.Left + (sample.TimestampUtc - start).TotalSeconds / (end - start).TotalSeconds * plot.Width;
            double x1 = plot.Left + (next - start).TotalSeconds / (end - start).TotalSeconds * plot.Width;
            IReadOnlyList<double> loads = sample.Threads!;

            for (int t = 0; t < Math.Min(threads, loads.Count); t++)
            {
                int level = (int)Math.Round(Math.Clamp(loads[t], 0, 100) / 5);

                if (level == 0)
                    continue;

                double y = plot.Top + t * rowHeight;
                dc.DrawRectangle(CellBrush(baseBrush, level), null,
                    new Rect(x0, y, Math.Max(1, x1 - x0 + 0.5), Math.Max(1, rowHeight - gap)));
            }
        }

        dc.Pop();

        if (!labels)
            return;

        // Número de hilo a la izquierda; con muchos hilos, solo algunos para que no se encimen.
        int every = Math.Max(1, (int)Math.Ceiling(12 / Math.Max(1, rowHeight)));

        for (int t = 0; t < threads; t += every)
        {
            FormattedText label = Text(Loc.F("summary.threadShort", t + 1), typeface, 10, textBrush, dpi);
            double y = plot.Top + t * rowHeight + (rowHeight - label.Height) / 2;
            dc.DrawText(label, new Point(plot.Left - label.Width - 6, y));
        }
    }

    private Brush CellBrush(Brush baseBrush, int level)
    {
        if (_threadCellBrushes.TryGetValue(level, out Brush? cached))
            return cached;

        Brush brush = baseBrush.Clone();
        brush.Opacity = 0.12 + 0.88 * level / 20.0;
        brush.Freeze();
        _threadCellBrushes[level] = brush;
        return brush;
    }

    private List<ChartSeries> GetActiveSeries()
    {
        var result = new List<ChartSeries>(5);
        string series = Series?.Trim() ?? string.Empty;

        // Cada disco tiene su serie: «Disk:2 C:». Va antes del switch porque
        // el identificador del disco distingue mayúsculas.
        if (series.StartsWith(ChartMetrics.DiskKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string diskId = series[ChartMetrics.DiskKeyPrefix.Length..];
            result.Add(new ChartSeries(s => Disk(s, diskId)?.ActivePercent, Resource("ChartDiskActiveBrush", Brushes.DarkGreen), PercentArea: true));
            result.Add(new ChartSeries(s => Disk(s, diskId)?.ReadMegaBytesPerSecond, Resource("ChartDiskReadBrush", Brushes.LimeGreen)));
            result.Add(new ChartSeries(s => Disk(s, diskId)?.WriteMegaBytesPerSecond, Resource("ChartDiskWriteBrush", Brushes.Gold)));
            return result;
        }

        if (series.Length > 0)
        {
            switch (series.ToLowerInvariant())
            {
                case "cpu":
                    result.Add(new ChartSeries(s => s.Cpu, Resource("ChartCpuBrush", Brushes.DodgerBlue)));
                    break;
                case "gpu":
                    result.Add(new ChartSeries(s => s.Gpu, Resource("ChartGpuBrush", Brushes.MediumSeaGreen)));
                    break;
                case "cpuload":
                    result.Add(new ChartSeries(s => s.CpuLoad, Resource("ChartCpuBrush", Brushes.DodgerBlue)));
                    break;
                case "gpuload":
                    result.Add(new ChartSeries(s => s.GpuLoad, Resource("ChartGpuBrush", Brushes.MediumSeaGreen)));
                    break;
                case "board":
                    result.Add(new ChartSeries(s => s.Board, Resource("ChartBoardBrush", Brushes.Goldenrod)));
                    break;
                case "hotspot":
                    result.Add(new ChartSeries(s => s.Hotspot, Resource("ChartHotspotBrush", Brushes.IndianRed)));
                    break;
                case "ram":
                    result.Add(new ChartSeries(s => s.RamPercent, Resource("ChartRamBrush", Brushes.MediumPurple)));
                    break;
                case "vram":
                    result.Add(new ChartSeries(s => s.VramPercent, Resource("ChartVramBrush", Brushes.HotPink)));
                    break;
                case "network":
                    result.Add(new ChartSeries(s => s.NetDownMbs, Resource("ChartNetDownBrush", Brushes.DeepSkyBlue)));
                    result.Add(new ChartSeries(s => s.NetUpMbs, Resource("ChartNetUpBrush", Brushes.Orange)));
                    break;
            }

            return result;
        }

        if (ShowCpu)
            result.Add(new ChartSeries(s => s.Cpu, Resource("ChartCpuBrush", Brushes.DodgerBlue)));

        if (ShowGpu)
            result.Add(new ChartSeries(s => s.Gpu, Resource("ChartGpuBrush", Brushes.MediumSeaGreen)));

        if (ShowBoard)
            result.Add(new ChartSeries(s => s.Board, Resource("ChartBoardBrush", Brushes.Goldenrod)));

        if (ShowHotspot)
            result.Add(new ChartSeries(s => s.Hotspot, Resource("ChartHotspotBrush", Brushes.IndianRed)));

        return result;
    }

    private (double Min, double Max) GetAutoScaleRange(
        IReadOnlyList<ChartSample> samples,
        IReadOnlyList<ChartSeries> series,
        DateTime start)
    {
        double step = Math.Max(1, AxisStep);
        double floor = Math.Min(AxisMin, AxisMax - step);
        double ceiling = Math.Max(AxisMax, floor + step);

        double lowest = double.MaxValue;
        double highest = double.MinValue;
        bool any = false;

        foreach (ChartSample sample in samples)
        {
            if (sample.TimestampUtc < start)
                continue;

            foreach (ChartSeries item in series)
            {
                if (item.Selector(sample) is not { } value)
                    continue;

                lowest = Math.Min(lowest, value);
                highest = Math.Max(highest, value);
                any = true;
            }
        }

        if (!any)
            return (floor, ceiling);

        double min = Math.Floor((lowest - step * 0.2) / step) * step;
        double max = Math.Ceiling((highest + step * 0.2) / step) * step;

        // Nunca mostramos menos de dos divisiones: un gráfico demasiado plano
        // exagera el ruido y parece que hay picos que no existen.
        if (max - min < step * 2)
        {
            double middle = (highest + lowest) / 2;
            min = Math.Floor((middle - step) / step) * step;
            max = min + step * 2;
        }

        min = Math.Max(floor, min);
        max = Math.Min(ceiling, max);

        if (max - min < step * 2)
        {
            if (min <= floor)
                max = Math.Min(ceiling, floor + step * 2);
            else
                min = Math.Max(floor, max - step * 2);
        }

        return (min, max);
    }

    private static void DrawGrid(
        DrawingContext dc,
        Rect plot,
        Brush gridBrush,
        Brush textBrush,
        Typeface typeface,
        double dpi,
        double min,
        double max,
        double windowMinutes,
        AxisTicks ticks,
        string unit)
    {
        double step = ticks.GridStep;
        double first = Math.Ceiling(min / step - 1e-9) * step;

        // Índice entero en vez de acumular «value += step», que arrastra error
        // de coma flotante y desalinea las etiquetas.
        for (int i = 0; ; i++)
        {
            double value = first + i * step;

            if (value > max + step * 1e-6)
                break;

            double y = plot.Bottom - (value - min) / (max - min) * plot.Height;
            bool isLabel = ticks.IsLabel(value);
            var pen = new Pen(gridBrush, isLabel ? 0.9 : 0.45);

            if (pen.CanFreeze)
                pen.Freeze();

            dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));

            if (isLabel)
            {
                FormattedText label = Text($"{value:0}{unit}", typeface, 10.5, textBrush, dpi);
                dc.DrawText(label, new Point(plot.Left - label.Width - 8, y - label.Height / 2));
            }
        }

        int divisions = windowMinutes >= 8 ? 5 : windowMinutes >= 3 ? 5 : 4;

        for (int i = 0; i <= divisions; i++)
        {
            double fraction = (double)i / divisions;
            double x = plot.Left + fraction * plot.Width;
            var pen = new Pen(gridBrush, i == 0 || i == divisions ? 0.9 : 0.45);

            if (pen.CanFreeze)
                pen.Freeze();

            dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));

            double minutesAgo = windowMinutes * (1 - fraction);
            string labelText = i == divisions
                ? Loc.T("chart.now")
                : minutesAgo >= 0.95
                    ? $"-{minutesAgo:0.#} min"
                    : $"-{minutesAgo * 60:0} s";

            FormattedText label = Text(labelText, typeface, 10, textBrush, dpi);
            double labelX = i == divisions ? x - label.Width : x - label.Width / 2;

            if (labelX < 2)
                labelX = 2;

            if (labelX + label.Width > plot.Right)
                labelX = plot.Right - label.Width;

            dc.DrawText(label, new Point(labelX, plot.Bottom + 7));
        }

        var axisPen = new Pen(gridBrush, 1);

        if (axisPen.CanFreeze)
            axisPen.Freeze();

        dc.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
    }

    private static void DrawSeries(
        DrawingContext dc,
        IReadOnlyList<ChartSample> samples,
        Func<ChartSample, double?> selector,
        Brush brush,
        Rect plot,
        DateTime start,
        DateTime end,
        double min,
        double max,
        bool compact = false)
    {
        double totalSeconds = Math.Max(1, (end - start).TotalSeconds);
        var geometry = new StreamGeometry();
        bool hasPoint = false;

        using (StreamGeometryContext context = geometry.Open())
        {
            bool figureOpen = false;

            foreach (ChartSample sample in samples)
            {
                if (sample.TimestampUtc < start)
                    continue;

                double? value = selector(sample);

                if (value is null)
                {
                    figureOpen = false;
                    continue;
                }

                Point point = ToPoint(sample.TimestampUtc, value.Value, start, totalSeconds, plot, min, max);

                if (!figureOpen)
                {
                    context.BeginFigure(point, isFilled: false, isClosed: false);
                    figureOpen = true;
                }
                else
                {
                    context.LineTo(point, isStroked: true, isSmoothJoin: true);
                }

                hasPoint = true;
            }
        }

        if (!hasPoint)
            return;

        if (geometry.CanFreeze)
            geometry.Freeze();

        var pen = new Pen(brush, compact ? 1.2 : 1.8)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        if (pen.CanFreeze)
            pen.Freeze();

        dc.DrawGeometry(null, pen, geometry);

        // El punto final ayuda a leer el valor actual; en el mini gráfico molesta.
        if (compact)
            return;

        for (int i = samples.Count - 1; i >= 0; i--)
        {
            if (samples[i].TimestampUtc < start)
                break;

            double? value = selector(samples[i]);

            if (value is null)
                continue;

            Point last = ToPoint(samples[i].TimestampUtc, value.Value, start, totalSeconds, plot, min, max);
            dc.DrawEllipse(brush, null, last, 3, 3);
            break;
        }
    }

    private static void DrawPercentArea(
        DrawingContext dc,
        IReadOnlyList<ChartSample> samples,
        Func<ChartSample, double?> selector,
        Brush brush,
        Rect plot,
        DateTime start,
        DateTime end)
    {
        double totalSeconds = Math.Max(1, (end - start).TotalSeconds);
        var geometry = new StreamGeometry();
        bool hasPoint = false;

        using (StreamGeometryContext context = geometry.Open())
        {
            Point? first = null;
            Point last = default;

            foreach (ChartSample sample in samples)
            {
                if (sample.TimestampUtc < start || selector(sample) is not { } value)
                    continue;

                Point point = ToPoint(sample.TimestampUtc, value, start, totalSeconds, plot, 0, 100);

                if (first is null)
                {
                    first = point;
                    context.BeginFigure(new Point(point.X, plot.Bottom), isFilled: true, isClosed: true);
                }

                context.LineTo(point, isStroked: false, isSmoothJoin: true);
                last = point;
                hasPoint = true;
            }

            if (hasPoint)
                context.LineTo(new Point(last.X, plot.Bottom), isStroked: false, isSmoothJoin: false);
        }

        if (!hasPoint)
            return;

        if (geometry.CanFreeze)
            geometry.Freeze();

        dc.DrawGeometry(brush, null, geometry);
    }

    private static Point ToPoint(
        DateTime timestamp,
        double value,
        DateTime start,
        double totalSeconds,
        Rect plot,
        double min,
        double max)
    {
        double x = plot.Left + (timestamp - start).TotalSeconds / totalSeconds * plot.Width;
        double y = plot.Bottom - (Math.Clamp(value, min, max) - min) / (max - min) * plot.Height;

        return new Point(x, y);
    }

    private static DiskActivity? Disk(ChartSample sample, string diskId) =>
        sample.Disks is { } disks && disks.TryGetValue(diskId, out DiskActivity? disk) ? disk : null;

    private Brush Resource(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;

    private static FormattedText Text(string text, Typeface typeface, double size, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, dpi);

    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (LiveChart)d;

        if (chart._observable is not null)
            chart._observable.CollectionChanged -= chart.OnCollectionChanged;

        chart._observable = e.NewValue as INotifyCollectionChanged;

        if (chart._observable is not null)
            chart._observable.CollectionChanged += chart.OnCollectionChanged;

        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        InvalidateVisual();

    /// <param name="PercentArea">
    /// Se dibuja como área de fondo en escala 0–100 % de la altura, sin usar el
    /// eje. Sirve para mostrar el % de actividad del disco junto a sus MB/s.
    /// </param>
    private readonly record struct ChartSeries(
        Func<ChartSample, double?> Selector,
        Brush Brush,
        bool PercentArea = false);
}
