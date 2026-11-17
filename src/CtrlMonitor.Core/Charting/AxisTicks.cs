namespace CtrlMonitor.Core.Charting;

/// <summary>
/// Separación de las líneas y etiquetas del eje vertical de un gráfico.
/// </summary>
/// <param name="GridStep">Distancia entre líneas de la grilla.</param>
/// <param name="LabelStep">Distancia entre etiquetas; siempre múltiplo de <see cref="GridStep"/>.</param>
public readonly record struct AxisTicks(double GridStep, double LabelStep)
{
    /// <summary>Altura mínima, en píxeles, entre dos líneas de la grilla.</summary>
    public const double MinGridSpacing = 14;

    /// <summary>Altura mínima, en píxeles, entre dos etiquetas del eje.</summary>
    public const double MinLabelSpacing = 28;

    /// <summary>
    /// Elige pasos «redondos» (1, 2, 5 × 10ⁿ) a partir de <paramref name="baseStep"/>
    /// para que la grilla y las etiquetas entren en <paramref name="plotHeight"/>
    /// sin amontonarse, sea cual sea el rango de valores.
    /// </summary>
    public static AxisTicks Compute(double min, double max, double baseStep, double plotHeight)
    {
        double range = max - min;
        double gridStep = baseStep > 0 ? baseStep : 1;

        if (!(range > 0) || !(plotHeight > 0) || double.IsInfinity(range))
            return new AxisTicks(gridStep, gridStep);

        int maxLines = Math.Max(2, (int)(plotHeight / MinGridSpacing));
        int maxLabels = Math.Max(2, (int)(plotHeight / MinLabelSpacing));

        while (range / gridStep > maxLines)
            gridStep = NextNice(gridStep);

        double factor = 1;

        while (range / (gridStep * factor) > maxLabels)
            factor = NextNice(factor);

        return new AxisTicks(gridStep, gridStep * factor);
    }

    /// <summary>Siguiente valor de la serie 1, 2, 5, 10, 20, 50…</summary>
    public static double NextNice(double value)
    {
        if (!(value > 0))
            return 1;

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double mantissa = Math.Round(value / magnitude, 6);

        double next = mantissa < 2 ? 2 : mantissa < 5 ? 5 : 10;
        return next * magnitude;
    }

    /// <summary>Indica si <paramref name="value"/> cae sobre una etiqueta.</summary>
    public bool IsLabel(double value)
    {
        double ratio = value / LabelStep;
        return Math.Abs(ratio - Math.Round(ratio)) < 1e-6;
    }
}
