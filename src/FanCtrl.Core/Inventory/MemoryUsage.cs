using System.Runtime.InteropServices;

namespace FanCtrl.Core.Inventory;

/// <summary>Uso actual de la memoria física del equipo.</summary>
public readonly record struct MemoryUsage(long TotalBytes, long AvailableBytes)
{
    private const double Gigabyte = 1024d * 1024d * 1024d;

    public long UsedBytes => Math.Max(0, TotalBytes - AvailableBytes);

    public double TotalGb => TotalBytes / Gigabyte;

    public double UsedGb => UsedBytes / Gigabyte;

    public double UsedPercent => TotalBytes > 0
        ? (double)UsedBytes / TotalBytes * 100d
        : 0d;
}

/// <summary>
/// Lee la memoria en uso con GlobalMemoryStatusEx. Es una llamada barata y sin
/// WMI, pensada para actualizarse en cada refresco del monitor.
/// </summary>
public static class MemoryUsageReader
{
    public static MemoryUsage Read()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!GlobalMemoryStatusEx(ref status))
            return default;

        return new MemoryUsage((long)status.TotalPhys, (long)status.AvailPhys);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
