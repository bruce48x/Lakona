using System.Text;
using System.Text.Json;
using Lakona.Game.Server.Http;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Users;
using Server.Hotfix.Operations;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarOperationsHttpTests
{
    [Fact]
    public async Task Internal_user_query_returns_persisted_profile_without_credentials()
    {
        var store = new InMemoryUserStore();
        await store.SaveAsync(
            new PersistedUser
            {
                UserId = "alice",
                PasswordHash = "must-not-leak",
                LoginCount = 7,
                CreatedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                LastLoginAtUtc = new DateTime(2026, 7, 25, 6, 7, 8, DateTimeKind.Utc),
                WinCount = 3,
                VictoryPoints = 42
            },
            TestContext.Current.CancellationToken);
        var service = new AgarOperationsHttpService(store);
        await using var services = new ServiceCollection().BuildServiceProvider();

        var response = await service.GetUserAsync(
            CreateCall("alice", services, TestContext.Current.CancellationToken));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.ContentType);
        using var user = JsonDocument.Parse(response.Body);
        Assert.Equal("alice", user.RootElement.GetProperty("Account").GetString());
        Assert.Equal(7, user.RootElement.GetProperty("LoginCount").GetInt32());
        Assert.Equal(3, user.RootElement.GetProperty("WinCount").GetInt32());
        Assert.Equal(42, user.RootElement.GetProperty("VictoryPoints").GetInt32());
        Assert.DoesNotContain(
            "must-not-leak",
            Encoding.UTF8.GetString(response.Body.Span),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Internal_user_query_returns_not_found_for_unknown_account()
    {
        var service = new AgarOperationsHttpService(new InMemoryUserStore());
        await using var services = new ServiceCollection().BuildServiceProvider();

        var response = await service.GetUserAsync(
            CreateCall("missing", services, TestContext.Current.CancellationToken));

        Assert.Equal(404, response.StatusCode);
        using var error = JsonDocument.Parse(response.Body);
        Assert.Equal(
            "user_not_found",
            error.RootElement.GetProperty("Code").GetString());
    }

    [Fact]
    public async Task Internal_user_query_rejects_invalid_account()
    {
        var service = new AgarOperationsHttpService(new InMemoryUserStore());
        await using var services = new ServiceCollection().BuildServiceProvider();

        var response = await service.GetUserAsync(
            CreateCall(new string('a', 129), services, TestContext.Current.CancellationToken));

        Assert.Equal(400, response.StatusCode);
        using var error = JsonDocument.Parse(response.Body);
        Assert.Equal(
            "invalid_account",
            error.RootElement.GetProperty("Code").GetString());
    }

    private static LakonaHttpCall CreateCall(
        string account,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return new LakonaHttpCall(
            new LakonaHttpRequest(
                ReadOnlyMemory<byte>.Empty,
                new Dictionary<string, string[]>(),
                new Dictionary<string, string[]>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account"] = account
                },
                AuthenticatedName: null,
                RemoteEndpoint: null,
                TraceIdentifier: "agar-operations-test"),
            services,
            cancellationToken);
    }
}
