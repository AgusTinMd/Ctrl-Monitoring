using DiskInfoToolkit;
using DiskInfoToolkit.Enums.Interop;
using DiskInfoToolkit.Interop.Enums;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using SmartAttribute = DiskInfoToolkit.SmartAttribute;

namespace CtrlMonitor.Core.Hardware;

public sealed partial class HardwareService
{
    /// <summary>
    /// Datos S.M.A.R.T. de cada disco, con los valores de la última lectura de
    /// discos (ver <see cref="StorageUpdatesEnabled"/>). No toca el hardware:
    /// solo copia lo que LibreHardwareMonitor ya leyó.
    /// </summary>
    public IReadOnlyList<DiskSmartInfo> GetDiskSmart()
    {
        var result = new List<DiskSmartInfo>();

        lock (_gate)
        {
            if (_disposed)
                return result;

            foreach (IHardware hardware in _computer.Hardware)
            {
                if (hardware is not StorageDevice { Storage: { } storage })
                    continue;

                try
                {
                    result.Add(ToSmartInfo(storage));
                }
                catch (Exception ex)
                {
                    // Un disco con datos raros no debe dejar sin información a los demás.
                    LastError = ex.Message;
                }
            }
        }

        return result.OrderBy(disk => disk.Number).ToList();
    }

    private static DiskSmartInfo ToSmartInfo(Storage storage)
    {
        SmartInfo? smart = storage.Smart;
        List<SmartAttribute> attributes = smart?.SmartAttributes ?? new List<SmartAttribute>();

        ulong? Raw(params SmartAttributeType[] types)
        {
            foreach (SmartAttributeType type in types)
            {
                SmartAttribute? match = attributes.FirstOrDefault(a => a.Info?.Type == type);

                if (match is not null)
                    return match.Attribute.RawValueULong;
            }

            return null;
        }

        return new DiskSmartInfo
        {
            Number = storage.DriveNumber,
            Model = storage.Model?.Trim() ?? string.Empty,
            Firmware = (storage.Firmware ?? storage.FirmwareRev ?? string.Empty).Trim(),
            SerialNumber = storage.SerialNumber?.Trim() ?? string.Empty,
            Interface = InterfaceName(storage.BusType, storage.IsNVMe),
            IsSsd = storage.IsSSD,
            IsNvme = storage.IsNVMe,
            SizeBytes = storage.TotalSize,
            ToolkitStatus = smart?.DiskStatus switch
            {
                DiskStatus.Good => DiskCondition.Good,
                DiskStatus.Caution => DiskCondition.Caution,
                DiskStatus.Bad => DiskCondition.Bad,
                _ => DiskCondition.Unknown
            },
            Temperature = smart?.Temperature is { } temperature && temperature > 0 ? temperature : null,
            TemperatureWarning = smart?.TemperatureWarning is { } warning && warning > 0 ? warning : null,
            LifePercent = smart?.Life is { } life && life >= 0 ? life : null,
            HostReadsGb = smart?.HostReads is { } reads ? reads : null,
            HostWritesGb = smart?.HostWrites is { } writes ? writes : null,
            PowerOnHours = smart is null ? null : NonZero(Math.Max(smart.MeasuredPowerOnHours, smart.DetectedPowerOnHours)),
            PowerOnCount = smart is null ? null : NonZero(smart.PowerOnCount),
            UnsafeShutdowns = Raw(SmartAttributeType.UnsafeShutdownCount, SmartAttributeType.UnexpectedPowerLoss),
            ReallocatedSectors = Raw(SmartAttributeType.ReallocatedSectorsCount),
            PendingSectors = Raw(SmartAttributeType.PendingSectorCount),
            UncorrectableSectors = Raw(SmartAttributeType.UncorrectableSectorCount),
            MediaErrors = Raw(SmartAttributeType.MediaAndDataIntegrityErrors),
            AvailableSpare = Raw(SmartAttributeType.AvailableSpare),
            AvailableSpareThreshold = Raw(SmartAttributeType.AvailableSpareThreshold),
            CriticalWarning = Raw(SmartAttributeType.CriticalWarning),
            Attributes = attributes.Select(a => ToReading(a, storage.IsNVMe)).ToList()
        };
    }

    // NVMe no tiene valores normalizados ni umbrales: solo el valor crudo.
    private static SmartAttributeReading ToReading(SmartAttribute attribute, bool isNvme)
    {
        return new SmartAttributeReading(
            attribute.Attribute.ID,
            attribute.Info?.Name ?? $"0x{attribute.Attribute.ID:X2}",
            isNvme ? -1 : attribute.Attribute.CurrentValue,
            isNvme ? -1 : attribute.Attribute.WorstValue,
            isNvme ? -1 : attribute.Attribute.Threshold,
            attribute.Attribute.RawValueULong);
    }

    private static ulong? NonZero(ulong value) => value == 0 ? null : value;

    private static string InterfaceName(StorageBusType bus, bool isNvme) => bus switch
    {
        _ when isNvme => "NVMe",
        StorageBusType.BusTypeSata => "SATA",
        StorageBusType.BusTypeAta => "ATA",
        StorageBusType.BusTypeUsb => "USB",
        StorageBusType.BusTypeScsi => "SCSI",
        StorageBusType.BusTypeSas => "SAS",
        StorageBusType.BusTypeRAID => "RAID",
        StorageBusType.BusTypeSd => "SD",
        _ => string.Empty
    };
}
