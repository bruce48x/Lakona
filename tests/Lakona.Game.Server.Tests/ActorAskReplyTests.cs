using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests;

public sealed class ActorAskReplyTests
{
    private static readonly ActorId Id = ActorId.From("ask/reply");

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("reply", false)]
    [InlineData("reply", true)]
    public Task Reference_reply_matches_dynamic_local_and_remote_conversion(string? reply, bool asynchronous) =>
        AssertReplyAsync(reply, asynchronous);

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(42, false)]
    [InlineData(42, true)]
    public Task Nullable_value_reply_matches_dynamic_local_and_remote_conversion(int? reply, bool asynchronous) =>
        AssertReplyAsync(reply, asynchronous);

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(42, false)]
    [InlineData(42, true)]
    public Task Value_reply_matches_dynamic_local_and_remote_conversion(int reply, bool asynchronous) =>
        AssertReplyAsync(reply, asynchronous);

    private static async Task AssertReplyAsync<TResult>(TResult reply, bool asynchronous)
    {
        var token = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<ActorActivationCatalog>();
        await catalog.CreateAsync<ReplyActor>(Id, token);
        var runtime = provider.GetRequiredService<IActorRuntime>();

        ValueTask<TResult> Read() => asynchronous ? ReadLater() : new ValueTask<TResult>(reply);
        async ValueTask<TResult> ReadLater()
        {
            await Task.Yield();
            return reply;
        }

        // Local calls use the real mailbox; the remote comparison covers reply conversion, not wire serialization.
        var dynamicLocal = await runtime.AskAsync(typeof(ReplyActor), Id,
            async (_, _) => await Read(), token);
        var remote = GetRemoteReply<TResult>(RemoteActorInvocationResult.Replied(reply));
        var typedLocal = await runtime.AskAsync<ReplyActor, TResult>(Id, (_, _) => Read(), token);

        Assert.Equal(reply, typedLocal);
        Assert.Equal((object?)reply, dynamicLocal);
        Assert.Equal(reply, remote);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nullable_reply_preserves_handler_failure(bool asynchronous)
    {
        var token = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<ActorActivationCatalog>();
        await catalog.CreateAsync<ReplyActor>(Id, token);
        var expected = new InvalidOperationException("handler failed");
        ValueTask<string?> Fail() => asynchronous ? FailLater() : throw expected;
        async ValueTask<string?> FailLater()
        {
            await Task.Yield();
            throw expected;
        }

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await catalog.AskAsync<ReplyActor, string?>(Id, (_, _) => Fail(), token));
        Assert.Same(expected, actual);
        Assert.Null(await catalog.AskAsync<ReplyActor, string?>(Id,
            (_, _) => new ValueTask<string?>((string?)null), token));
    }

    [Fact]
    public async Task Nullable_reply_preserves_handler_cancellation()
    {
        var token = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<ActorActivationCatalog>();
        await catalog.CreateAsync<ReplyActor>(Id, token);
        using var handler = new CancellationTokenSource();
        handler.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await catalog.AskAsync<ReplyActor, int?>(Id, async (_, _) =>
            {
                await Task.Yield();
                handler.Token.ThrowIfCancellationRequested();
                return null;
            }, token));
        Assert.Equal(handler.Token, error.CancellationToken);
        Assert.Null(await catalog.AskAsync<ReplyActor, int?>(Id,
            (_, _) => new ValueTask<int?>((int?)null), token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nullable_reply_preserves_caller_cancellation(bool afterAdmission)
    {
        var token = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<ActorActivationCatalog>();
        await catalog.CreateAsync<ReplyActor>(Id, token);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = 0;
        if (!afterAdmission) caller.Cancel();
        try
        {
            var call = catalog.AskAsync<ReplyActor, string?>(Id, async (_, executionToken) =>
            {
                Assert.False(executionToken.CanBeCanceled);
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                Interlocked.Increment(ref executed);
                return null;
            }, caller.Token).AsTask();
            if (afterAdmission)
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                caller.Cancel();
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            Assert.Equal(afterAdmission, entered.Task.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref executed));
        }
        finally
        {
            release.TrySetResult();
        }

        // A subsequent turn proves accepted work drained, and a late null reply did not poison the mailbox.
        Assert.Null(await catalog.AskAsync<ReplyActor, string?>(Id,
            (_, _) => new ValueTask<string?>((string?)null), token));
        Assert.Equal(afterAdmission ? 1 : 0, Volatile.Read(ref executed));
    }

    [Fact]
    public void Remote_null_reply_still_rejects_non_nullable_value_type() =>
        Assert.Throws<ActorCallException>(() => GetRemoteReply<int>(RemoteActorInvocationResult.Replied<string?>(null)));

    [Fact]
    public void Remote_wrong_type_is_not_replaced_with_a_nullable_default() =>
        Assert.Throws<ActorCallException>(() => GetRemoteReply<string?>(RemoteActorInvocationResult.Replied(42)));

    [Theory]
    [InlineData(RemoteActorStatus.SerializationFailed)]
    [InlineData(RemoteActorStatus.Timeout)]
    [InlineData(RemoteActorStatus.NodeUnavailable)]
    public void Remote_failure_without_reply_is_not_a_successful_null(RemoteActorStatus status) =>
        Assert.ThrowsAny<ActorCallException>(() =>
            GetRemoteReply<string?>(RemoteActorInvocationResult.Failed(status, "remote failure")));

    [Fact]
    public void Remote_cancellation_without_reply_is_not_a_successful_null() =>
        Assert.Throws<OperationCanceledException>(() => GetRemoteReply<string?>(
            RemoteActorInvocationResult.Failed(RemoteActorStatus.Cancelled, "remote cancelled")));

    private static TResult GetRemoteReply<TResult>(RemoteActorInvocationResult result) =>
        RemoteActorCall.GetReply<TResult>(result, Id, nameof(ReplyActor), "Read");

    private static ServiceProvider CreateProvider() =>
        new ServiceCollection().AddLakonaGameServerActors().BuildServiceProvider();

    private sealed class ReplyActor : GameActor;
}
