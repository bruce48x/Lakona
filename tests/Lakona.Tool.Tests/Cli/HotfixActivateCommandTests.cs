using System.Collections.Concurrent;
using System.Text.Json;
using Lakona.Tool.Cli.Commands.Hotfix;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Tool.Tests.Cli;

public sealed class HotfixActivateCommandTests
{
    [Theory]
    [InlineData("v2", "--expected-current-version")]
    [InlineData("v2", "--expected-current-version", "")]
    [InlineData("v2", "--expected-current-version", "   ")]
    [InlineData("v2", "--expected-current-version", "--unknown")]
    [InlineData("v2", "--expected-current-version", "v1", "--expected-current-version", "v1")]
    [InlineData("v2", "--expected-current-version", "v1", "--expected-current-version", "v3")]
    [InlineData("v2", "--expected-current-versoin", "v1")]
    [InlineData("v2", "--unknown")]
    [InlineData("v2", "extra")]
    [InlineData("v2", "--server")]
    [InlineData("v2", "--server", "")]
    [InlineData("v2", "--server", "http://localhost:20080")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--expected-current-version", "v1")]
    public async Task Invalid_arguments_report_usage_error_without_sending_request(params string[] arguments)
    {
        await using var server = await CaptureServer.StartAsync();
        var terminal = new RecordingTerminal();
        // Keep a valid capture endpoint first, so the old parser also remains local
        // to this test when it ignores malformed or duplicate options.
        var exitCode = await new CliApplication(terminal: terminal).RunAsync(
            ["hotfix", "activate", arguments[0], "--server", server.Url, .. arguments.Skip(1)])
            .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Empty(server.Requests);
        Assert.Equal(1, exitCode);
        Assert.NotEmpty(terminal.Errors);
        Assert.Empty(terminal.Output);
    }

    [Fact]
    public async Task Missing_version_reports_usage_error()
    {
        var terminal = new RecordingTerminal();

        var exitCode = await new CliApplication(terminal: terminal).RunAsync(["hotfix", "activate"]);

        Assert.Equal(1, exitCode);
        Assert.Contains(terminal.Errors, error => error.Contains("Missing hotfix version", StringComparison.Ordinal));
        Assert.Empty(terminal.Output);
    }

    [Theory]
    [InlineData()]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--expected-current-version", "v1")]
    public async Task Missing_server_value_is_rejected_before_request(params string[] trailingArguments)
    {
        var command = new HotfixActivateCommand(new RecordingTerminal());
        // A regression must not contact the default admin endpoint. Argument
        // validation should still finish before any canceled HTTP operation.
        var exception = await Assert.ThrowsAsync<CliUsageException>(() => command.RunAsync(
            ["v2", "--server", .. trailingArguments], new CancellationToken(canceled: true)));

        Assert.Contains("Missing value for --server", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("v1", false)]
    [InlineData("v1", true)]
    public async Task Valid_arguments_preserve_activation_request(string? expectedVersion, bool serverLast)
    {
        await using var server = await CaptureServer.StartAsync();
        var terminal = new RecordingTerminal();
        string[] precondition = expectedVersion is null ? [] : ["--expected-current-version", expectedVersion];
        string[] options = serverLast
            ? [.. precondition, "--server", server.Url]
            : ["--server", server.Url, .. precondition];

        var exitCode = await new CliApplication(terminal: terminal).RunAsync(["hotfix", "activate", "v2", .. options])
            .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Empty(terminal.Errors);
        Assert.Equal("{}", Assert.Single(terminal.Output));
        var request = Assert.Single(server.Requests);
        Assert.Equal("v2", request.GetProperty("version").GetString());
        Assert.Equal("v2", request.GetProperty("operationId").GetString());
        Assert.Equal(expectedVersion, request.GetProperty("expectedCurrentVersion").GetString());
    }

    private sealed class CaptureServer(WebApplication app) : IAsyncDisposable
    {
        public ConcurrentQueue<JsonElement> Requests { get; } = new();
        public string Url => app.Urls.Single();

        public static async Task<CaptureServer> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var server = new CaptureServer(app);
            app.MapPost("/_lakona/hotfix/activate", async (HttpContext context) =>
            {
                server.Requests.Enqueue(await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted));
                await context.Response.WriteAsync("{}", context.RequestAborted);
            });
            try
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                return server;
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync() => app.DisposeAsync();
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
}
