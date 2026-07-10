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

        Assert.Contains("/\\_/\\", writer.ToString(), StringComparison.Ordinal);
    }
}
