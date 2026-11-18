using System.Management;
using System.Runtime.Versioning;

namespace CtrlMonitor.Core.Inventory;

/// <summary>Estado de salud que informa Windows para un disco.</summary>
public enum DiskHealth
{
    Unknown = 0,
    Healthy = 1,
    Warning = 2,
    Unhealthy = 3
}

/// <summary>Disco físico con su salud y las letras de sus volúmenes.</summary>
/// <param name="Number">Número de disco de Windows (el de «Disco 2»).</param>
/// <param name="Model">Modelo, por ejemplo «KINGSTON SNV2S500G».</param>
/// <param name="MediaType">«SSD», «HDD» o vacío si Windows no lo sabe.</param>
/// <param name="BusType">«NVMe», «SATA», «USB»… o vacío.</param>
/// <param name="Health">Salud según Windows (S.M.A.R.T. y diagnóstico del disco).</param>
/// <param name="SizeBytes">Tamaño total.</param>
/// <param name="Letters">Letras de sus volúmenes, por ejemplo «C:».</param>
public sealed record PhysicalDiskInfo(
    int Number,
    string Model,
    string MediaType,
    string BusType,
    DiskHealth Health,
    long SizeBytes,
    IReadOnlyList<string> Letters);

/// <summary>
/// Lee los discos físicos con la API de almacenamiento de Windows
/// (root\Microsoft\Windows\Storage). No necesita el driver de sensores.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PhysicalDiskReader
{
    private const string Scope = @"root\Microsoft\Windows\Storage";

    public static IReadOnlyList<PhysicalDiskInfo> Read()
    {
        Dictionary<int, List<string>> letters = ReadLetters();
        var result = new List<PhysicalDiskInfo>();

        using var searcher = new ManagementObjectSearcher(
            Scope, "SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk");

        foreach (ManagementBaseObject disk in searcher.Get())
        {
            using (disk)
            {
                if (!int.TryParse(Convert.ToString(disk["DeviceId"]), out int number))
                    continue;

                result.Add(new PhysicalDiskInfo(
                    number,
                    (Convert.ToString(disk["FriendlyName"]) ?? string.Empty).Trim(),
                    MediaTypeName(ToInt(disk["MediaType"])),
                    BusTypeName(ToInt(disk["BusType"])),
                    HealthFrom(ToInt(disk["HealthStatus"])),
                    disk["Size"] is null ? 0 : Convert.ToInt64(disk["Size"]),
                    letters.TryGetValue(number, out List<string>? list) ? list : Array.Empty<string>()));
            }
        }

        return result.OrderBy(disk => disk.Number).ToList();
    }

    private static Dictionary<int, List<string>> ReadLetters()
    {
        var result = new Dictionary<int, List<string>>();

        using var searcher = new ManagementObjectSearcher(
            Scope, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition");

        foreach (ManagementBaseObject partition in searcher.Get())
        {
            using (partition)
            {
                // DriveLetter es un char; 0 cuando la partición no tiene letra.
                char letter = partition["DriveLetter"] is char c ? c : '\0';

                if (letter == '\0' || !char.IsLetter(letter))
                    continue;

                int number = ToInt(partition["DiskNumber"]);

                if (!result.TryGetValue(number, out List<string>? list))
                    result[number] = list = new List<string>();

                list.Add($"{char.ToUpperInvariant(letter)}:");
            }
        }

        foreach (List<string> list in result.Values)
            list.Sort(StringComparer.Ordinal);

        return result;
    }

    private static int ToInt(object? value) => value is null ? -1 : Convert.ToInt32(value);

    // Valores de MSFT_PhysicalDisk documentados por Microsoft.
    public static string MediaTypeName(int value) => value switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => string.Empty
    };

    public static string BusTypeName(int value) => value switch
    {
        1 => "SCSI",
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        15 => "VHD",
        17 => "NVMe",
        _ => string.Empty
    };

    public static DiskHealth HealthFrom(int value) => value switch
    {
        0 => DiskHealth.Healthy,
        1 => DiskHealth.Warning,
        2 => DiskHealth.Unhealthy,
        _ => DiskHealth.Unknown
    };
}
