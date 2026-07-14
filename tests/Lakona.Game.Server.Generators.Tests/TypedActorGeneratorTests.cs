using Xunit;

namespace Lakona.Game.Server.Generators.Tests;

public sealed class TypedActorGeneratorTests
{
    [Fact]
    public void Generator_emits_local_and_route_refs_for_actor()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct RoomId(string Value);

            public sealed class JoinRoomRequest
            {
            }

            public sealed class JoinRoomReply
            {
            }

            public sealed class LeaveRoomRequest
            {
            }

            public sealed class RoomActor : Actor<RoomId>
            {
                public ValueTask<JoinRoomReply> JoinAsync(JoinRoomRequest request, CancellationToken cancellationToken = default)
                {
                    return new ValueTask<JoinRoomReply>(new JoinRoomReply());
                }

                public ValueTask LeaveAsync(LeaveRoomRequest request, CancellationToken cancellationToken = default)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public sealed class ActorAccess", result.GeneratedSource);
        Assert.Contains("public LocalActor<TActor> Local<TActor>(global::Game.Server.RoomId id)", result.GeneratedSource);
        Assert.Contains("public ActorRoute<TActor> Route<TActor>(global::Game.Server.RoomId id)", result.GeneratedSource);
        Assert.Contains("public ActorPlacement<TActor, global::Game.Server.RoomId> Place<TActor>(global::Game.Server.RoomId id)", result.GeneratedSource);
        Assert.DoesNotContain("RoomActors", result.GeneratedSource);
        Assert.DoesNotContain("RoomRouteRef", result.GeneratedSource);
        Assert.DoesNotContain("RoomLocalRef", result.GeneratedSource);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask<TResult> ActorCall<in TActor, in TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask<TResult> ActorCallNoCancellation<in TActor, in TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask ActorPost<in TActor, in TRequest>(", result.GeneratedSource);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask ActorPostNoCancellation<in TActor, in TRequest>(", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("ActorCall<TActor, TRequest, TResult> method", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(", result.GeneratedSource);
        Assert.Contains("ActorPost<TActor, TRequest> method", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(", result.GeneratedSource);
        Assert.Contains("GeneratedActorMetadata<TActor>.ActorName + \"/\" + id.Value", result.GeneratedSource);
        Assert.Contains("var payload = _actors.Serializer.Serialize(request);", result.GeneratedSource);
        Assert.Contains("new global::Lakona.Game.Server.Actors.RemoteActorInvocation(", result.GeneratedSource);
        Assert.Contains("var result = await _actors.Remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(", result.GeneratedSource);
        Assert.Contains("var localResult = _actors.Runtime.TryTell<TActor>(", result.GeneratedSource);
        Assert.Contains("Routed local actor post was rejected with result", result.GeneratedSource);
        Assert.Contains("if (_actors.Runtime.GetState(_actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)", result.GeneratedSource);
        Assert.Contains("if (!_actors.DirectoryCache.TryGet(_actorId, out var node))", result.GeneratedSource);
        Assert.Contains("var record = await _actors.Directory.ResolveAsync(_actorId, cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("_actors.DirectoryCache.Set(_actorId, node);", result.GeneratedSource);
        Assert.Contains("_actors.DirectoryCache.Remove(_actorId);", result.GeneratedSource);
        Assert.DoesNotContain("public global::System.Threading.Tasks.ValueTask<JoinRoomReply> JoinAsync", result.GeneratedSource);
        Assert.DoesNotContain("public global::System.Threading.Tasks.ValueTask LeaveAsync", result.GeneratedSource);
        Assert.DoesNotContain("TryJoinAsync", result.GeneratedSource);
        Assert.DoesNotContain("TryLeaveAsync", result.GeneratedSource);
        Assert.DoesNotContain("if (result.Status != global::Lakona.Game.Server.Actors.RemoteActorStatus.Replied)", result.GeneratedSource);
        Assert.DoesNotContain("if (result.Status != global::Lakona.Game.Server.Actors.RemoteActorStatus.Accepted)", result.GeneratedSource);
        Assert.DoesNotContain("RemoteActorStatus", result.GeneratedSource);
        Assert.DoesNotContain("new global::Lakona.Game.Server.Actors.RemoteActorException", result.GeneratedSource);
        Assert.Contains("return _actors.Serializer.Deserialize<TResult>(result.Payload);", result.GeneratedSource);
        Assert.Contains("public sealed class RoomActorClusterHandler", result.GeneratedSource);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Cluster.ClusterSendStatus> HandleAsync", result.GeneratedSource);
        Assert.Contains("case \"join\":", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.RemoteActorGateway.SendReplyAsync", result.GeneratedSource);
        Assert.Contains("private readonly global::Lakona.Game.Cluster.IClusterNodeSender _nodeSender;", result.GeneratedSource);
        Assert.Contains("private readonly global::Lakona.Game.Server.Actors.LocalActorNodeIdentity _localNode;", result.GeneratedSource);
        Assert.DoesNotContain("IClusterRouter _router", result.GeneratedSource);
        Assert.Contains("            _nodeSender,", result.GeneratedSource);
        Assert.Contains("            _localNode.NodeId,", result.GeneratedSource);
        Assert.Contains("            envelope.SourceNode,", result.GeneratedSource);
        Assert.Contains("return await global::Lakona.Game.Server.Actors.RemoteActorGateway.SendReplyAsync", result.GeneratedSource);
        Assert.Contains("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedActorAccess", result.GeneratedSource);
        Assert.Contains("TryAddSingleton<ActorAccess>(services);", result.GeneratedSource);
        Assert.Contains("TryAddEnumerable", result.GeneratedSource);
        Assert.Contains("RoomActorClusterHandler", result.GeneratedSource);
    }

    [Fact]
    public void Generator_uses_ToString_for_key_without_Value_property()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public sealed class PingRequest
            {
            }

            public sealed class PingReply
            {
            }

            public sealed class SessionActor : Actor<Guid>
            {
                public ValueTask<PingReply> PingAsync(PingRequest request, CancellationToken cancellationToken = default)
                {
                    return ValueTask.FromResult(new PingReply());
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("GeneratedActorMetadata<TActor>.ActorName + \"/\" + id.ToString()", result.GeneratedSource);
    }

    [Fact]
    public void Generator_emits_placement_ref_for_distributed_actor()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct RoomId(string Value);
            public sealed record PingRequest;

            [ActorName("room")]
            public sealed class RoomActor : Actor<RoomId>
            {
                public ValueTask PingAsync(PingRequest request, CancellationToken cancellationToken = default)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("private readonly global::Lakona.Game.Server.Actors.IActorPlacementService _placement;", result.GeneratedSource);
        Assert.Contains("public ActorPlacement<TActor, global::Game.Server.RoomId> Place<TActor>(global::Game.Server.RoomId id)", result.GeneratedSource);
        Assert.Contains("return new ActorPlacement<TActor, global::Game.Server.RoomId>(_placement, id);", result.GeneratedSource);
        Assert.Contains("public readonly struct ActorPlacement<TActor, TKey>", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Server.Actors.ActorPlacementResult> CreateAsync(", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.ActorPlacementCreateMode.Create", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Server.Actors.ActorPlacementResult> EnsureAsync(", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.ActorPlacementCreateMode.Ensure", result.GeneratedSource);
        Assert.Contains("return _placement.PlaceAsync<TActor, TKey>(", result.GeneratedSource);
    }

    [Fact]
    public void Generator_uses_string_key_directly()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public sealed class PingRequest
            {
            }

            public sealed class PingReply
            {
            }

            public sealed class SessionActor : Actor<string>
            {
                public ValueTask<PingReply> PingAsync(PingRequest request, CancellationToken cancellationToken = default)
                {
                    return new ValueTask<PingReply>(new PingReply());
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("GeneratedActorMetadata<TActor>.ActorName + \"/\" + id", result.GeneratedSource);
    }

    [Fact]
    public void Generator_ignores_non_actor_classes()
    {
        var source = """
            namespace Game.Server;

            public sealed class RoomActor
            {
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Equal(string.Empty, result.GeneratedSource);
    }

    [Fact]
    public void Generator_uses_explicit_actor_and_method_names()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct RoomId(string Value);
            public sealed record JoinRoomRequest(string PlayerId);
            public sealed record JoinRoomReply(bool Accepted);

            [ActorName("battle-room")]
            public sealed class BattleRoomActor : Actor<RoomId>
            {
                [ActorMethod("join")]
                public ValueTask<JoinRoomReply> EnterAsync(
                    JoinRoomRequest request,
                    CancellationToken cancellationToken = default)
                {
                    return new ValueTask<JoinRoomReply>(new JoinRoomReply(true));
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("GeneratedActorMetadata<TActor>.ActorName + \"/\" + id.Value", result.GeneratedSource);
        Assert.Contains("return \"join\";", result.GeneratedSource);
        Assert.Contains("return \"battle-room\";", result.GeneratedSource);
        Assert.Contains("new global::Lakona.Game.Server.Actors.RemoteActorInvocation(", result.GeneratedSource);
    }

    [Fact]
    public void Generator_skips_actor_ignore_methods()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct RoomId(string Value);
            public sealed record PingRequest;

            public sealed class RoomActor : Actor<RoomId>
            {
                [ActorIgnore]
                public ValueTask HiddenAsync(PingRequest request, CancellationToken cancellationToken = default)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.DoesNotContain("HiddenAsync", result.GeneratedSource);
    }

    [Fact]
    public void Generator_does_not_emit_actor_lifecycle_methods()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct RoomId(string Value);
            public sealed record PingRequest;

            public sealed class RoomActor : Actor<RoomId>
            {
                public ValueTask PingAsync(PingRequest request, CancellationToken cancellationToken = default)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public LocalActor<TActor> Local<TActor>(global::Game.Server.RoomId id)", result.GeneratedSource);
        Assert.Contains("public ActorRoute<TActor> Route<TActor>(global::Game.Server.RoomId id)", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(", result.GeneratedSource);
        Assert.DoesNotContain("RoomActors", result.GeneratedSource);
        Assert.DoesNotContain("RoomRouteRef", result.GeneratedSource);
        Assert.DoesNotContain("public global::System.Threading.Tasks.ValueTask PingAsync(PingRequest request, global::System.Threading.CancellationToken cancellationToken = default)", result.GeneratedSource);
        Assert.DoesNotContain(string.Concat("Spawn", "Async"), result.GeneratedSource);
        Assert.DoesNotContain(string.Concat("Destroy", "Async"), result.GeneratedSource);
        Assert.DoesNotContain(string.Concat("Actor", "Spawn"), result.GeneratedSource);
        Assert.DoesNotContain(string.Concat("Actor", "Destroy"), result.GeneratedSource);
        Assert.DoesNotContain(string.Concat("Get", "Or", "Create", "Async"), result.GeneratedSource);
        Assert.DoesNotContain(string.Concat("Stop", "Async"), result.GeneratedSource);
    }

    [Fact]
    public void Generator_supports_method_groups_without_cancellation_token()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct SessionId(string Value);
            public sealed record PingRequest;
            public sealed record PingReply;

            public sealed class SessionActor : Actor<SessionId>
            {
                public ValueTask<PingReply> PingAsync(PingRequest request)
                {
                    return new ValueTask<PingReply>(new PingReply());
                }

                public ValueTask NotifyAsync(PingRequest request)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask<TResult> ActorCallNoCancellation<in TActor, in TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask ActorPostNoCancellation<in TActor, in TRequest>(", result.GeneratedSource);
        Assert.Contains("ActorCallNoCancellation<TActor, TRequest, TResult> method", result.GeneratedSource);
        Assert.Contains("ActorPostNoCancellation<TActor, TRequest> method", result.GeneratedSource);
        Assert.Contains("method(actor, value)", result.GeneratedSource);
    }

    [Fact]
    public void Generator_skips_route_ref_for_local_only_actor()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct MetricsId(string Value);
            public sealed record PingRequest;

            [ActorLocalOnly]
            public sealed class MetricsActor : Actor<MetricsId>
            {
                public ValueTask PingAsync(PingRequest request, CancellationToken cancellationToken = default)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public LocalActor<TActor> Local<TActor>(global::Game.Server.MetricsId id)", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(", result.GeneratedSource);
        Assert.DoesNotContain("ActorRoute<TActor> Route<TActor>(global::Game.Server.MetricsId id)", result.GeneratedSource);
        Assert.DoesNotContain("ActorPlacement<TActor, global::Game.Server.MetricsId> Place<TActor>", result.GeneratedSource);
        Assert.DoesNotContain("MetricsActorClusterHandler", result.GeneratedSource);
    }

    [Fact]
    public void Generated_actor_access_supports_actor_only_type_arguments_and_typed_method_lambdas()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Generated;

            namespace Game.Server;

            public readonly record struct RoomId(string Value);
            public sealed record PingRequest;
            public sealed record PingReply;

            public sealed class RoomActor : Actor<RoomId>
            {
                public ValueTask<PingReply> PingAsync(PingRequest request)
                {
                    return new ValueTask<PingReply>(new PingReply());
                }
            }

            public sealed class Caller(ActorAccess actors)
            {
                public ValueTask<PingReply> PingAsync(RoomId roomId)
                {
                    return actors.Route<RoomActor>(roomId).CallAsync(
                        (RoomActor actor, PingRequest request) => actor.PingAsync(request),
                        new PingRequest());
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
    }

    [Fact]
    public void Generated_actor_access_rejects_an_actor_key_mismatch_at_compile_time()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Generated;

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public readonly record struct RoomId(string Value);
            public sealed record PingRequest;

            public sealed class UserActor : Actor<UserId>
            {
                public ValueTask PingAsync(PingRequest request) => default;
            }

            public sealed class RoomActor : Actor<RoomId>
            {
                public ValueTask PingAsync(PingRequest request) => default;
            }

            public sealed class Caller(ActorAccess actors)
            {
                public void Invalid(RoomId roomId)
                {
                    _ = actors.Route<UserActor>(roomId);
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Contains(result.ErrorDiagnostics, diagnostic => diagnostic.Id == "CS0311");
    }

    [Fact]
    public void Generator_reports_warning_for_unsupported_public_method()
    {
        var source = """
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct RoomId(string Value);

            public sealed class RoomActor : Actor<RoomId>
            {
                public int Count()
                {
                    return 1;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "LAKONA001");
    }
}
