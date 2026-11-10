using System.ComponentModel;
using System.Diagnostics;
using Lakona.Hub.Sdk;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubSdkDiscoveryTests
{
    [Theory]
    [InlineData("win-x64", true)]
    [InlineData("osx-arm64", true)]
    [InlineData("linux-x64", true)]
    [InlineData("win-x64", false)]
    [InlineData("osx-arm64", false)]
    [InlineData("linux-x64", false)]
    public async Task Missing_candidates_allow_remaining_locations_to_be_inspected(string platform, bool installed)
    {
        var candidates = HubRuntimeInfo.SystemDotNetCandidates(platform);
        var expected = installed ? candidates[^1] : null;
        var attempted = new List<string>();
        var factory = new DelegateFactory(info =>
        {
            attempted.Add(info.FileName);
            if (info.FileName == expected) return new CompletedProcess();
            throw new Win32Exception(2);
        });
        using var http = new HttpClient();
        var manager = new HubSdkManager(http, new HubSdkCommandRunner(factory), platform, MissingPath());

        var result = await manager.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(candidates, attempted);
        Assert.Equal(installed, result.IsReady);
        Assert.Equal(expected, result.ExecutablePath);
        Assert.Equal(installed ? HubSdkSource.System : HubSdkSource.None, result.Source);
        Assert.Equal(installed ? "10.0.202" : null, result.Version);
    }

    [Fact]
    public async Task Real_missing_executable_returns_failed_attempt()
    {
        var result = await new HubSdkCommandRunner().RunAsync(
            MissingPath(), "--list-sdks", TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task Cancellation_during_missing_candidate_does_not_try_next_location()
    {
        using var cancellation = new CancellationTokenSource();
        var starts = 0;
        var factory = new DelegateFactory(_ =>
        {
            starts++;
            cancellation.Cancel();
            throw new Win32Exception(2);
        });
        using var http = new HttpClient();
        var manager = new HubSdkManager(http, new HubSdkCommandRunner(factory), "win-x64", MissingPath());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.InspectAsync(cancellation.Token));
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task Already_canceled_command_does_not_start_process()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var starts = 0;
        var runner = new HubSdkCommandRunner(new DelegateFactory(_ =>
        {
            starts++;
            return new CompletedProcess();
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync("dotnet", "--list-sdks", cancellation.Token));
        Assert.Equal(0, starts);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(193)]
    public async Task Other_native_start_failures_are_not_reported_as_missing(int code)
    {
        var failure = new Win32Exception(code);
        var runner = new HubSdkCommandRunner(new DelegateFactory(_ => throw failure));
        var error = await Assert.ThrowsAsync<Win32Exception>(() =>
            runner.RunAsync("dotnet", "--list-sdks", TestContext.Current.CancellationToken));
        Assert.Same(failure, error);
    }

    private static string MissingPath() => Path.Combine(Path.GetTempPath(), "Lakona-missing-" + Guid.NewGuid().ToString("N"), "dotnet");

    private sealed class DelegateFactory(Func<ProcessStartInfo, IHubSdkProcess?> start) : IHubSdkProcessFactory
    {
        public IHubSdkProcess? Start(ProcessStartInfo startInfo) => start(startInfo);
    }

    private sealed class CompletedProcess : IHubSdkProcess
    {
        public TextReader StandardOutput { get; } = new StringReader("10.0.202 [sdk]");
        public TextReader StandardError { get; } = new StringReader("");
        public int ExitCode => 0;
        public bool HasExited => true;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Kill(bool entireProcessTree) => throw new InvalidOperationException("Already exited.");
        public void Dispose()
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }
}
