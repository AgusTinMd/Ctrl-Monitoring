using CtrlMonitor.Core.Hardware;

namespace CtrlMonitor.Core.Tests;

public class BoardIdentityTests
{
    [Theory]
    [InlineData("Notebook")]
    [InlineData("To Be Filled By O.E.M.")]
    [InlineData("Default string")]
    [InlineData("System Product Name")]
    [InlineData("  ")]
    [InlineData(null)]
    public void RecognizesPlaceholders(string? text) => Assert.True(BoardIdentity.IsPlaceholder(text));

    [Theory]
    [InlineData("ASUS PRIME B560M-A")]
    [InlineData("ENOVA")]
    [InlineData("NL40_50CU")]
    public void RealNamesAreNotPlaceholders(string text) => Assert.False(BoardIdentity.IsPlaceholder(text));

    [Fact]
    public void KeepsARealBoardName()
    {
        var identity = new SystemIdentity("ASUS", "System Product Name", "ASUSTeK COMPUTER INC.", "PRIME B560M-A");
        Assert.Equal("ASUS PRIME B560M-A", BoardIdentity.DisplayName("ASUS PRIME B560M-A", identity));
    }

    [Fact]
    public void GenericNotebookUsesTheSystemManufacturerAndModel()
    {
        var identity = new SystemIdentity("ENOVA", "C141PP", "Notebook", "NL40_50CU");

        Assert.Equal("ENOVA C141PP", BoardIdentity.DisplayName("Notebook", identity));
        Assert.Equal("ENOVA", BoardBrands.Detect(BoardIdentity.BrandText("Notebook", identity))?.Name);
    }

    [Fact]
    public void FallsBackToTheBoardWhenTheSystemIsGenericToo()
    {
        var identity = new SystemIdentity("Notebook", "To Be Filled By O.E.M.", "Notebook", "NL40_50CU");
        Assert.Equal("NL40_50CU", BoardIdentity.DisplayName("Notebook", identity));
    }

    [Fact]
    public void FindsTheBrandInTheSystemDataWhenTheBoardHasNone()
    {
        var identity = new SystemIdentity("Positivo BGH", "BGH C-550", "Notebook", "Default string");
        Assert.Equal("Positivo BGH", BoardBrands.Detect(BoardIdentity.BrandText("Notebook", identity))?.Name);
    }

    [Fact]
    public void DoesNotRepeatTheManufacturer()
    {
        var identity = new SystemIdentity("ASUS", "System Product Name", "ASUSTeK COMPUTER INC.", "PRIME B560M-A");
        Assert.Equal("ASUS PRIME B560M-A ASUSTeK COMPUTER INC.", BoardIdentity.BrandText("ASUS PRIME B560M-A", identity));
    }
}
