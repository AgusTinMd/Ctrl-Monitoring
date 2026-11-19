using System.Management;
using System.Runtime.Versioning;

namespace CtrlMonitor.Core.Hardware;

/// <summary>
/// Datos del equipo que guarda el BIOS (SMBIOS) y que Windows expone por WMI.
/// Las notebooks de marca suelen poner su nombre acá aunque la placa informe
/// algo genérico como «Notebook».
/// </summary>
public sealed record SystemIdentity(
    string SystemManufacturer,
    string SystemModel,
    string BoardManufacturer,
    string BoardProduct)
{
    public static SystemIdentity Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
}

[SupportedOSPlatform("windows")]
public static class SystemIdentityReader
{
    public static SystemIdentity Read()
    {
        (string manufacturer, string model) = Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem", "Manufacturer", "Model");
        (string boardManufacturer, string product) = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard", "Manufacturer", "Product");
        return new SystemIdentity(manufacturer, model, boardManufacturer, product);
    }

    private static (string, string) Query(string query, string first, string second)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                    return (Clean(item[first]), Clean(item[second]));
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Sin WMI se usa solo lo que informa LibreHardwareMonitor.
        }

        return (string.Empty, string.Empty);
    }

    private static string Clean(object? value) => (Convert.ToString(value) ?? string.Empty).Trim();
}

/// <summary>
/// Elige qué nombre mostrar para la placa y dónde buscar la marca, descartando
/// los textos de relleno que dejan muchos fabricantes en el BIOS.
/// </summary>
public static class BoardIdentity
{
    // Textos que el BIOS trae sin completar: no identifican nada.
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "notebook", "laptop", "desktop", "motherboard", "mainboard", "placa madre",
        "to be filled by o.e.m.", "to be filled by oem", "default string", "default",
        "system manufacturer", "system product name", "system version", "system serial number",
        "base board manufacturer", "base board product name", "oem", "o.e.m.", "oem chassis",
        "not applicable", "not specified", "n/a", "na", "none", "invalid", "unknown", "standard",
        "type1productconfigid", "type2 - board product name1", "x.x", "123456789", "0123456789", "1234567890"
    };

    public static bool IsPlaceholder(string? text) =>
        string.IsNullOrWhiteSpace(text) || Placeholders.Contains(text.Trim());

    /// <summary>
    /// Nombre para la tarjeta de placa madre: el de LibreHardwareMonitor si dice
    /// algo; si no, el fabricante y modelo del equipo; si no, los de la placa.
    /// </summary>
    public static string DisplayName(string hardwareName, SystemIdentity identity)
    {
        if (!IsPlaceholder(hardwareName))
            return hardwareName.Trim();

        string system = Join(identity.SystemManufacturer, identity.SystemModel);

        if (system.Length > 0)
            return system;

        string board = Join(identity.BoardManufacturer, identity.BoardProduct);
        return board.Length > 0 ? board : hardwareName.Trim();
    }

    /// <summary>Todo el texto útil donde buscar la marca (placa y equipo).</summary>
    public static string BrandText(string hardwareName, SystemIdentity identity) =>
        Join(hardwareName, identity.SystemManufacturer, identity.SystemModel, identity.BoardManufacturer, identity.BoardProduct);

    private static string Join(params string?[] parts)
    {
        var useful = new List<string>();

        foreach (string? part in parts)
        {
            if (IsPlaceholder(part))
                continue;

            string text = part!.Trim();

            // Evita repetir «ASUS ASUS …» cuando el fabricante ya está en el nombre.
            if (!useful.Any(existing => existing.Contains(text, StringComparison.OrdinalIgnoreCase)))
                useful.Add(text);
        }

        return string.Join(" ", useful);
    }
}
