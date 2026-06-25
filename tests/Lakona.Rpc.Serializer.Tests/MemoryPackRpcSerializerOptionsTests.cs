using Lakona.Rpc.Serializer.MemoryPack;
using MemoryPack;

namespace Lakona.Rpc.Serializer.Tests;

public class MemoryPackRpcSerializerOptionsTests
{
    [Fact]
    public void Constructor_rejects_null_options()
    {
        Assert.Throws<ArgumentNullException>(() => new MemoryPackRpcSerializer(null!));
    }

    [Fact]
    public void Default_constructor_roundtrips_memorypackable_payload()
    {
        var serializer = new MemoryPackRpcSerializer();
        var input = new MemoryPackOptionsPayload
        {
            Name = "default",
            Value = 42
        };

        using var bytes = serializer.SerializeFrame(input);
        var output = serializer.Deserialize<MemoryPackOptionsPayload>(bytes.Memory);

        Assert.Equal(input.Name, output.Name);
        Assert.Equal(input.Value, output.Value);
    }

    [Fact]
    public void Configured_constructor_roundtrips_memorypackable_payload()
    {
        var serializer = new MemoryPackRpcSerializer(MemoryPackSerializerOptions.Default);
        var input = new MemoryPackOptionsPayload
        {
            Name = "configured",
            Value = 99
        };

        using var bytes = serializer.SerializeFrame(input);
        var output = serializer.Deserialize<MemoryPackOptionsPayload>(bytes.Span);

        Assert.Equal(input.Name, output.Name);
        Assert.Equal(input.Value, output.Value);
    }
}

[MemoryPackable]
public partial class MemoryPackOptionsPayload
{
    public string Name { get; set; } = string.Empty;

    public int Value { get; set; }
}
