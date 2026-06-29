using Lakona.Game.Cluster;
using Lakona.Game.Server.Features;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixFeatureMessageHandler : IFeatureMessageHandler
{
    private readonly IHotfixRuntimeAccessor? _hotfixRuntime;
    private readonly IFeatureMessageSerializer? _serializer;

    public HotfixFeatureMessageHandler(
        IHotfixRuntimeAccessor? hotfixRuntime = null,
        IFeatureMessageSerializer? serializer = null)
    {
        _hotfixRuntime = hotfixRuntime;
        _serializer = serializer;
    }

    public async ValueTask<FeatureMessageReply> HandleAsync(
        FeatureMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.IsExpired(DateTimeOffset.UtcNow))
        {
            return new FeatureMessageReply(ClusterSendStatus.Expired, ReadOnlyMemory<byte>.Empty);
        }

        if (_hotfixRuntime is null)
        {
            return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, ReadOnlyMemory<byte>.Empty);
        }

        var snapshot = _hotfixRuntime.Current;
        if (!FeatureCommandId.TryParse(request.Kind, out var commandId))
        {
            return new FeatureMessageReply(
                ClusterSendStatus.Rejected,
                ReadOnlyMemory<byte>.Empty,
                "Feature message kind must be a positive feature command id.");
        }

        if (!snapshot.FeatureCommands.TryResolve(request.Feature.Value, commandId, out var descriptor))
        {
            return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, ReadOnlyMemory<byte>.Empty);
        }

        if (_serializer is null)
        {
            return new FeatureMessageReply(
                ClusterSendStatus.HandlerUnavailable,
                ReadOnlyMemory<byte>.Empty,
                "Feature message serializer is unavailable.");
        }

        object? command;
        try
        {
            command = FeatureMessageSerializerInvoker.Deserialize(
                _serializer,
                descriptor.RequestType,
                request.Payload);
        }
        catch (Exception ex)
        {
            return new FeatureMessageReply(
                ClusterSendStatus.DeserializationFailed,
                ReadOnlyMemory<byte>.Empty,
                ex.Message);
        }

        object? reply;
        try
        {
            reply = await snapshot.FeatureCommands
                .InvokeAsync(descriptor, command, request, snapshot.Services, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FeatureMessageReply(
                ClusterSendStatus.Failed,
                ReadOnlyMemory<byte>.Empty,
                ex.Message);
        }

        try
        {
            var payload = FeatureMessageSerializerInvoker.Serialize(
                _serializer,
                descriptor.ReplyType,
                reply);
            return new FeatureMessageReply(ClusterSendStatus.Accepted, payload);
        }
        catch (Exception ex)
        {
            return new FeatureMessageReply(
                ClusterSendStatus.SerializationFailed,
                ReadOnlyMemory<byte>.Empty,
                ex.Message);
        }
    }
}
