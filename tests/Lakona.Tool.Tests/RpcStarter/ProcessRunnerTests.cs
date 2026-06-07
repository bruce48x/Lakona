using Lakona.Tool.RpcStarter;
using Xunit;

namespace Lakona.Tool.Tests.RpcStarter;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunProcessAsync_FailedCommand_IncludesStdoutAndStderr()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProcessRunner.RunProcessAsync(
                "pwsh",
                Directory.GetCurrentDirectory(),
                "-NoProfile -Command \"[Console]::Out.Write('out-text'); [Console]::Error.Write('err-text'); exit 7\"",
                TimeSpan.FromSeconds(10)));

        Assert.Contains("out-text", ex.Message);
        Assert.Contains("err-text", ex.Message);
    }

    [Fact]
    public async Task RunProcessAsync_Timeout_KillsAndThrowsTimeoutException()
    {
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            ProcessRunner.RunProcessAsync(
                "pwsh",
                Directory.GetCurrentDirectory(),
                "-NoProfile -Command \"Start-Sleep -Seconds 10\"",
                TimeSpan.FromMilliseconds(200)));

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
