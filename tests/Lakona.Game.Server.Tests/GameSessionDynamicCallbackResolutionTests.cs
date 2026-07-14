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

        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        await using var provider = services.BuildServiceProvider();
        var gameServer = provider.GetRequiredService<ILakonaGameServer>();
        var session = await gameServer.StartSessionAsync("player-a", ConnectionId, cancellationToken);
        provider.GetRequiredService<GameFrameworkConnectionRegistry>().Set(serverSession);
        provider.GetRequiredService<GameSessionCallbackProxyRegistry>()
            .Add(new CallbackBinder(first, second));

        var notifications = provider.GetRequiredService<IClientNotifications>();
        var firstStatus = await notifications.ForSession<IFirstCallback>(session)
            .DispatchGeneratedAsync(1, 1, nameof(IFirstCallback.Notify), "first", cancellationToken);
        var secondStatus = await notifications.ForSession<ISecondCallback>(session)
            .DispatchGeneratedAsync(2, 1, nameof(ISecondCallback.Notify), "second", cancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, firstStatus);
        Assert.Equal(ClientNotificationStatus.Delivered, secondStatus);
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
            RpcSession session,
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
