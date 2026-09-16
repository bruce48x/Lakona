using Lakona.Tool.Hotfix;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.HotfixAdmin;
using Lakona.Game.Server.LocalAdmin;
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
            var services = new ServiceCollection();
            services.AddSingleton<IHotfixManager>(manager);
            services.AddLakonaGameHotfixAdmin(options => options.HotfixRoot = directory);
            using var provider = services.BuildServiceProvider();
            var router = new LakonaLocalAdminRouter(provider.GetServices<ILakonaLocalAdminRoute>());
            using var http = new HttpClient(new RouterHandler(router));
            var client = new HotfixAdminClient(http);
            var exception = await Assert.ThrowsAsync<HotfixAdminRequestException>(() => client.PostAsync(
                "http://127.0.0.1:20090", "/_lakona/hotfix/reload", new { }, TestContext.Current.CancellationToken));

            Assert.Contains("HOTFIX_RELOAD_FAILED", exception.Message);
            Assert.Contains("Stage: reload", exception.Message);
            Assert.Contains("Next step:", exception.Message);
            Assert.Contains("Loaded version: none", exception.Message);
            Assert.Contains("Correlation ID:", exception.Message);
            Assert.DoesNotContain("Local admin endpoint failed", exception.Message);
            var status = await provider.GetRequiredService<HotfixAdminController>().GetStatusAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(status.LastOperationFailure);
            Assert.Contains(status.LastOperationFailure.Message, exception.Message);
            Assert.Contains(status.LastOperationFailure.CorrelationId, exception.Message);
            Assert.Equal(0, manager.Current.DispatchTableVersion);

            // Exercise the actual CLI entry and HTTP client over a loopback socket.
            // The small test adapter returns the real registered rollback route response.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            async Task ServeAsync()
            {
                using var connection = await listener.AcceptTcpClientAsync(deadline.Token);
                await using var stream = connection.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(deadline.Token);
                Assert.Equal("POST /_lakona/hotfix/rollback HTTP/1.1", requestLine);
                while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 }) { }
                var response = await router.RouteAsync(new LakonaLocalAdminRequest("POST", "/_lakona/hotfix/rollback", Stream.Null, true), deadline.Token);
                var content = System.Text.Encoding.UTF8.GetBytes(response.Body);
                var header = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 {response.StatusCode} Bad Request\r\nContent-Type: application/json\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, deadline.Token);
                await stream.WriteAsync(content, deadline.Token);
            }
            var serving = ServeAsync();
            var terminal = new RecordingTerminal();
            var exitCode = await new CliApplication(terminal: terminal).RunAsync(
                ["hotfix", "rollback", "--server", $"http://127.0.0.1:{port}"]).WaitAsync(deadline.Token);
            await serving;
            Assert.Equal(1, exitCode);
            var error = Assert.Single(terminal.Errors);
            Assert.Contains("HOTFIX_NO_PREVIOUS_VERSION", error);
            Assert.Contains("Next step: Install and activate", error);
            Assert.Empty(terminal.Output);
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

    private sealed class RouterHandler(LakonaLocalAdminRouter router) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? Stream.Null : await request.Content.ReadAsStreamAsync(cancellationToken);
            var response = await router.RouteAsync(new LakonaLocalAdminRequest(request.Method.Method,
                request.RequestUri!.AbsolutePath, body, true), cancellationToken);
            return new HttpResponseMessage((System.Net.HttpStatusCode)response.StatusCode)
            {
                Content = new StringContent(response.Body, System.Text.Encoding.UTF8, response.ContentType)
            };
        }
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
