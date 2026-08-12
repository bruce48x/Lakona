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
    public void Unsupported_key_fails_instead_of_using_to_string()
    {
        Assert.Throws<ArgumentException>(() =>
            ActorIdentity.Create<RoomActor, UnsupportedKey>(new UnsupportedKey()));
    }

    [ActorName("room")]
    private sealed class RoomActor : Actor<int>;

    private readonly record struct RoomId(int Value);

    private sealed class UnsupportedKey
    {
        public override string ToString() => "dangerous";
    }
}
