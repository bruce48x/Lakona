using System.Reflection;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorLifecycleRpcHandler(
    ActorHosting hosting,
    IActorDirectory directory,
    IHotfixRuntimeAccessor hotfixRuntime,
    LocalActorNodeIdentity localNode,
    IServiceProvider services)
{
    internal async ValueTask<ActorLifecycleReply> HandleAsync(
        ActorLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        var admissionGate = services.GetService<IDistributedWorkAdmissionGate>();
        DistributedWorkAdmission admission = default;
        if (admissionGate is not null && !admissionGate.TryEnter(out admission))
            return Failure("The node is not authoritative for distributed Actor work.");
        try
        {
            var actorId = ActorId.From(request.ActorId);
            var record = await directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
            if (record?.OwnerReference is not { } owner
                || owner.Node != localNode.NodeId
                || owner.Cluster.Value != request.ClusterIncarnation
                || owner.Incarnation.Value != request.NodeIncarnation
                || record.ActivationId?.Value != request.ActivationId
                || record.Version != request.ActivationVersion)
                return Failure("The proposed Actor activation is no longer current.");

            using var lease = hotfixRuntime.AcquireCurrent();
            var actorType = lease.Snapshot.ActorTypes.SingleOrDefault(type =>
                string.Equals(ActorNameResolver.Resolve(type), request.Actor, StringComparison.Ordinal));
            if (actorType is null) return Failure($"Actor '{request.Actor}' is not loaded.");

            try
            {
                await InvokeHostingAsync(actorType, actorId, request.Mode, cancellationToken).ConfigureAwait(false);
                return new ActorLifecycleReply { Succeeded = true, OwnerNode = localNode.NodeId.Value, Message = "created" };
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
        service.Register<ActorLifecycleRequest, ActorLifecycleReply>(
            ActorLifecycleProtocol.CreateMethodId,
            static (value, request, ct) => value.HandleAsync(request, ct),
            "Create");
    }

    private async ValueTask InvokeHostingAsync(
        Type actorType,
        ActorId actorId,
        string mode,
        CancellationToken cancellationToken)
    {
        var name = string.Equals(mode, "create", StringComparison.OrdinalIgnoreCase)
            ? nameof(ActorHosting.CreateAsync)
            : string.Equals(mode, "ensure", StringComparison.OrdinalIgnoreCase)
                ? nameof(ActorHosting.EnsureAsync)
                : throw new InvalidOperationException($"Unknown Actor lifecycle mode '{mode}'.");
        var method = typeof(ActorHosting).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(candidate => candidate.Name == name && candidate.IsGenericMethodDefinition);
        await ((ValueTask)method.MakeGenericMethod(actorType)
            .Invoke(hosting, [actorId, cancellationToken])!).ConfigureAwait(false);
    }

    private static ActorLifecycleReply Failure(string message) => new() { Message = message };
}
