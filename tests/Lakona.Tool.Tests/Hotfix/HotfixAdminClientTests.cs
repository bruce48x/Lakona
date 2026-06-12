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
}
