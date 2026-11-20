using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CtrlMonitor.Core.Localization;
using CtrlMonitor.Core.Overlay;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Brochas del overlay, congeladas y cacheadas por color.</summary>
internal static class OverlayBrushes
{
    private static readonly Dictionary<string, Brush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly Brush Separator = FromHex("#66FFFFFF");
    public static readonly Brush GlobalChoice = FromHex("#FF647696");

    public static Brush FromHex(string hex)
    {
        if (Cache.TryGetValue(hex, out Brush? cached))
            return cached;

        Brush brush;

        try
        {
            var solid = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            solid.Freeze();
            brush = solid;
        }
        catch
        {
            brush = Brushes.White;
        }

        Cache[hex] = brush;
        return brush;
    }
}

/// <summary>Una opción del selector de color.</summary>
public sealed class OverlayColorOption : ObservableObject
{
    public OverlayColorOption(string hex, string nameKey, Brush? brush = null)
    {
        Hex = hex;
        NameKey = nameKey;
        Brush = brush ?? OverlayBrushes.FromHex(hex);
    }

    public string Hex { get; }
    public string NameKey { get; }
    public string Name => Loc.T(NameKey);
    public Brush Brush { get; }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Name));
}

/// <summary>Color elegido para un grupo (CPU, GPU, RAM o red).</summary>
public sealed class OverlayGroupColorOption : ObservableObject
{
    private readonly Action _changed;
    private string _selectedHex;

    public OverlayGroupColorOption(
        string groupKey,
        string labelKey,
        string selectedHex,
        IReadOnlyList<OverlayColorOption> options,
        Action changed)
    {
        GroupKey = groupKey;
        LabelKey = labelKey;
        _selectedHex = selectedHex;
        Options = options;
        _changed = changed;
    }

    public string GroupKey { get; }
    public string LabelKey { get; }
    public string Label => Loc.T(LabelKey);
    public IReadOnlyList<OverlayColorOption> Options { get; }

    public string SelectedHex
    {
        get => _selectedHex;
        set
        {
            string normalized = value ?? string.Empty;

            if (string.Equals(_selectedHex, normalized, StringComparison.Ordinal))
                return;

            _selectedHex = normalized;
            OnPropertyChanged();
            _changed();
        }
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));
}

/// <summary>Una métrica que el usuario puede marcar para la barra.</summary>
public sealed class OverlayMetricOption : ObservableObject
{
    private readonly Action _changed;
    private bool _isSelected;

    public OverlayMetricOption(string key, string labelKey, bool isSelected, Action changed)
    {
        Key = key;
        LabelKey = labelKey;
        _isSelected = isSelected;
        _changed = changed;
    }

    public string Key { get; }
    public string LabelKey { get; }
    public string Label => Loc.T(LabelKey);
    public string GroupKey => OverlayMetricKeys.GroupOf(Key);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                _changed();
        }
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));
}

/// <summary>Un trozo de la barra: texto y color, o un separador.</summary>
public sealed class OverlaySegmentViewModel
{
    public OverlaySegmentViewModel(string text, Brush brush, bool isSeparator = false)
    {
        Text = text;
        Brush = brush;
        IsSeparator = isSeparator;
    }

    public string Text { get; }
    public Brush Brush { get; }
    public bool IsSeparator { get; }

    public Thickness Margin => IsSeparator ? new Thickness(2, 0, 8, 0) : new Thickness(0, 0, 8, 0);
}

/// <summary>Paleta compartida por el selector global y los colores por grupo.</summary>
public static class OverlayColorPalette
{
    public static readonly OverlayColorOption Global =
        new(string.Empty, "overlay.color.global", OverlayBrushes.GlobalChoice);

    public static readonly IReadOnlyList<OverlayColorOption> All = new[]
    {
        new OverlayColorOption("#FFFFFFFF", "overlay.color.white"),
        new OverlayColorOption("#FFB0BEC5", "overlay.color.gray"),
        new OverlayColorOption("#FF7BD88F", "overlay.color.green"),
        new OverlayColorOption("#FFFFD166", "overlay.color.yellow"),
        new OverlayColorOption("#FFFF9F43", "overlay.color.orange"),
        new OverlayColorOption("#FFFF6B6B", "overlay.color.red"),
        new OverlayColorOption("#FF56B6C2", "overlay.color.cyan"),
        new OverlayColorOption("#FF6CB6FF", "overlay.color.blue"),
        new OverlayColorOption("#FFC792EA", "overlay.color.purple"),
        new OverlayColorOption("#FFFF8FB1", "overlay.color.pink")
    };

    public static readonly IReadOnlyList<OverlayColorOption> WithGlobal =
        new[] { Global }.Concat(All).ToList();

    public static void RefreshLanguage()
    {
        Global.RefreshLanguage();

        foreach (OverlayColorOption option in All)
            option.RefreshLanguage();
    }
}

/// <summary>Una de las nueve posiciones predefinidas del selector.</summary>
public sealed class OverlayAnchorOption : ObservableObject
{
    private readonly Action<string> _select;
    private bool _isSelected;

    public OverlayAnchorOption(string key, string current, Action<string> select)
    {
        Key = key;
        _select = select;
        _isSelected = string.Equals(key, current, StringComparison.OrdinalIgnoreCase);
    }

    public string Key { get; }

    /// <summary>Flecha que apunta al lugar de la pantalla.</summary>
    public string Glyph => Key switch
    {
        OverlayAnchors.TopLeft => "↖",
        OverlayAnchors.TopCenter => "↑",
        OverlayAnchors.TopRight => "↗",
        OverlayAnchors.MiddleLeft => "←",
        OverlayAnchors.MiddleCenter => "•",
        OverlayAnchors.MiddleRight => "→",
        OverlayAnchors.BottomLeft => "↙",
        OverlayAnchors.BottomCenter => "↓",
        _ => "↘"
    };

    public string Label => Loc.T("overlay.anchor." + Key);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            OnPropertyChanged();

            if (value)
                _select(Key);
        }
    }

    /// <summary>Marca esta opción si coincide con la posición actual, sin volver a elegirla.</summary>
    public void Refresh(string current)
    {
        bool selected = string.Equals(Key, current, StringComparison.OrdinalIgnoreCase);

        if (_isSelected == selected)
            return;

        _isSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));
}
