using LibreHardwareMonitor.Hardware;

namespace CtrlMonitor.Core.Hardware;

/// <summary>
/// Implementación en memoria de ISettings para que LibreHardwareMonitor no escriba
/// el modo de control en el registro. La app es dueña de su propia configuración.
/// </summary>
public sealed class InMemorySettings : ISettings
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public bool Contains(string name) => _values.ContainsKey(name);

    public string GetValue(string name, string value) => _values.TryGetValue(name, out string? result) ? result : value;

    public void SetValue(string name, string value) => _values[name] = value;

    public void Remove(string name) => _values.Remove(name);
}
