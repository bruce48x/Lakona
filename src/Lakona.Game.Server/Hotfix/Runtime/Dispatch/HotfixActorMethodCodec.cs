using System.Buffers;
using System.Reflection;
using Lakona.Rpc.Core;
using MemoryPack;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal interface IHotfixActorMethodCodec
{
    object? DeserializeRequest(ReadOnlyMemory<byte> payload);

    void SerializeResult(IBufferWriter<byte> writer, object? result);
}

internal static class HotfixActorMethodCodec
{
    private static readonly MethodInfo CreateResultMethod = typeof(HotfixActorMethodCodec)
        .GetMethod(nameof(CreateResult), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CreateNoResultMethod = typeof(HotfixActorMethodCodec)
        .GetMethod(nameof(CreateNoResult), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static IHotfixActorMethodCodec Create(Type requestType, Type? resultType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        var factory = resultType is null
            ? CreateNoResultMethod.MakeGenericMethod(requestType)
            : CreateResultMethod.MakeGenericMethod(requestType, resultType);
        return (IHotfixActorMethodCodec)factory.Invoke(null, null)!;
    }

    private static IHotfixActorMethodCodec CreateNoResult<TRequest>()
    {
        return NoResultCodec<TRequest>.Instance;
    }

    private static IHotfixActorMethodCodec CreateResult<TRequest, TResult>()
    {
        return ResultCodec<TRequest, TResult>.Instance;
    }

    private sealed class NoResultCodec<TRequest> : IHotfixActorMethodCodec
    {
        public static NoResultCodec<TRequest> Instance { get; } = new();

        public object? DeserializeRequest(ReadOnlyMemory<byte> payload)
        {
            return MemoryPackSerializer.Deserialize<TRequest>(payload.Span);
        }

        public void SerializeResult(IBufferWriter<byte> writer, object? result)
        {
            if (result is not null)
            {
                throw new InvalidOperationException(
                    "A resultless Hotfix Actor method returned a result.");
            }
        }
    }

    private sealed class ResultCodec<TRequest, TResult> : IHotfixActorMethodCodec
    {
        public static ResultCodec<TRequest, TResult> Instance { get; } = new();

        public object? DeserializeRequest(ReadOnlyMemory<byte> payload)
        {
            return MemoryPackSerializer.Deserialize<TRequest>(payload.Span);
        }

        public void SerializeResult(IBufferWriter<byte> writer, object? result)
        {
            var typed = (TResult)result!;
            MemoryPackSerializer.Serialize(writer, typed);
        }
    }
}
