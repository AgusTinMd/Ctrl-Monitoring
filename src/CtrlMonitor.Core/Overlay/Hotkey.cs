using System.Text;

namespace CtrlMonitor.Core.Overlay;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}

/// <summary>
/// Atajo global: modificadores más una tecla (letra, número o F1–F24). Se
/// guarda como texto legible («Ctrl+Alt+O») y se registra con RegisterHotKey.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, string Key)
{
    public const string DefaultToggle = "Ctrl+Alt+O";
    public const string DefaultMode = "Ctrl+Alt+B";
    public const string DefaultRecord = "Ctrl+Alt+R";

    /// <summary>Código de tecla virtual de Windows, o 0 si la tecla no se admite.</summary>
    public int VirtualKey => ToVirtualKey(Key);

    public override string ToString()
    {
        var text = new StringBuilder();

        if (Modifiers.HasFlag(HotkeyModifiers.Control))
            text.Append("Ctrl+");

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            text.Append("Alt+");

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            text.Append("Shift+");

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
            text.Append("Win+");

        return text.Append(Key).ToString();
    }

    /// <summary>
    /// Lee «Ctrl+Alt+O». Exige al menos Ctrl, Alt o Win, para no pisar
    /// teclas que el juego usa solas o solo con Shift.
    /// </summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        string? key = null;

        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win" or "windows":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (key is not null)
                        return false;

                    key = raw.ToUpperInvariant();
                    break;
            }
        }

        if (key is null || ToVirtualKey(key) == 0)
            return false;

        if ((modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0)
            return false;

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    /// <summary>Texto normalizado, o el valor por defecto si no es un atajo válido.</summary>
    public static string NormalizeOr(string? text, string fallback) =>
        TryParse(text, out Hotkey hotkey) ? hotkey.ToString() : fallback;

    private static int ToVirtualKey(string key)
    {
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z')
            return key[0];

        if (key.Length == 1 && key[0] is >= '0' and <= '9')
            return key[0];

        if (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24)
            return 0x70 + f - 1;

        return 0;
    }
}
