using System.Diagnostics;
using System.Net.NetworkInformation;

namespace CtrlMonitor.Core.Inventory;

/// <summary>Velocidad instantánea de red, en bytes por segundo.</summary>
public readonly record struct NetworkUsage(double DownBytesPerSecond, double UpBytesPerSecond)
{
    private const double Megabyte = 1024d * 1024d;

    public double DownMegaBytesPerSecond => DownBytesPerSecond / Megabyte;

    public double UpMegaBytesPerSecond => UpBytesPerSecond / Megabyte;

    public static NetworkUsage Zero => default;
}

/// <summary>
/// Calcula la velocidad de bajada y subida sumando las interfaces físicas
/// activas. Guarda los totales de la lectura anterior, así que está pensado
/// para llamarse una vez por refresco del monitor.
/// </summary>
public sealed class NetworkUsageReader
{
    private static readonly string[] VirtualKeywords =
    {
        "virtual", "hyper-v", "vmware", "virtualbox", "loopback", "tunnel",
        "tap-", "wireguard", "tailscale", "zerotier", "bluetooth"
    };

    private long _lastReceived;
    private long _lastSent;
    private long _lastTimestamp;
    private bool _initialized;

    public NetworkUsage Read()
    {
        (long received, long sent) = ReadTotals();
        long now = Stopwatch.GetTimestamp();

        if (!_initialized)
        {
            _initialized = true;
            _lastReceived = received;
            _lastSent = sent;
            _lastTimestamp = now;
            return NetworkUsage.Zero;
        }

        double seconds = (now - _lastTimestamp) / (double)Stopwatch.Frequency;

        _lastTimestamp = now;

        long receivedDelta = received - _lastReceived;
        long sentDelta = sent - _lastSent;
        _lastReceived = received;
        _lastSent = sent;

        if (seconds <= 0)
            return NetworkUsage.Zero;

        return new NetworkUsage(
            Math.Max(0, receivedDelta) / seconds,
            Math.Max(0, sentDelta) / seconds);
    }

    private static (long Received, long Sent) ReadTotals()
    {
        long received = 0;
        long sent = 0;

        foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsPhysicalAndUp(network))
                continue;

            try
            {
                // GetIPStatistics suma IPv4 e IPv6 en Windows.
                IPInterfaceStatistics statistics = network.GetIPStatistics();

                received += statistics.BytesReceived;
                sent += statistics.BytesSent;
            }
            catch (NetworkInformationException)
            {
                // Algunas interfaces virtuales no exponen estadísticas.
            }
        }

        return (received, sent);
    }

    private static bool IsPhysicalAndUp(NetworkInterface network)
    {
        if (network.OperationalStatus != OperationalStatus.Up || network.Speed <= 0)
            return false;

        if (network.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.Wireless80211))
        {
            return false;
        }

        if (network.GetPhysicalAddress().GetAddressBytes().Length == 0)
            return false;

        string description = network.Description;

        return !VirtualKeywords.Any(keyword =>
            description.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
