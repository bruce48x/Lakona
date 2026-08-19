using System.Text;
using Lakona.ProjectSystem.Packaging.Server;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Packaging.Server;

public sealed class DotNetCommandRunnerTests
{
    [Fact]
    public void Packaging_process_runs_without_creating_a_console_window()
    {
        var runner = new DotNetCommandRunner("dotnet.exe");

        var startInfo = runner.CreateStartInfo("C:/project", ["publish", "Server.App.csproj"]);

        Assert.Equal("dotnet.exe", startInfo.FileName);
        Assert.Equal("C:/project", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(Encoding.UTF8, startInfo.StandardOutputEncoding);
        Assert.Equal(Encoding.UTF8, startInfo.StandardErrorEncoding);
        Assert.Equal(["publish", "Server.App.csproj"], startInfo.ArgumentList);
    }

    [Fact]
    public async Task Packaging_process_preserves_chinese_text_from_dotnet()
    {
        var runner = new DotNetCommandRunner();

        var result = await runner.RunAsync(
            AppContext.BaseDirectory,
            ["命令不存在"],
            TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("命令不存在", result.StandardOutput + result.StandardError, StringComparison.Ordinal);
    }
}
