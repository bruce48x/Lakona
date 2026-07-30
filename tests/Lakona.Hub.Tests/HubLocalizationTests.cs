using System.Globalization;
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
        Assert.Equal("Server", localization.Text.Server);
        Assert.Equal("Client", localization.Text.Client);
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
        Assert.Equal("前往 GitHub Issues", localization.Text.OpenGitHubIssues);
        Assert.Equal("服务端", localization.Text.Server);
        Assert.Equal("客户端", localization.Text.Client);
        Assert.Equal(HubLocalization.SimplifiedChinese, localization.SelectedLanguage);
        Assert.Contains(nameof(HubLocalization.Text), changedProperties);
    }

    [Fact]
    public void LanguageOptions_IncludeSimplifiedTraditionalAndEnglish()
    {
        var localization = new HubLocalization(HubLanguage.English);

        Assert.Equal(
            [HubLocalization.SimplifiedChinese, HubLocalization.TraditionalChinese, HubLocalization.English],
            localization.LanguageOptions);
    }

    [Fact]
    public void SetLanguage_SwitchesToTraditionalChineseImmediately()
    {
        var localization = new HubLocalization(HubLanguage.English);

        localization.SetLanguage(HubLanguage.TraditionalChinese);

        Assert.Equal("設定", localization.Text.Settings);
        Assert.Equal("開發環境", localization.Text.DevelopmentEnvironment);
        Assert.Equal("建立專案", localization.Text.CreateProject);
        Assert.Equal("檢查更新", localization.Text.CheckForUpdates);
        Assert.Equal(HubLocalization.TraditionalChinese, localization.SelectedLanguage);
    }

    [Theory]
    [InlineData("en-US", HubLanguage.English)]
    [InlineData("zh-CN", HubLanguage.SimplifiedChinese)]
    [InlineData("zh-Hans", HubLanguage.SimplifiedChinese)]
    [InlineData("zh-SG", HubLanguage.SimplifiedChinese)]
    [InlineData("zh-TW", HubLanguage.TraditionalChinese)]
    [InlineData("zh-HK", HubLanguage.TraditionalChinese)]
    [InlineData("zh-MO", HubLanguage.TraditionalChinese)]
    [InlineData("zh-Hant", HubLanguage.TraditionalChinese)]
    public void Constructor_DetectsLanguageLikeLakonaTool(string cultureName, HubLanguage expected)
    {
        var localization = new HubLocalization(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, localization.Language);
    }

    [Fact]
    public void TraditionalChinese_LocalizesEveryStaticTextProperty()
    {
        var traditional = HubText.For(HubLanguage.TraditionalChinese);
        var english = HubText.For(HubLanguage.English);
        var properties = typeof(HubText).GetProperties()
            .Where(property => property.PropertyType == typeof(string));

        foreach (var property in properties)
        {
            Assert.NotEqual(property.GetValue(english), property.GetValue(traditional));
        }
    }
}
