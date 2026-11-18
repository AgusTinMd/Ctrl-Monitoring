using CtrlMonitor.Core.Hardware;

namespace CtrlMonitor.Core.Tests;

public class HardwareBrandTests
{
    [Theory]
    [InlineData("ASUS PRIME B560M-A", "ASUS")]
    [InlineData("ASUSTeK COMPUTER INC. ROG STRIX B550-F GAMING", "ASUS")]
    [InlineData("ROG STRIX Z790-E GAMING WIFI", "ASUS")]
    [InlineData("TUF GAMING X570-PLUS", "ASUS")]
    [InlineData("Gigabyte B550 AORUS ELITE V2", "GIGABYTE")]
    [InlineData("B650 AORUS ELITE AX", "GIGABYTE")]
    [InlineData("MSI MAG B650 TOMAHAWK WIFI", "MSI")]
    [InlineData("Micro-Star International Co., Ltd. PRO B760M-A", "MSI")]
    [InlineData("MEG Z790 ACE", "MSI")]
    [InlineData("ASRock B550M Pro4", "ASRock")]
    [InlineData("Biostar B550MH", "BIOSTAR")]
    [InlineData("Dell Inc. 0XYZ12", "Dell")]
    [InlineData("HP 8603", "HP")]
    [InlineData("LENOVO 3717", "Lenovo")]
    public void Board_KnownBrands(string boardName, string expected)
    {
        Assert.Equal(expected, BoardBrands.Detect(boardName)?.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Placa madre")]
    [InlineData("To Be Filled By O.E.M.")]
    [InlineData("IMAGE-B450")]
    public void Board_Unknown_ReturnsNull(string? boardName)
    {
        Assert.Null(BoardBrands.Detect(boardName));
    }

    [Theory]
    [InlineData("ASUS PRIME B560M-A", "asus.png")]
    [InlineData("Gigabyte B550 AORUS ELITE", "gigabyte.png")]
    [InlineData("MSI MAG B650 TOMAHAWK WIFI", "msi.png")]
    [InlineData("ASRock B550M Pro4", "asrock.png")]
    [InlineData("Biostar B550MH", null)]
    public void Board_LogoFiles(string boardName, string? expected)
    {
        Assert.Equal(expected, BoardBrands.Detect(boardName)?.LogoFile);
    }

    [Theory]
    [InlineData("AMD Ryzen 7 5800X", "AMD")]
    [InlineData("AMD Ryzen Threadripper PRO 5975WX", "AMD")]
    [InlineData("Ryzen 5 7600", "AMD")]
    [InlineData("AMD Athlon 3000G", "AMD")]
    [InlineData("Intel Core i7-12700K", "Intel")]
    [InlineData("13th Gen Intel(R) Core(TM) i5-13600K", "Intel")]
    [InlineData("Intel Core Ultra 7 265K", "Intel")]
    public void Cpu_KnownBrands(string cpuName, string expected)
    {
        HardwareBrand? brand = CpuBrands.Detect(cpuName);
        Assert.Equal(expected, brand?.Name);
        Assert.NotNull(brand?.LogoFile);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("CPU")]
    [InlineData("Snapdragon X Elite")]
    public void Cpu_Unknown_ReturnsNull(string? cpuName)
    {
        Assert.Null(CpuBrands.Detect(cpuName));
    }

    [Theory]
    [InlineData("AMD Radeon RX 6700 XT", "amd.png")]
    [InlineData("NVIDIA GeForce RTX 4070", "nvidia.png")]
    [InlineData("GeForce GTX 1060 6GB", "nvidia.png")]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", "intel.png")]
    [InlineData("Intel UHD Graphics 630", "intel.png")]
    public void Gpu_KnownBrands(string gpuName, string logo) =>
        Assert.Equal(logo, GpuBrands.Detect(gpuName)?.LogoFile);

    [Theory]
    [InlineData(null)]
    [InlineData("GPU")]
    [InlineData("Microsoft Basic Display Adapter")]
    public void Gpu_Unknown_ReturnsNull(string? gpuName) =>
        Assert.Null(GpuBrands.Detect(gpuName));
}
