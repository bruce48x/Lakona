using Lakona.Rpc.Core;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Lakona.Game.Server.Actors;

public sealed class RpcRemoteActorSerializer : IRemoteActorSerializer
{
    private static readonly MethodInfo SerializeFrameMethod = typeof(IRpcSerializer)
        .GetMethods()
        .Single(static method => method.Name == nameof(IRpcSerializer.SerializeFrame)
            && method.IsGenericMethodDefinition);

    private static readonly MethodInfo DeserializeMemoryMethod = typeof(IRpcSerializer)
        .GetMethods()
        .Single(static method => method.Name == nameof(IRpcSerializer.Deserialize)
            && method.IsGenericMethodDefinition
            && method.GetParameters() is [{ ParameterType: var type }]
            && type == typeof(ReadOnlyMemory<byte>));

    private readonly IRpcSerializer _serializer;

    public RpcRemoteActorSerializer(IRpcSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public ReadOnlyMemory<byte> Serialize<T>(T value)
    {
        using var frame = _serializer.SerializeFrame(value);
        return frame.Memory.ToArray();
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> payload)
    {
        return _serializer.Deserialize<T>(payload);
    }

    public ReadOnlyMemory<byte> Serialize(object? value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        try
        {
            using var frame = (TransportFrame)SerializeFrameMethod
                .MakeGenericMethod(type)
                .Invoke(_serializer, [value])!;
            return frame.Memory.ToArray();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        try
        {
            return DeserializeMemoryMethod
                .MakeGenericMethod(type)
                .Invoke(_serializer, [payload]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
