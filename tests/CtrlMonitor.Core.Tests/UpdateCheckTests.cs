using CtrlMonitor.Core.Configuration;
using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.Core.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("v2.13.0", "2.13.0")]
    [InlineData("2.14.1", "2.14.1")]
    [InlineData("V3.0", "3.0.0")]
    public void ParseTag_ReadsVersions(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateCheck.ParseTag(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void ParseTag_RejectsNonVersions(string? tag)
    {
        Assert.Null(UpdateCheck.ParseTag(tag));
    }

    [Theory]
    [InlineData("2.14.0", "2.13.0", true)]
    [InlineData("2.13.0", "2.13.0", false)]
    [InlineData("2.12.0", "2.13.0", false)]
    [InlineData("2.13.0", "2.13.0.0", false)]
    [InlineData("2.13.1", "0.0.0", true)]
    public void IsNewer_ComparesMajorMinorBuild(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateCheck.IsNewer(Version.Parse(latest), Version.Parse(current)));
    }

    [Fact]
    public void ParseLatestRelease_ReadsTagAndUrl()
    {
        const string json = """
            {"tag_name":"v2.14.0","html_url":"https://github.com/AgusTinMd/Ctrl-Monitoring/releases/tag/v2.14.0","draft":false,"prerelease":false}
            """;

        ReleaseInfo release = UpdateCheck.ParseLatestRelease(json)!;

        Assert.Equal(new Version(2, 14, 0), release.Version);
        Assert.EndsWith("/releases/tag/v2.14.0", release.Url);
    }

    [Theory]
    [InlineData("""{"tag_name":"v2.14.0","html_url":"https://example.com/malware","draft":false}""")]
    [InlineData("""{"tag_name":"v2.14.0","html_url":"https://github.com/AgusTinMd/Ctrl-Monitoring/releases/tag/v2.14.0","prerelease":true}""")]
    [InlineData("""{"tag_name":"nightly","html_url":"https://github.com/AgusTinMd/Ctrl-Monitoring/releases/tag/nightly"}""")]
    [InlineData("no es json")]
    public void ParseLatestRelease_RejectsUnexpectedAnswers(string json)
    {
        Assert.Null(UpdateCheck.ParseLatestRelease(json));
    }

    [Fact]
    public void UpdateCheck_IsOffByDefault()
    {
        Assert.False(new AppConfig().CheckForUpdates);
    }
}
