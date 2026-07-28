using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Loopback;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionDynamicCallbackResolutionTests
{
    [Fact]
    public async Task OneSessionConnectionResolvesDifferentCallbackContractsAtSendTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        LoopbackTransport.CreatePair(out var unusedClientTransport, out var serverTransport);
        await using var unusedClientSession = new RpcSession(
            unusedClientTransport,
            new JsonRpcSerializer(),
            "unused-client");
        await using var serverSession = new RpcSession(
            serverTransport,
            new JsonRpcSerializer(),
            ConnectionId);
        var first = new FirstCallback();
        var second = new SecondCallback();

        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        await using var provider = services.BuildServiceProvider();
        var gameServer = provider.GetRequiredService<ILakonaGameServer>();
        var session = await gameServer.StartSessionAsync("player-a", ConnectionId, cancellationToken);
        provider.GetRequiredService<GameFrameworkConnectionRegistry>()
            .Set(ConnectionId, new RpcNotificationChannel(serverSession));
        provider.GetRequiredService<GameSessionCallbackProxyRegistry>()
            .Add(new CallbackBinder(first, second));

        var notifications = provider.GetRequiredService<IClientNotifications>();
        var firstStatus = notifications.ForSession<IFirstCallback>(session)
            .EnqueueGenerated(1, 1, nameof(IFirstCallback.Notify), "first");
        var secondStatus = notifications.ForSession<ISecondCallback>(session)
            .EnqueueGenerated(2, 1, nameof(ISecondCallback.Notify), "second");
        await ((ClientNotificationCommandRouter)provider.GetRequiredService<IClientNotificationCommandRouter>())
            .WaitForIdleAsync(session, cancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, firstStatus);
        Assert.Equal(ClientNotificationStatus.Accepted, secondStatus);
        Assert.Equal("first", first.Message);
        Assert.Equal("second", second.Message);
    }

    private const string ConnectionId = "dynamic-callback-connection";

    private interface IFirstCallback
    {
        void Notify(string message);
    }

    private interface ISecondCallback
    {
        void Notify(string message);
    }

    private sealed class FirstCallback : IFirstCallback
    {
        public string? Message { get; private set; }
        public void Notify(string message) => Message = message;
    }

    private sealed class SecondCallback : ISecondCallback
    {
        public string? Message { get; private set; }
        public void Notify(string message) => Message = message;
    }

    private sealed class CallbackBinder(FirstCallback first, SecondCallback second) : LakonaRpcServiceBinder
    {
        public override void Bind(LakonaGameServerRpcContext context)
        {
        }

        public override bool TryCreateCallback(
            Type callbackContractType,
            RpcNotificationChannel notifications,
            out object? callback)
        {
            callback = callbackContractType == typeof(IFirstCallback)
                ? first
                : callbackContractType == typeof(ISecondCallback)
                    ? second
                    : null;
            return callback is not null;
        }
    }
}
