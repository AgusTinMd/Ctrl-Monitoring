using System.Globalization;
using System.Management;

namespace FanCtrl.Core.Inventory;

/// <summary>Módulo de memoria física instalado.</summary>
public sealed record MemoryModule(long CapacityBytes, int SpeedMhz);

/// <summary>Disco físico detectado por Windows.</summary>
public sealed record PhysicalDisk(string Model, long SizeBytes, string InterfaceType);

/// <summary>Inventario estático del equipo: memoria instalada y discos físicos.</summary>
public sealed record MachineInventory(
    long TotalMemoryBytes,
    IReadOnlyList<MemoryModule> Modules,
    IReadOnlyList<PhysicalDisk> Disks)
{
    public static MachineInventory Empty { get; } = new(
        0,
        Array.Empty<MemoryModule>(),
        Array.Empty<PhysicalDisk>());
}

/// <summary>
/// Lee RAM y discos con WMI. Son datos que no cambian mientras la app está
/// abierta, así que se consultan una sola vez y se cachean en el view model.
/// </summary>
public static class MachineInventoryReader
{
    private const string Scope = @"root\CIMV2";

    public static MachineInventory Read()
    {
        try
        {
            List<MemoryModule> modules = ReadMemoryModules();
            long total = modules.Sum(module => module.CapacityBytes);

            if (total <= 0)
                total = ReadTotalMemory();

            return new MachineInventory(total, modules, ReadDisks());
        }
        catch
        {
            return MachineInventory.Empty;
        }
    }

    private static List<MemoryModule> ReadMemoryModules()
    {
        var result = new List<MemoryModule>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                Scope, "SELECT Capacity, Speed FROM Win32_PhysicalMemory");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    long capacity = ToLong(item["Capacity"]);

                    if (capacity > 0)
                        result.Add(new MemoryModule(capacity, (int)ToLong(item["Speed"])));
                }
            }
        }
        catch
        {
            // Sin WMI se recurre al total que reporta el sistema.
        }

        return result;
    }

    private static long ReadTotalMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                Scope, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                    return ToLong(item["TotalPhysicalMemory"]);
            }
        }
        catch
        {
            // best effort
        }

        return 0;
    }

    private static List<PhysicalDisk> ReadDisks()
    {
        var result = new List<PhysicalDisk>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                Scope, "SELECT Model, Size, InterfaceType FROM Win32_DiskDrive");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    long size = ToLong(item["Size"]);

                    if (size <= 0)
                        continue;

                    string model = item["Model"]?.ToString()?.Trim() ?? "Disco";
                    string interfaceType = item["InterfaceType"]?.ToString()?.Trim() ?? string.Empty;

                    result.Add(new PhysicalDisk(model, size, interfaceType));
                }
            }
        }
        catch
        {
            // best effort
        }

        return result;
    }

    private static long ToLong(object? value)
    {
        if (value is null)
            return 0;

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }
}
