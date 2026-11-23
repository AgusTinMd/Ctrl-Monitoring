namespace CtrlMonitor.Core.Fps;

/// <summary>
/// Deduce la API gráfica de un juego a partir de dos datos que no tocan el
/// juego: el «PresentRuntime» que informa PresentMon y las librerías
/// gráficas que Windows registró como cargadas en su proceso (ETW).
/// </summary>
public static class GraphicsApi
{
    public const string DirectX9 = "DX9";
    public const string DirectX11 = "DX11";
    public const string DirectX12 = "DX12";
    public const string Vulkan = "VK";
    public const string OpenGl = "OGL";

    /// <summary>Librerías que identifican cada API (nombre de archivo, en minúsculas).</summary>
    public static readonly IReadOnlyDictionary<string, string> Modules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["d3d9.dll"] = DirectX9,
        ["d3d10.dll"] = DirectX11,
        ["d3d10_1.dll"] = DirectX11,
        ["d3d11.dll"] = DirectX11,
        ["d3d12.dll"] = DirectX12,
        ["d3d12core.dll"] = DirectX12,
        ["vulkan-1.dll"] = Vulkan,
        ["opengl32.dll"] = OpenGl
    };

    /// <summary>True si el archivo es una de las librerías que interesan.</summary>
    public static bool IsGraphicsModule(string? path) =>
        !string.IsNullOrEmpty(path) && Modules.ContainsKey(Path.GetFileName(path));

    /// <summary>
    /// API del juego, o null si no hay certeza. Prioridad cuando hay varias:
    /// DX12, Vulkan, DX11, OpenGL (algunos juegos cargan más de una).
    /// </summary>
    public static string? Detect(string? presentRuntime, IReadOnlyCollection<string> loadedApis)
    {
        string runtime = presentRuntime?.Trim() ?? string.Empty;

        // DX9 se presenta con su propio runtime: es seguro sin mirar librerías.
        if (runtime.Equals("D3D9", StringComparison.OrdinalIgnoreCase))
            return DirectX9;

        bool Has(string api) => loadedApis.Contains(api);

        if (runtime.Equals("DXGI", StringComparison.OrdinalIgnoreCase))
        {
            if (Has(DirectX12))
                return DirectX12;

            if (Has(Vulkan))
                return Vulkan;

            if (Has(DirectX11))
                return DirectX11;

            return null;
        }

        // «Other»: Vulkan u OpenGL presentando sin DXGI.
        if (Has(Vulkan))
            return Vulkan;

        if (Has(OpenGl))
            return OpenGl;

        return null;
    }
}
