using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorStartArgumentsValidationTests
{
    [Theory]
    [InlineData(typeof(MissingMarker), "missing [MemoryPackable]")]
    [InlineData(typeof(AbstractArgs), "concrete class")]
    [InlineData(typeof(GenericArgs<int>), "must not be generic")]
    [InlineData(typeof(int), "concrete class")]
    public void Runtime_error_identifies_type_assembly_and_repair(Type type, string reason)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ActorStartArguments.ValidateType(type));
        Assert.Contains(type.FullName!, error.Message);
        Assert.Contains(type.Assembly.GetName().Name!, error.Message);
        Assert.Contains(reason, error.Message);
        Assert.Contains("DTO", error.Message);
    }

    public sealed class MissingMarker;
    public abstract class AbstractArgs;
    public sealed class GenericArgs<T>;
}
