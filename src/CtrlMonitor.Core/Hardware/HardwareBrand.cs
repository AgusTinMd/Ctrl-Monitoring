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
        (new HardwareBrand("BIOSTAR", "biostar.png"), new[] { "Biostar" }),
        (new HardwareBrand("EVGA", "evga.png"), new[] { "EVGA" }),
        (new HardwareBrand("NZXT", "nzxt.png"), new[] { "NZXT" }),
        (new HardwareBrand("Supermicro", "supermicro.png"), new[] { "Supermicro", "Super Micro" }),
        (new HardwareBrand("COLORFUL", "colorful.png"), new[] { "Colorful" }),
        (new HardwareBrand("MAXSUN", "maxsun.png"), new[] { "Maxsun" }),
        (new HardwareBrand("Dell", "dell.png"), new[] { "Dell" }),
        (new HardwareBrand("HP", "hp.png"), new[] { "HP", "Hewlett-Packard" }),
        (new HardwareBrand("Lenovo", "lenovo.png"), new[] { "Lenovo" }),
        (new HardwareBrand("Acer", "acer.png"), new[] { "Acer" }),
        // Notebooks y PCs de marca: la marca suele aparecer en el fabricante del
        // equipo y no en la placa. Alienware va antes que Dell y VAIO antes que Sony.
        (new HardwareBrand("Alienware", "alienware.png"), new[] { "Alienware" }),
        (new HardwareBrand("Apple", "apple.png"), new[] { "Apple", "Apple Inc." }),
        (new HardwareBrand("Microsoft", "microsoft.png"), new[] { "Microsoft", "Surface" }),
        (new HardwareBrand("Samsung", "samsung.png"), new[] { "Samsung" }),
        (new HardwareBrand("Dynabook", "dynabook.png"), new[] { "Dynabook" }),
        (new HardwareBrand("Toshiba", "toshiba.png"), new[] { "Toshiba" }),
        (new HardwareBrand("VAIO", null), new[] { "VAIO" }),
        (new HardwareBrand("Sony", "sony.png"), new[] { "Sony" }),
        (new HardwareBrand("Huawei", "huawei.png"), new[] { "Huawei", "Honor" }),
        (new HardwareBrand("Xiaomi", "xiaomi.png"), new[] { "Xiaomi", "TIMI", "Redmi" }),
        (new HardwareBrand("Razer", "razer.png"), new[] { "Razer" }),
        (new HardwareBrand("LG", "lg.png"), new[] { "LG", "LG Electronics" }),
        (new HardwareBrand("Fujitsu", "fujitsu.png"), new[] { "Fujitsu" }),
        (new HardwareBrand("Framework", "framework.png"), new[] { "Framework" }),
        (new HardwareBrand("System76", "system76.png"), new[] { "System76" }),
        (new HardwareBrand("Packard Bell", "packardbell.png"), new[] { "Packard Bell" }),
        (new HardwareBrand("Gateway", null), new[] { "Gateway" }),
        (new HardwareBrand("MEDION", null), new[] { "Medion" }),
        (new HardwareBrand("CHUWI", null), new[] { "Chuwi" }),
        // Fabricantes de placas y equipos chicos.
        (new HardwareBrand("ZOTAC", "zotac.png"), new[] { "Zotac" }),
        (new HardwareBrand("ECS", "ecs.png"), new[] { "ECS", "Elitegroup" }),
        (new HardwareBrand("Foxconn", "foxconn.png"), new[] { "Foxconn" }),
        (new HardwareBrand("Clevo", "clevo.png"), new[] { "Clevo" }),
        (new HardwareBrand("HUANANZHI", null), new[] { "Huananzhi" }),
        (new HardwareBrand("MACHINIST", null), new[] { "Machinist" }),
        // Marcas argentinas de notebooks: suelen usar placas genéricas, así que
        // la marca aparece en los datos del equipo y no en el nombre de la placa.
        (new HardwareBrand("ENOVA", null), new[] { "Enova" }),
        (new HardwareBrand("Positivo", "positivo.png"), new[] { "Positivo" }),
        (new HardwareBrand("BGH", "bgh.png"), new[] { "BGH" }),
        (new HardwareBrand("Bangho", null), new[] { "Bangho" }),
        (new HardwareBrand("EXO", "exo.png"), new[] { "EXO", "Exomate" }),
        (new HardwareBrand("Noblex", "noblex.png"), new[] { "Noblex" }),
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

