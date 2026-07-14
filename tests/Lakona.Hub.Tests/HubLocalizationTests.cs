using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubLocalizationTests
{
    [Fact]
    public void Constructor_UsesExplicitLanguageInsteadOfSystemLanguage()
    {
        var localization = new HubLocalization(HubLanguage.English);

        Assert.Equal(HubLanguage.English, localization.Language);
        Assert.Equal("Settings", localization.Text.Settings);
        Assert.Equal("Development environment", localization.Text.DevelopmentEnvironment);
    }

    [Fact]
    public void SetLanguage_SwitchesBetweenChineseAndEnglishImmediately()
    {
        var localization = new HubLocalization(HubLanguage.English);
        var changedProperties = new List<string?>();
        localization.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        localization.SetLanguage(HubLanguage.SimplifiedChinese);

        Assert.Equal("设置", localization.Text.Settings);
        Assert.Equal("开发环境", localization.Text.DevelopmentEnvironment);
        Assert.Equal(HubLocalization.SimplifiedChinese, localization.SelectedLanguage);
        Assert.Contains(nameof(HubLocalization.Text), changedProperties);
    }
}
