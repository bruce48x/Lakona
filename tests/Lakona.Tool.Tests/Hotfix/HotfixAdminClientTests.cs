using Lakona.Tool.Hotfix;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.HotfixAdmin;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Tool.Tests.Hotfix;

public sealed class HotfixAdminClientTests
{
    [Fact]
    public async Task Real_manager_failure_reaches_cli_through_registered_admin_routes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-admin-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(directory, "Broken.dll"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Broken.dll"), "invalid assembly", TestContext.Current.CancellationToken);
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            var runtime = new LakonaGameRuntimeOptions
            {
                Health = new LakonaHealthOptions { Enabled = false },
                Management = new LakonaManagementOptions
                {
                    Http = new LakonaManagementHttpOptions { Host = "127.0.0.1", Port = port },
                    Admin = new LakonaManagementAdminOptions { Enabled = true }
                }
            };
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(runtime);
            builder.Services.AddSingleton<IHotfixManager>(manager);
            builder.Services.AddLakonaGameHotfixAdmin(options => options.HotfixRoot = directory);
            LakonaHttpHosting.Configure(builder, runtime);
            await using var app = builder.Build();
            LakonaHttpHosting.Map(app);
            await app.StartAsync(TestContext.Current.CancellationToken);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var client = new HotfixAdminClient(http);
            var exception = await Assert.ThrowsAsync<HotfixAdminRequestException>(() => client.PostAsync(
                $"http://127.0.0.1:{port}", "/_lakona/hotfix/reload", new { }, TestContext.Current.CancellationToken));

            Assert.Contains("HOTFIX_RELOAD_FAILED", exception.Message);
            Assert.Contains("Stage: reload", exception.Message);
            Assert.Contains("Next step:", exception.Message);
            Assert.Contains("Loaded version: none", exception.Message);
            Assert.Contains("Correlation ID:", exception.Message);
            Assert.DoesNotContain("Local admin endpoint failed", exception.Message);
            var status = await app.Services.GetRequiredService<HotfixAdminController>().GetStatusAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(status.LastOperationFailure);
            Assert.Contains(status.LastOperationFailure.Message, exception.Message);
            Assert.Contains(status.LastOperationFailure.CorrelationId, exception.Message);
            Assert.Equal(0, manager.Current.DispatchTableVersion);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var terminal = new RecordingTerminal();
            var exitCode = await new CliApplication(terminal: terminal).RunAsync(
                ["hotfix", "rollback", "--server", $"http://127.0.0.1:{port}"]).WaitAsync(deadline.Token);
            Assert.Equal(1, exitCode);
            var error = Assert.Single(terminal.Errors);
            Assert.Contains("HOTFIX_NO_PREVIOUS_VERSION", error);
            Assert.Contains("Next step: Install and activate", error);
            Assert.Empty(terminal.Output);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await manager.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[]")]
    [InlineData("{\"diagnostic\":{\"code\":42}}")]
    public async Task Unknown_error_formats_preserve_response_body(string body)
    {
        using var http = new HttpClient(new StaticResponseHandler(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
        {
            Content = new StringContent(body)
        }));
        var exception = await Assert.ThrowsAsync<HotfixAdminRequestException>(() => new HotfixAdminClient(http).GetAsync(
            "http://localhost:20090", "/_lakona/hotfix/status", TestContext.Current.CancellationToken));
        Assert.Contains(body, exception.Message);
    }

    private sealed class RecordingTerminal : ICliTerminal
    {
        public List<string> Output { get; } = [];
        public List<string> Errors { get; } = [];
        public bool IsInputRedirected => true;
        public bool IsOutputRedirected => true;
        public void Write(string value) => Output.Add(value);
        public void WriteLine(string value) => Output.Add(value);
        public void WriteErrorLine(string value) => Errors.Add(value);
        public string? ReadLine() => null;
    }

    [Theory]
    [InlineData("http://127.0.0.1:20090")]
    [InlineData("http://localhost:20090")]
    public void Hotfix_admin_commands_accept_loopback_server(string url)
    {
        Assert.True(HotfixAdminClient.IsLoopbackServer(url));
    }

    [Theory]
    [InlineData("http://10.0.0.5:20090")]
    [InlineData("https://game.example.com:20090")]
    public void Hotfix_admin_commands_reject_non_loopback_server(string url)
    {
        Assert.False(HotfixAdminClient.IsLoopbackServer(url));
    }

    [Fact]
    public async Task PostAsync_throws_when_server_returns_error_status()
    {
        using var http = new HttpClient(new StaticResponseHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{ "error": "boom" }""")
            }));
        var client = new HotfixAdminClient(http);

        var exception = await Assert.ThrowsAsync<HotfixAdminRequestException>(
            async () => await client.PostAsync(
                "http://127.0.0.1:20090",
                "/_lakona/hotfix/activate",
                new { version = "v1" },
                TestContext.Current.CancellationToken));

        Assert.Contains("400", exception.Message, StringComparison.Ordinal);
        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage response;

        public StaticResponseHandler(HttpResponseMessage response)
        {
            this.response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}
