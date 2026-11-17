using System.Globalization;
using System.Security.Principal;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace CtrlMonitor.Probe;

/// <summary>
/// Sonda de diagnóstico de solo lectura. Vuelca todos los sensores que ve
/// LibreHardwareMonitor para comparar con lo que muestra Ctrl Monitor.
/// No escribe PWM ni modifica el BIOS.
/// </summary>
internal static class Program
{
    private static readonly StringBuilder Log = new();

    private static void Main(string[] args)
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        Write($"Admin: {principal.IsInRole(WindowsBuiltInRole.Administrator)} | Usuario: {identity.Name}");
        Write($"PawnIO instalado: {LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled} | Version: {LibreHardwareMonitor.PawnIo.PawnIo.Version}");
        Write($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Write(new string('-', 78));
        Write("Sonda de solo lectura: no se escribe PWM ni se cambia el BIOS.");
        Write(new string('-', 78));

        RunDump();

        string outputPath = Path.Combine(AppContext.BaseDirectory, "probe-output.txt");
        File.WriteAllText(outputPath, Log.ToString(), Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine($"Resultado guardado en: {outputPath}");

        if (args.Contains("--pause"))
        {
            Console.WriteLine("Presioná ENTER para cerrar...");
            Console.ReadLine();
        }
    }

    private static Computer CreateComputer() => new(new InMemorySettings())
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
        IsControllerEnabled = true,
        IsMemoryEnabled = false,
        IsStorageEnabled = false,
        IsNetworkEnabled = false,
        IsPowerMonitorEnabled = false
    };

    private static void RunDump()
    {
        using var computer = new ComputerOwner(CreateComputer());

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (IHardware hardware in computer.Value.Hardware)
                DumpHardware(hardware, 0);

            if (pass == 0)
            {
                Write(new string('-', 78));
                Write("Segunda pasada (valores estabilizados)");
                Write(new string('-', 78));
            }
        }
    }

    private static void DumpHardware(IHardware hardware, int depth)
    {
        string indent = new(' ', depth * 2);
        Write($"{indent}== {hardware.HardwareType}: {hardware.Name} [{hardware.Identifier}]");

        try
        {
            hardware.Update();
        }
        catch (Exception ex)
        {
            Write($"{indent}   ERROR Update(): {ex.Message}");
        }

        foreach (ISensor sensor in hardware.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index))
        {
            if (sensor.SensorType is not (SensorType.Temperature or SensorType.Fan
                or SensorType.Control or SensorType.Voltage or SensorType.Power or SensorType.Load
                or SensorType.SmallData))
                continue;

            string value = sensor.Value.HasValue
                ? sensor.Value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "n/a";

            string type = sensor.SensorType switch
            {
                SensorType.Control => "PWM",
                _ => sensor.SensorType.ToString()
            };

            Write($"{indent}   [{type,-11}] {sensor.Name,-34} = {value,8}");
        }

        foreach (IHardware sub in hardware.SubHardware)
            DumpHardware(sub, depth + 1);
    }

    private static void Write(string text)
    {
        Console.WriteLine(text);
        Log.AppendLine(text);
    }

    private sealed class InMemorySettings : ISettings
    {
        private readonly Dictionary<string, string> _values = new();

        public bool Contains(string name) => _values.ContainsKey(name);
        public void SetValue(string name, string value) => _values[name] = value;
        public string GetValue(string name, string value) => _values.TryGetValue(name, out string? result) ? result : value;
        public void Remove(string name) => _values.Remove(name);
    }

    private sealed class ComputerOwner : IDisposable
    {
        public ComputerOwner(Computer computer)
        {
            Value = computer;
            try { Value.Open(); }
            catch (Exception ex)
            {
                Write($"ERROR al abrir hardware: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        public Computer Value { get; }

        public void Dispose()
        {
            try { Value.Close(); } catch { /* best effort */ }
        }
    }
}
