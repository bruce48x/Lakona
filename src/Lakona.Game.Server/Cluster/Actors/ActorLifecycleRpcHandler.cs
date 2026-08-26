using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Cluster.Actors;

internal sealed class ActorLifecycleRpcHandler(
    ActorActivationCatalog activationCatalog,
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
            || string.IsNullOrWhiteSpace(request.HotfixVersion)
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
                request.HotfixVersion,
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
                hotfixVersion: null,
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
        string? hotfixVersion,
        CancellationToken cancellationToken)
    {
        var admissionGate = services.GetService<IDistributedWorkAdmissionGate>();
        DistributedWorkAdmission admission = default;
        if (admissionGate is not null && !admissionGate.TryEnter(out admission))
            return Failure("The node is not authoritative for distributed Actor work.");
        try
        {
            if (target.Owner != localNode.Reference)
                return Failure("The proposed Actor activation does not belong to this node incarnation.");

            if (operation == ActorLifecycleOperation.Destroy)
            {
                var record = await directory.ResolveAsync(target.ActorId, cancellationToken).ConfigureAwait(false);
                if (record?.OwnerReference != target.Owner || record.ActivationId != target.ActivationId)
                    return new ActorLifecycleReply
                    {
                        Succeeded = true,
                        Message = "The exact Actor activation is already absent."
                    };
            }

            using var lease = hotfixRuntime.AcquireCurrent();
            if (operation != ActorLifecycleOperation.Destroy)
            {
                var currentHotfixVersion = StartupActorIdentity.NormalizeHotfixVersion(lease.Snapshot.SourceVersion);
                if (!string.Equals(hotfixVersion, currentHotfixVersion, StringComparison.Ordinal))
                    return Failure(
                        $"Actor host capability hotfix version '{hotfixVersion}' is stale; current version is '{currentHotfixVersion}'.");
            }

            if (!lease.Snapshot.ActorLifecycleDispatch.TryResolve(actor, out var dispatch))
                return Failure($"Actor '{actor}' is not loaded.");

            try
            {
                await dispatch.InvokeAsync(activationCatalog, operation, target, cancellationToken).ConfigureAwait(false);
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
            catch (ActorAlreadyHostedException exception)
            {
                var current = await directory.ResolveAsync(target.ActorId, cancellationToken).ConfigureAwait(false);
                return new ActorLifecycleReply
                {
                    OwnerNode = current?.Node.Value,
                    Message = exception.Message
                };
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
