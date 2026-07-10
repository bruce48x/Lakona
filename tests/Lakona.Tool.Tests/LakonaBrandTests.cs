using System.Text.RegularExpressions;
using Xunit;

namespace Lakona.Tool.Tests;

public sealed class LakonaBrandTests
{
    private readonly ITestOutputHelper _output;

    public LakonaBrandTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Print_ShowsBannerInTestOutputPanel()
    {
        var writer = new StringWriter { NewLine = "\n" };
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(writer);
            LakonaBrand.Print();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Write captured output to VS Code 的"测试输出"面板
        foreach (var line in writer.ToString().Split('\n'))
            _output.WriteLine(line.TrimEnd('\r'));

        var rendered = Regex.Replace(
            writer.ToString(),
            "\\x1b\\[[0-9;]*m",
            "");
        const string expected =
            "╔══════════════════════════════╗\n" +
            "║        /\\_/\\                 ║\n" +
            "║       ( oᴥo )     Lakona     ║\n" +
            "║        U___U                 ║\n" +
            "╚══════════════════════════════╝\n";

        Assert.Equal(expected, rendered);
    }
}
