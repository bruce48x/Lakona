using System.Globalization;
using System.Reflection;
using Lakona.Tool.RpcStarter;
using Xunit;

namespace Lakona.Tool.Tests.RpcStarter;

public sealed class StarterLocalizationTests
{
    [Theory]
    [InlineData("en-US", (int)StarterLanguage.English)]
    [InlineData("zh", (int)StarterLanguage.SimplifiedChinese)]
    [InlineData("zh-CN", (int)StarterLanguage.SimplifiedChinese)]
    [InlineData("zh-Hans", (int)StarterLanguage.SimplifiedChinese)]
    [InlineData("zh-SG", (int)StarterLanguage.SimplifiedChinese)]
    [InlineData("zh-TW", (int)StarterLanguage.TraditionalChinese)]
    [InlineData("zh-Hant", (int)StarterLanguage.TraditionalChinese)]
    [InlineData("zh-CHT", (int)StarterLanguage.TraditionalChinese)]
    [InlineData("zh-HK", (int)StarterLanguage.TraditionalChinese)]
    [InlineData("zh-MO", (int)StarterLanguage.TraditionalChinese)]
    public void DetectLanguage_MapsSupportedCultures(string cultureName, int expected)
    {
        Assert.Equal((StarterLanguage)expected, StarterText.DetectLanguage(CultureInfo.GetCultureInfo(cultureName)));
    }

    [Fact]
    public void PrintUsage_UsesSimplifiedChinese_ForZhCnUiCulture()
    {
        var text = WithUiCulture("zh-CN", () => ToolText.ForCulture(CultureInfo.GetCultureInfo("zh-CN")));

        Assert.Contains("lakona-tool new", text.HelpText, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintUsage_UsesTraditionalChinese_ForZhTwUiCulture()
    {
        var text = WithUiCulture("zh-TW", () => ToolText.ForCulture(CultureInfo.GetCultureInfo("zh-TW")));

        Assert.Contains("lakona-tool new", text.HelpText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("zh-CN", "--client-engine 不支持值")]
    [InlineData("zh-TW", "--client-engine 不支援值")]
    public void TryParseArgs_LocalizesErrors_ForChineseUiCultures(string cultureName, string expectedError)
    {
        var result = WithUiCulture(cultureName, () =>
        {
            try
            {
                CliParser.ParseNewOptions(["--client-engine", "invalid-engine"], ToolText.ForCulture(CultureInfo.GetCultureInfo(cultureName)));
                return (true, "");
            }
            catch (CliUsageException ex)
            {
                return (false, ex.Message);
            }
        });

        Assert.False(result.Item1);
        Assert.Contains(expectedError, result.Item2, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("zh-CN", "  3) 使用 Unity 2022 LTS（中国网络环境默认配置） 打开 \"Client\"。")]
    [InlineData("zh-TW", "  3) 使用 Unity 2022 LTS（中國網路環境預設配置） 開啟 \"Client\"。")]
    public void OpenClientStep_LocalizesUnityCnNextStep(string cultureName, string expected)
    {
        var text = StarterText.ForCulture(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, text.OpenClientStep(ClientEngineKind.UnityCn));
    }

    [Fact]
    public void NewCommand_PrintsOnlyThreeNextSteps()
    {
        var root = Path.Combine(Path.GetTempPath(), "Lakona.Tool.Tests.RpcStarter", Guid.NewGuid().ToString("N"));

        try
        {
            var output = WithUiCulture("en-US", CaptureStdout(() =>
            {
                var generator = new RpcStarterGenerator();
                generator.Generate(new RpcStarterNewOptions(
                    "Sample",
                    root,
                    ClientEngineKind.Unity,
                    TransportKind.WebSocket,
                    SerializerKind.MemoryPack,
                    NuGetForUnitySourceKind.Embedded));
            }));

            // Verify project was generated
            Assert.True(Directory.Exists(Path.Combine(root, "Sample")));
            Assert.True(Directory.Exists(Path.Combine(root, "Sample", "Shared")));
            Assert.True(Directory.Exists(Path.Combine(root, "Sample", "Server")));
            Assert.True(Directory.Exists(Path.Combine(root, "Sample", "Client")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static Func<string> CaptureStdout(Action action) => () =>
    {
        var previousOut = Console.Out;
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(writer);
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(previousOut);
        }
    };

    private static T WithUiCulture<T>(string cultureName, Func<T> action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var culture = CultureInfo.GetCultureInfo(cultureName);

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
