using Xunit;

namespace Lakona.Game.Server.Generators.Tests;

public sealed class TypedActorGeneratorTests
{
    [Fact]
    public void Generator_emits_local_and_remote_refs_for_actor()
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
                    return ValueTask.FromResult(new JoinRoomReply());
                }

                public ValueTask LeaveAsync(LeaveRoomRequest request, CancellationToken cancellationToken = default)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public sealed class RoomActors", result.GeneratedSource);
        Assert.Contains("public RoomRef Get(RoomId id)", result.GeneratedSource);
        Assert.Contains("public RoomLocalRef Local(RoomId id)", result.GeneratedSource);
        Assert.Contains("public RoomRemoteRef Remote(global::Lakona.Game.Cluster.NodeId nodeId, RoomId id)", result.GeneratedSource);
        Assert.Contains("return new RoomRef(_runtime, _remote, _serializer, _options, _directory, _directoryCache, id);", result.GeneratedSource);
        Assert.Contains("return new RoomRemoteRef(_remote, _serializer, _options, nodeId, id);", result.GeneratedSource);
        Assert.Contains("private readonly global::Lakona.Game.Server.Actors.IActorDirectory _directory;", result.GeneratedSource);
        Assert.Contains("private readonly global::Lakona.Game.Server.Actors.IActorDirectoryCache _directoryCache;", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Cluster.NodeId nodeId,", result.GeneratedSource);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<JoinRoomReply> JoinAsync", result.GeneratedSource);
        Assert.Contains("private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;", result.GeneratedSource);
        Assert.Contains("return _runtime.AskAsync<global::Game.Server.RoomActor, JoinRoomReply>", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.ActorId.From(\"room/\" + _id.Value)", result.GeneratedSource);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask<JoinRoomReply> JoinAsync", result.GeneratedSource);
        Assert.Contains("var payload = _serializer.Serialize(request);", result.GeneratedSource);
        Assert.Contains("new global::Lakona.Game.Server.Actors.RemoteActorInvocation(_node, actorId, \"room\", \"join\", payload, deadline, correlationId)", result.GeneratedSource);
        Assert.Contains("var result = await _remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(result, actorId, \"room\", \"join\", _node, correlationId);", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(result, actorId, \"room\", \"leave\", _node, correlationId);", result.GeneratedSource);
        Assert.Contains("if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)", result.GeneratedSource);
        Assert.Contains("if (!_directoryCache.TryGet(actorId, out var node))", result.GeneratedSource);
        Assert.Contains("var record = await _directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("_directoryCache.Set(actorId, node);", result.GeneratedSource);
        Assert.Contains("_directoryCache.Remove(actorId);", result.GeneratedSource);
        Assert.DoesNotContain("if (result.Status != global::Lakona.Game.Server.Actors.RemoteActorStatus.Replied)", result.GeneratedSource);
        Assert.DoesNotContain("if (result.Status != global::Lakona.Game.Server.Actors.RemoteActorStatus.Accepted)", result.GeneratedSource);
        Assert.DoesNotContain("RemoteActorStatus", result.GeneratedSource);
        Assert.DoesNotContain("new global::Lakona.Game.Server.Actors.RemoteActorException", result.GeneratedSource);
        Assert.Contains("return _serializer.Deserialize<JoinRoomReply>(result.Payload);", result.GeneratedSource);
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
                    return ValueTask.FromResult(new PingReply());
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
                    return ValueTask.FromResult(new JoinRoomReply(true));
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("global::Lakona.Game.Server.Actors.ActorId.From(\"battle-room/\" + _id.Value)", result.GeneratedSource);
        Assert.Contains("\"battle-room\", \"join\"", result.GeneratedSource);
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
                    return ValueTask.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.DoesNotContain("HiddenAsync", result.GeneratedSource);
    }

    [Fact]
    public void Generator_emits_local_lifecycle_methods_and_excludes_hooks_from_business_methods()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct RoomId(string Value);
            public sealed record SpawnRoomRequest(string OwnerId);
            public sealed record PingRequest;

            public sealed class RoomActor : Actor<RoomId>
            {
                [ActorSpawn]
                public ValueTask OpenAsync(SpawnRoomRequest request, CancellationToken cancellationToken = default)
                {
                    return ValueTask.CompletedTask;
                }

                [ActorDestroy]
                public ValueTask CloseAsync(CancellationToken cancellationToken = default)
                {
                    return ValueTask.CompletedTask;
                }

                public ValueTask PingAsync(PingRequest request, CancellationToken cancellationToken = default)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask SpawnAsync(RoomId id, SpawnRoomRequest request, global::System.Threading.CancellationToken cancellationToken = default)", result.GeneratedSource);
        Assert.Contains("await _runtime.GetOrCreateAsync<global::Game.Server.RoomActor>(actorId, cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("await _runtime.TellAsync<global::Game.Server.RoomActor>(actorId, (actor, ct) => actor.OpenAsync(request, ct), cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("var registerStatus = await _directory.RegisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("_directoryCache.Set(actorId, _localNode.NodeId);", result.GeneratedSource);
        var spawnRegister = result.GeneratedSource.IndexOf("var registerStatus = await _directory.RegisterAsync", StringComparison.Ordinal);
        var spawnHook = result.GeneratedSource.IndexOf("actor.OpenAsync(request, ct)", StringComparison.Ordinal);
        Assert.True(spawnRegister >= 0 && spawnRegister < spawnHook);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask DestroyAsync(RoomId id, global::System.Threading.CancellationToken cancellationToken = default)", result.GeneratedSource);
        Assert.Contains("await _runtime.TellAsync<global::Game.Server.RoomActor>(actorId, (actor, ct) => actor.CloseAsync(ct), cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("await _runtime.StopAsync(actorId).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("var unregisterStatus = await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        var destroyUnregister = result.GeneratedSource.IndexOf("var unregisterStatus = await _directory.UnregisterAsync", StringComparison.Ordinal);
        var destroyHook = result.GeneratedSource.IndexOf("actor.CloseAsync(ct)", StringComparison.Ordinal);
        var destroyStop = result.GeneratedSource.IndexOf("await _runtime.StopAsync(actorId)", destroyHook, StringComparison.Ordinal);
        var destroyRestoreRegister = result.GeneratedSource.LastIndexOf("await _directory.RegisterAsync(actorId, _localNode.NodeId, cancellationToken)", StringComparison.Ordinal);
        Assert.True(destroyUnregister >= 0 && destroyUnregister < destroyHook);
        Assert.True(destroyUnregister < destroyStop);
        Assert.True(destroyRestoreRegister > destroyStop);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask PingAsync(PingRequest request, global::System.Threading.CancellationToken cancellationToken = default)", result.GeneratedSource);
        Assert.DoesNotContain("public global::System.Threading.Tasks.ValueTask OpenAsync", result.GeneratedSource);
        Assert.DoesNotContain("public global::System.Threading.Tasks.ValueTask CloseAsync", result.GeneratedSource);
        Assert.DoesNotContain("case \"open\":", result.GeneratedSource);
        Assert.DoesNotContain("case \"close\":", result.GeneratedSource);
        Assert.DoesNotContain("\"room\", \"open\"", result.GeneratedSource);
        Assert.DoesNotContain("\"room\", \"close\"", result.GeneratedSource);
    }

    [Fact]
    public void Generator_emits_lifecycle_methods_without_hook_request_when_hook_has_no_request()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct RoomId(string Value);

            public sealed class RoomActor : Actor<RoomId>
            {
                [ActorSpawn]
                public ValueTask OpenAsync(CancellationToken cancellationToken = default)
                {
                    return ValueTask.CompletedTask;
                }

                [ActorDestroy]
                public ValueTask CloseAsync()
                {
                    return ValueTask.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask SpawnAsync(RoomId id, global::System.Threading.CancellationToken cancellationToken = default)", result.GeneratedSource);
        Assert.Contains("await _runtime.TellAsync<global::Game.Server.RoomActor>(actorId, (actor, ct) => actor.OpenAsync(ct), cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask DestroyAsync(RoomId id, global::System.Threading.CancellationToken cancellationToken = default)", result.GeneratedSource);
        Assert.Contains("await _runtime.TellAsync<global::Game.Server.RoomActor>(actorId, (actor, ct) => actor.CloseAsync(), cancellationToken).ConfigureAwait(false);", result.GeneratedSource);
    }

    [Fact]
    public void Generator_reports_warning_for_destroy_hook_with_request()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public readonly record struct RoomId(string Value);
            public sealed record DestroyRoomRequest(string Reason);

            public sealed class RoomActor : Actor<RoomId>
            {
                [ActorDestroy]
                public ValueTask CloseAsync(DestroyRoomRequest request, CancellationToken cancellationToken = default)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "ULINKACTOR001");
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask DestroyAsync(RoomId id, global::System.Threading.CancellationToken cancellationToken = default)", result.GeneratedSource);
        Assert.DoesNotContain("DestroyRoomRequest request", result.GeneratedSource);
        Assert.DoesNotContain("actor.CloseAsync(request", result.GeneratedSource);
    }

    [Fact]
    public void Generator_skips_remote_ref_for_local_only_actor()
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
                    return ValueTask.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.DoesNotContain("public MetricsRef Get(MetricsId id)", result.GeneratedSource);
        Assert.Contains("public MetricsLocalRef Local(MetricsId id)", result.GeneratedSource);
        Assert.DoesNotContain("MetricsRemoteRef", result.GeneratedSource);
        Assert.DoesNotContain("Remote(global::Lakona.Game.Cluster.NodeId nodeId", result.GeneratedSource);
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

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "ULINKACTOR001");
    }
}
