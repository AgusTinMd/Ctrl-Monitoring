using System.Globalization;
using System.Text.RegularExpressions;
using CtrlMonitor.Core.Configuration;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.Core.Tests;

public partial class LocalizationTests
{
    [GeneratedRegex(@"\{(\d+)(?:[:,][^}]*)?\}")]
    private static partial Regex Placeholder();

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();

        foreach (LanguageInfo language in AppLanguage.Supported)
            data.Add(language.Code);

        return data;
    }

    [Fact]
    public void EverySupportedLanguageHasTranslations()
    {
        foreach (LanguageInfo language in AppLanguage.Supported)
            Assert.True(Translations.All.ContainsKey(language.Code), language.Code);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Language_HasExactlyTheEnglishKeys(string code)
    {
        IReadOnlyDictionary<string, string> english = Translations.For("en");
        IReadOnlyDictionary<string, string> texts = Translations.All[code];

        Assert.Empty(english.Keys.Except(texts.Keys));
        Assert.Empty(texts.Keys.Except(english.Keys));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Language_HasNoEmptyTexts(string code)
    {
        foreach ((string key, string value) in Translations.All[code])
            Assert.False(string.IsNullOrWhiteSpace(value), $"{code}: {key} vacío");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Language_UsesSamePlaceholdersAsEnglish(string code)
    {
        IReadOnlyDictionary<string, string> english = Translations.For("en");

        foreach ((string key, string value) in Translations.All[code])
        {
            Assert.Equal(Placeholders(english[key]), Placeholders(value));

            // Que string.Format no falle con los argumentos esperados.
            object[] args = Enumerable.Range(0, 4).Select(i => (object)(i + 1.5)).ToArray();
            _ = string.Format(CultureInfo.InvariantCulture, value, args);
        }
    }

    [Theory]
    [InlineData("es-AR", "es")]
    [InlineData("es-ES", "es")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "en")]
    [InlineData("zh-CN", "en")]
    public void Resolve_Auto_FollowsWindowsLanguage(string windows, string expected)
    {
        Assert.Equal(expected, AppLanguage.Resolve(AppLanguage.Auto, new CultureInfo(windows)));
    }

    [Theory]
    [InlineData("en", "es-AR", "en")]
    [InlineData("pt-br", "en-US", "pt-BR")]
    [InlineData("ES", "pt-BR", "es")]
    public void Resolve_ExplicitSetting_IgnoresWindows(string setting, string windows, string expected)
    {
        Assert.Equal(expected, AppLanguage.Resolve(setting, new CultureInfo(windows)));
    }

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("klingon", "auto")]
    [InlineData(" pt-br ", "pt-BR")]
    [InlineData("EN", "en")]
    public void NormalizeSetting_KeepsOnlyValidValues(string? setting, string expected)
    {
        Assert.Equal(expected, AppLanguage.NormalizeSetting(setting));
    }

    [Fact]
    public void AppConfig_NormalizesLanguage()
    {
        var config = new AppConfig { Language = "fr" };
        config.Normalize();

        Assert.Equal(AppLanguage.Auto, config.Language);
    }

    [Fact]
    public void Get_MissingKey_ReturnsKey()
    {
        Assert.Equal("CPU", Loc.Instance.Get("CPU"));
    }

    private static string[] Placeholders(string text) =>
        Placeholder().Matches(text)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
}
