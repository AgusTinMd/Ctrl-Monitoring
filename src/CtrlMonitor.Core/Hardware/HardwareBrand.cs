using System.Text.RegularExpressions;

namespace CtrlMonitor.Core.Hardware;

/// <summary>Marca de un componente (placa madre o CPU).</summary>
/// <param name="Name">Nombre de la marca para mostrar, por ejemplo «GIGABYTE».</param>
/// <param name="LogoFile">
/// Archivo del logo dentro de <c>Assets</c> (por ejemplo «asus.png»), o
/// <c>null</c> si no hay logo y se muestra el nombre como texto.
/// </param>
public sealed record HardwareBrand(string Name, string? LogoFile);

/// <summary>
/// Reconoce una marca dentro de un nombre de hardware. Cada alias se busca como
/// palabra completa: «MAG» no coincide dentro de «IMAGE». Se devuelve la primera
/// marca de la lista que coincide, así que el orden importa.
/// </summary>
internal sealed class BrandMatcher
{
    private readonly (HardwareBrand Brand, Regex Pattern)[] _known;

    public BrandMatcher(params (HardwareBrand Brand, string[] Aliases)[] known)
    {
        _known = known
            .Select(k => (k.Brand, new Regex(
                $@"(?<![A-Za-z0-9])(?:{string.Join("|", k.Aliases.Select(Regex.Escape))})(?![A-Za-z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            .ToArray();
    }

    public HardwareBrand? Detect(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach ((HardwareBrand brand, Regex pattern) in _known)
        {
            if (pattern.IsMatch(name))
                return brand;
        }

        return null;
    }
}

/// <summary>
/// Marca de la placa a partir del nombre que da LibreHardwareMonitor
/// («ASUS PRIME B560M-A», «Gigabyte B550 AORUS ELITE», «MSI MAG B650 TOMAHAWK»…).
/// Para sumar el logo de una marca alcanza con poner su PNG en Assets y
/// completar <see cref="HardwareBrand.LogoFile"/> en la lista.
/// </summary>
public static class BoardBrands
{
    // Las submarcas (ROG, AORUS, MEG…) van con su fabricante.
    private static readonly BrandMatcher Matcher = new(
        (new HardwareBrand("ASUS", "asus.png"), new[] { "ASUS", "ASUSTeK", "ROG", "TUF GAMING", "ProArt" }),
        (new HardwareBrand("GIGABYTE", "gigabyte.png"), new[] { "Gigabyte", "AORUS" }),
        (new HardwareBrand("MSI", "msi.png"), new[] { "MSI", "Micro-Star", "MEG", "MPG", "MAG" }),
        (new HardwareBrand("ASRock", "asrock.png"), new[] { "ASRock", "ASRockRack" }),
        (new HardwareBrand("BIOSTAR", null), new[] { "Biostar" }),
        (new HardwareBrand("EVGA", null), new[] { "EVGA" }),
        (new HardwareBrand("NZXT", null), new[] { "NZXT" }),
        (new HardwareBrand("Supermicro", null), new[] { "Supermicro", "Super Micro" }),
        (new HardwareBrand("COLORFUL", null), new[] { "Colorful" }),
        (new HardwareBrand("MAXSUN", null), new[] { "Maxsun" }),
        (new HardwareBrand("Dell", null), new[] { "Dell" }),
        (new HardwareBrand("HP", null), new[] { "HP", "Hewlett-Packard" }),
        (new HardwareBrand("Lenovo", null), new[] { "Lenovo" }),
        (new HardwareBrand("Acer", null), new[] { "Acer" }),
        (new HardwareBrand("Intel", "intel.png"), new[] { "Intel" }));

    /// <summary>
    /// Marca de la placa, o <c>null</c> si el nombre no corresponde a una
    /// marca conocida (en ese caso no se muestra ningún logo).
    /// </summary>
    public static HardwareBrand? Detect(string? boardName) => Matcher.Detect(boardName);
}

/// <summary>
/// Marca del procesador a partir de su nombre («AMD Ryzen 7 5800X»,
/// «13th Gen Intel(R) Core(TM) i7-13700K», «Intel Core Ultra 7 265K»…).
/// </summary>
public static class CpuBrands
{
    private static readonly BrandMatcher Matcher = new(
        (new HardwareBrand("Intel", "intel.png"), new[] { "Intel", "GenuineIntel" }),
        (new HardwareBrand("AMD", "amd.png"), new[] { "AMD", "AuthenticAMD", "Ryzen", "Threadripper", "Athlon", "EPYC" }));

    /// <summary>Marca del CPU, o <c>null</c> si no se reconoce.</summary>
    public static HardwareBrand? Detect(string? cpuName) => Matcher.Detect(cpuName);
}