/// <summary>
/// Marca de la placa de video a partir de su nombre («AMD Radeon RX 6700 XT»,
/// «NVIDIA GeForce RTX 4070», «Intel Arc A770»…).
/// </summary>
public static class GpuBrands
{
    private static readonly BrandMatcher Matcher = new(
        (new HardwareBrand("NVIDIA", "nvidia.png"), new[] { "NVIDIA", "GeForce", "Quadro", "Tesla", "TITAN" }),
        (new HardwareBrand("AMD", "amd.png"), new[] { "AMD", "Radeon", "ATI", "FirePro" }),
        (new HardwareBrand("Intel", "intel.png"), new[] { "Intel", "Arc", "Iris" }));

    /// <summary>Marca de la GPU, o <c>null</c> si no se reconoce.</summary>
    public static HardwareBrand? Detect(string? gpuName) => Matcher.Detect(gpuName);
}

/// <summary>
/// Marca de un disco a partir de su modelo («WDC WD10EZEX-22MFCA0»,
/// «Samsung SSD 970 EVO», «ST1000DM010-2EP102», «KINGSTON SNV2S500G»…).
/// </summary>
public static class DiskBrands
{
    private static readonly HardwareBrand Seagate = new("Seagate", "seagate.png");
    private static readonly HardwareBrand Crucial = new("Crucial", "crucial.png");

    private static readonly BrandMatcher Matcher = new(
        (new HardwareBrand("Western Digital", "wd.png"), new[] { "WDC", "WD", "Western Digital", "WD_BLACK", "WD Blue", "WD Green" }),
        (new HardwareBrand("Samsung", "samsung.png"), new[] { "Samsung" }),
        (Seagate, new[] { "Seagate" }),
        (new HardwareBrand("Toshiba", "toshiba.png"), new[] { "Toshiba" }),
        (new HardwareBrand("SanDisk", "sandisk.png"), new[] { "SanDisk" }),
        (new HardwareBrand("Kingston", "kingston.png"), new[] { "Kingston" }),
        (Crucial, new[] { "Crucial" }),
        (new HardwareBrand("KIOXIA", "kioxia.png"), new[] { "KIOXIA" }),
        (new HardwareBrand("Intel", "intel.png"), new[] { "Intel", "INTEL SSD" }),
        (new HardwareBrand("ADATA", "adata.png"), new[] { "ADATA", "XPG" }),
        (new HardwareBrand("Corsair", "corsair.png"), new[] { "Corsair" }),
        (new HardwareBrand("SK hynix", "skhynix.png"), new[] { "SK hynix", "SKHynix", "HFM" }),
        (new HardwareBrand("Micron", "micron.png"), new[] { "Micron" }),
        (new HardwareBrand("HGST", "hgst.png"), new[] { "HGST" }),
        (new HardwareBrand("Hitachi", "hitachi.png"), new[] { "Hitachi" }),
        (new HardwareBrand("Lexar", "lexar.png"), new[] { "Lexar" }),
        (new HardwareBrand("Teamgroup", "teamgroup.png"), new[] { "TEAM", "T-FORCE" }),
        (new HardwareBrand("Patriot", "patriot.png"), new[] { "Patriot" }),
        (new HardwareBrand("PNY", "pny.png"), new[] { "PNY" }));

    /// <summary>Marca del disco, o <c>null</c> si el modelo no la deja ver.</summary>
    public static HardwareBrand? Detect(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;

        // Algunas marcas no ponen su nombre en el modelo, solo el prefijo:
        // Seagate «ST1000DM010», Crucial «CT500MX500SSD1».
        string trimmed = model.Trim();

        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^ST\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return Seagate;

        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^CT\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return Crucial;

        return Matcher.Detect(trimmed);
    }
}
