using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Cluster.Actors;

internal sealed class ActorLifecycleRpcHandler(
    ActorHosting hosting,
    IActorDirectory directory,
    IHotfixRuntimeAccessor hotfixRuntime,
    LocalActorNodeIdentity localNode,
    IServiceProvider services)
{
    internal ValueTask<ActorLifecycleReply> HandleCreateAsync(
        ActorLifecycleCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Actor)
            || string.IsNullOrWhiteSpace(request.BuildTag)
            || request.Mode is not (ActorPlacementCreateMode.Create or ActorPlacementCreateMode.Ensure))
        {
            return new ValueTask<ActorLifecycleReply>(Failure("The Actor lifecycle create request is invalid."));
        }

        try
        {
            var operation = request.Mode == ActorPlacementCreateMode.Create
                ? ActorLifecycleOperation.Create
                : ActorLifecycleOperation.Ensure;
            return ExecuteAsync(
                request.Actor,
                ActorLifecycleWireRequest.DecodeTarget(request.Target),
                operation,
                request.BuildTag,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return new ValueTask<ActorLifecycleReply>(Failure("The Actor lifecycle create request is invalid."));
        }
    }

    internal ValueTask<ActorLifecycleReply> HandleDestroyAsync(
        ActorLifecycleDestroyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Actor))
        {
            return new ValueTask<ActorLifecycleReply>(Failure("The Actor lifecycle destroy request is invalid."));
        }

        try
        {
            return ExecuteAsync(
                request.Actor,
                ActorLifecycleWireRequest.DecodeTarget(request.Target),
                ActorLifecycleOperation.Destroy,
                buildTag: null,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return new ValueTask<ActorLifecycleReply>(Failure("The Actor lifecycle destroy request is invalid."));
        }
    }

    private async ValueTask<ActorLifecycleReply> ExecuteAsync(
        string actor,
        ActorLifecycleTarget target,
        ActorLifecycleOperation operation,
        string? buildTag,
        CancellationToken cancellationToken)
    {
        var admissionGate = services.GetService<IDistributedWorkAdmissionGate>();
        DistributedWorkAdmission admission = default;
        if (admissionGate is not null && !admissionGate.TryEnter(out admission))
            return Failure("The node is not authoritative for distributed Actor work.");
        try
        {
            var record = await directory.ResolveAsync(target.ActorId, cancellationToken).ConfigureAwait(false);
            if (record?.OwnerReference is not { } owner
                || owner != target.Owner
                || owner != localNode.Reference
                || record.ActivationId != target.ActivationId)
                return operation == ActorLifecycleOperation.Destroy
                    ? new ActorLifecycleReply { Succeeded = true, Message = "The exact Actor activation is already absent." }
                    : Failure("The proposed Actor activation is no longer current.");

            using var lease = hotfixRuntime.AcquireCurrent();
            if (operation != ActorLifecycleOperation.Destroy)
            {
                var currentBuildTag = StartupActorIdentity.NormalizeBuildTag(lease.Snapshot.SourceVersion);
                if (!string.Equals(buildTag, currentBuildTag, StringComparison.Ordinal))
                    return Failure(
                        $"Actor host capability build '{buildTag}' is stale; current build is '{currentBuildTag}'.");
            }

            if (!lease.Snapshot.ActorLifecycleDispatch.TryResolve(actor, out var dispatch))
                return Failure($"Actor '{actor}' is not loaded.");

            try
            {
                await dispatch.InvokeAsync(hosting, operation, target, cancellationToken).ConfigureAwait(false);
                return new ActorLifecycleReply
                {
                    Succeeded = true,
                    OwnerNode = localNode.NodeId.Value,
                    Message = operation.ToString().ToLowerInvariant()
                };
            }
            catch (ActorHostedElsewhereException exception)
            {
                return new ActorLifecycleReply { OwnerNode = exception.OwnerNode.Value, Message = exception.Message };
            }
            catch (Exception exception)
            {
                return Failure(exception.Message);
            }
        }
        finally
        {
            if (admission.IsAdmitted) admissionGate!.Exit(admission);
        }
    }

    internal static void Bind(RpcServiceRegistry registry, ActorLifecycleRpcHandler handler)
    {
        var service = registry.RegisterSingleton(ClusterProtocol.ServiceId, handler, serviceName: "ActorLifecycle");
        service.Register<ActorLifecycleCreateRequest, ActorLifecycleReply>(
            ActorLifecycleProtocol.CreateMethodId,
            static (value, request, ct) => value.HandleCreateAsync(request, ct),
            "Create");
        service.Register<ActorLifecycleDestroyRequest, ActorLifecycleReply>(
            ActorLifecycleProtocol.DestroyMethodId,
            static (value, request, ct) => value.HandleDestroyAsync(request, ct),
            "Destroy");
    }

    private static ActorLifecycleReply Failure(string message) => new() { Message = message };
}
