using Lakona.Tool.Hotfix;
using Xunit;

namespace Lakona.Tool.Tests.Hotfix;

public sealed class HotfixAdminClientTests
{
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
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
