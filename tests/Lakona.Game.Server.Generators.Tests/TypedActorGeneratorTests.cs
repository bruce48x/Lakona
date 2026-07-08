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
        Assert.Contains("public sealed class RoomActors", result.GeneratedSource);
        Assert.Contains("public RoomLocalRef Local(RoomId id)", result.GeneratedSource);
        Assert.Contains("public RoomRouteRef Route(RoomId id)", result.GeneratedSource);
        Assert.Contains("return new RoomRouteRef(_runtime, _remote, _serializer, _options, _directory, _directoryCache, id);", result.GeneratedSource);
        Assert.DoesNotContain("public RoomRef Get(RoomId id)", result.GeneratedSource);
        Assert.DoesNotContain("public RoomRemoteRef Remote(", result.GeneratedSource);
        Assert.DoesNotContain("RoomRemoteRef", result.GeneratedSource);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask<TResult> RoomActorCall<in TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask<TResult> RoomActorCallNoCancellation<in TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask RoomActorPost<in TRequest>(", result.GeneratedSource);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask RoomActorPostNoCancellation<in TRequest>(", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("RoomActorCall<TRequest, TResult> method", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(", result.GeneratedSource);
        Assert.Contains("RoomActorPost<TRequest> method", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(", result.GeneratedSource);
        Assert.Contains("private readonly global::Lakona.Game.Server.Actors.IActorDirectory _directory;", result.GeneratedSource);
        Assert.Contains("private readonly global::Lakona.Game.Server.Actors.IActorDirectoryCache _directoryCache;", result.GeneratedSource);
        Assert.DoesNotContain("LocalActorNodeIdentity", result.GeneratedSource);
        Assert.Contains("private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;", result.GeneratedSource);
        Assert.Contains("return _runtime.AskAsync<global::Game.Server.RoomActor, TResult>", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.ActorId.From(\"room/\" + _id.Value)", result.GeneratedSource);
        Assert.Contains("var payload = _serializer.Serialize(request);", result.GeneratedSource);
        Assert.Contains("new global::Lakona.Game.Server.Actors.RemoteActorInvocation(node, actorId, \"room\", methodName, payload, deadline, correlationId)", result.GeneratedSource);
        Assert.Contains("var result = await _remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(result, actorId, \"room\", methodName, node, correlationId);", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(result, actorId, \"room\", methodName, node, correlationId);", result.GeneratedSource);
        Assert.Contains("var localResult = _runtime.TryTell<global::Game.Server.RoomActor>(actorId, (actor, ct) => method(actor, request, ct), cancellationToken);", result.GeneratedSource);
        Assert.Contains("Routed local actor post was rejected with result", result.GeneratedSource);
        Assert.Contains("if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)", result.GeneratedSource);
        Assert.Contains("if (!_directoryCache.TryGet(actorId, out var node))", result.GeneratedSource);
        Assert.Contains("var record = await _directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("_directoryCache.Set(actorId, node);", result.GeneratedSource);
        Assert.Contains("_directoryCache.Remove(actorId);", result.GeneratedSource);
        Assert.DoesNotContain("public global::System.Threading.Tasks.ValueTask<JoinRoomReply> JoinAsync", result.GeneratedSource);
        Assert.DoesNotContain("public global::System.Threading.Tasks.ValueTask LeaveAsync", result.GeneratedSource);
        Assert.DoesNotContain("TryJoinAsync", result.GeneratedSource);
        Assert.DoesNotContain("TryLeaveAsync", result.GeneratedSource);
        Assert.DoesNotContain("if (result.Status != global::Lakona.Game.Server.Actors.RemoteActorStatus.Replied)", result.GeneratedSource);
        Assert.DoesNotContain("if (result.Status != global::Lakona.Game.Server.Actors.RemoteActorStatus.Accepted)", result.GeneratedSource);
        Assert.DoesNotContain("RemoteActorStatus", result.GeneratedSource);
        Assert.DoesNotContain("new global::Lakona.Game.Server.Actors.RemoteActorException", result.GeneratedSource);
        Assert.Contains("return _serializer.Deserialize<TResult>(result.Payload);", result.GeneratedSource);
        Assert.Contains("public sealed class RoomActorClusterHandler", result.GeneratedSource);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Cluster.ClusterSendStatus> HandleAsync", result.GeneratedSource);
        Assert.Contains("case \"join\":", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.RemoteActorGateway.SendReplyAsync", result.GeneratedSource);
        Assert.Contains("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddRoomActors", result.GeneratedSource);
        Assert.Contains("TryAddSingleton<RoomActors>(services);", result.GeneratedSource);
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
        Assert.Contains("global::Lakona.Game.Server.Actors.ActorId.From(\"session/\" + _id.ToString())", result.GeneratedSource);
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
        Assert.Contains("global::Lakona.Game.Server.Actors.ActorId.From(\"session/\" + _id)", result.GeneratedSource);
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
        Assert.Contains("global::Lakona.Game.Server.Actors.ActorId.From(\"battle-room/\" + _id.Value)", result.GeneratedSource);
        Assert.Contains("return \"join\";", result.GeneratedSource);
        Assert.Contains("new global::Lakona.Game.Server.Actors.RemoteActorInvocation(node, actorId, \"battle-room\", methodName, payload, deadline, correlationId)", result.GeneratedSource);
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
        Assert.Contains("public RoomLocalRef Local(RoomId id)", result.GeneratedSource);
        Assert.Contains("public RoomRouteRef Route(RoomId id)", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(", result.GeneratedSource);
        Assert.DoesNotContain("public RoomRef Get(RoomId id)", result.GeneratedSource);
        Assert.DoesNotContain("public RoomRemoteRef Remote(", result.GeneratedSource);
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
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask<TResult> SessionActorCallNoCancellation<in TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("public delegate global::System.Threading.Tasks.ValueTask SessionActorPostNoCancellation<in TRequest>(", result.GeneratedSource);
        Assert.Contains("SessionActorCallNoCancellation<TRequest, TResult> method", result.GeneratedSource);
        Assert.Contains("SessionActorPostNoCancellation<TRequest> method", result.GeneratedSource);
        Assert.Contains("method(actor, argument)", result.GeneratedSource);
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
        Assert.DoesNotContain("public MetricsRef Get(MetricsId id)", result.GeneratedSource);
        Assert.Contains("public MetricsLocalRef Local(MetricsId id)", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(", result.GeneratedSource);
        Assert.DoesNotContain("public MetricsRouteRef Route(MetricsId id)", result.GeneratedSource);
        Assert.DoesNotContain("MetricsRouteRef", result.GeneratedSource);
        Assert.DoesNotContain("MetricsActorClusterHandler", result.GeneratedSource);
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
