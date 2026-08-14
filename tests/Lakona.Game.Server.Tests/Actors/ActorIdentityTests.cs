using System.Globalization;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorIdentityTests
{
    [Fact]
    public void Identity_contains_stable_actor_name_and_escaped_invariant_key()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal("room/42", ActorIdentity.Create<RoomActor, int>(42).Value);
            Assert.Equal("room/a%2Fb%20c", ActorIdentity.Create<RoomActor, string>("a/b c").Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Scalar_value_wrapper_has_the_same_canonical_key()
    {
        Assert.Equal(
            "room/42",
            ActorIdentity.Create<RoomActor, RoomId>(new RoomId(42)).Value);
    }

    [Fact]
    public void Enum_and_reference_value_wrappers_use_the_canonical_scalar_format()
    {
        Assert.Equal("room/2", ActorIdentity.Create<RoomActor, RoomKind>(RoomKind.Second).Value);
        Assert.Equal("room/42", ActorIdentity.Create<RoomActor, ReferenceRoomId>(new ReferenceRoomId(42)).Value);
    }

    [Fact]
    public void Unsupported_key_fails_instead_of_using_to_string()
    {
        Assert.Throws<ArgumentException>(() =>
            ActorIdentity.Create<RoomActor, UnsupportedKey>(new UnsupportedKey()));
    }

    [Fact]
    public void Context_exposes_the_decoded_business_key_separately_from_the_actor_id()
    {
        var id = ActorIdentity.Create<RoomActor, string>("a/b c");
        var context = new ActorContext(id, EmptyServices.Instance, EmptyRuntime.Instance);

        Assert.Equal("room/a%2Fb%20c", context.Id.Value);
        Assert.Equal("a/b c", context.Key);
    }

    [ActorName("room")]
    private sealed class RoomActor : Actor<int>;

    private readonly record struct RoomId(int Value);
    private sealed record ReferenceRoomId(int Value);
    private enum RoomKind { First = 1, Second = 2 }

    private sealed class UnsupportedKey
    {
        public override string ToString() => "dangerous";
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public static readonly EmptyServices Instance = new();

        public object? GetService(Type serviceType) => null;
    }

    private sealed class EmptyRuntime : IActorRuntime
    {
        public static readonly EmptyRuntime Instance = new();

        public ValueTask TellAsync<TActor>(ActorId id, Func<TActor, CancellationToken, ValueTask> message, CancellationToken cancellationToken = default) where TActor : class, IActor => throw new NotSupportedException();
        public ActorTellResult TryTell<TActor>(ActorId id, Func<TActor, CancellationToken, ValueTask> message, CancellationToken cancellationToken = default) where TActor : class, IActor => throw new NotSupportedException();
        public ValueTask TellAsync(Type actorType, ActorId id, Func<IActor, CancellationToken, ValueTask> message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ActorTellResult TryTell(Type actorType, ActorId id, Func<IActor, CancellationToken, ValueTask> message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TResult> AskAsync<TActor, TResult>(ActorId id, Func<TActor, CancellationToken, ValueTask<TResult>> message, CancellationToken cancellationToken = default) where TActor : class, IActor => throw new NotSupportedException();
        public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType) => throw new NotSupportedException();
        public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics) => throw new NotSupportedException();
        public ActorState GetState(ActorId id) => throw new NotSupportedException();
    }
}
