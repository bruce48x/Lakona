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
        Assert.Equal(["publish", "Server.App.csproj"], startInfo.ArgumentList);
    }
}
