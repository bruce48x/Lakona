using System.Text;

namespace Lakona.Game.Server.Generators
{
    public sealed partial class TypedActorGenerator
    {
        private static void AppendActorRouteSelector(StringBuilder builder)
        {
            builder.AppendLine("public readonly struct ActorRoute<TActor>");
            builder.AppendLine("    where TActor : global::Lakona.Game.Server.Actors.Actor");
            builder.AppendLine("{");
            builder.AppendLine("    private readonly ActorAccess _actors;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.ActorId _actorId;");
            builder.AppendLine();
            builder.AppendLine("    internal ActorRoute(ActorAccess actors, global::Lakona.Game.Server.Actors.ActorId actorId)");
            builder.AppendLine("    {");
            builder.AppendLine("        _actors = actors;");
            builder.AppendLine("        _actorId = actorId;");
            builder.AppendLine("    }");
            builder.AppendLine();
            AppendActorCallApi(builder);
            builder.AppendLine();
            AppendRouteCallCore(builder);
            builder.AppendLine();
            AppendRoutePostCore(builder);
            builder.AppendLine();
            AppendRouteRemoteHelpers(builder);
            builder.AppendLine("}");
        }

        private static void AppendRouteCallCore(StringBuilder builder)
        {
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask CallCoreAsync<TRequest>(");
            builder.AppendLine("        global::System.Func<TActor, TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask> method,");
            builder.AppendLine("        string methodName,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (_actors.Runtime.GetState(_actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.AppendLine("            await _actors.Runtime.TellAsync<TActor>(");
            builder.AppendLine("                _actorId, (actor, ct) => method(actor, request, ct), cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            return;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(methodName, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        var invocation = CreateRemoteInvocation(methodName, request, node, out var correlationId);");
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _actors.Remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(");
            builder.AppendLine("                result, _actorId, GeneratedActorMetadata<TActor>.ActorName, methodName, node, correlationId);");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.AppendLine("        {");
            builder.AppendLine("            _actors.DirectoryCache.Remove(_actorId);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<TResult> CallCoreAsync<TRequest, TResult>(");
            builder.AppendLine("        global::System.Func<TActor, TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<TResult>> method,");
            builder.AppendLine("        string methodName,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (_actors.Runtime.GetState(_actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.AppendLine("            return await _actors.Runtime.AskAsync<TActor, TResult>(");
            builder.AppendLine("                _actorId, (actor, ct) => method(actor, request, ct), cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(methodName, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        var invocation = CreateRemoteInvocation(methodName, request, node, out var correlationId);");
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _actors.Remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(");
            builder.AppendLine("                result, _actorId, GeneratedActorMetadata<TActor>.ActorName, methodName, node, correlationId);");
            builder.AppendLine("            return _actors.Serializer.Deserialize<TResult>(result.Payload);");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.AppendLine("        {");
            builder.AppendLine("            _actors.DirectoryCache.Remove(_actorId);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
        }

        private static void AppendRoutePostCore(StringBuilder builder)
        {
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask PostCoreAsync<TRequest>(");
            builder.AppendLine("        global::System.Func<TActor, TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask> method,");
            builder.AppendLine("        string methodName,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
            builder.AppendLine("        if (_actors.Runtime.GetState(_actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.AppendLine("            var localResult = _actors.Runtime.TryTell<TActor>(");
            builder.AppendLine("                _actorId, (actor, ct) => method(actor, request, ct), cancellationToken);");
            builder.AppendLine("            if (localResult == global::Lakona.Game.Server.Actors.ActorTellResult.Accepted)");
            builder.AppendLine("            {");
            builder.AppendLine("                return;");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine("            var localStatus = localResult == global::Lakona.Game.Server.Actors.ActorTellResult.MailboxFull");
            builder.AppendLine("                ? global::Lakona.Game.Server.Actors.ActorCallStatus.Backpressure");
            builder.AppendLine("                : localResult == global::Lakona.Game.Server.Actors.ActorTellResult.ActorNotFound");
            builder.AppendLine("                    ? global::Lakona.Game.Server.Actors.ActorCallStatus.ActorNotFound");
            builder.AppendLine("                    : global::Lakona.Game.Server.Actors.ActorCallStatus.Failed;");
            builder.AppendLine("            throw new global::Lakona.Game.Server.Actors.ActorCallException(");
            builder.AppendLine("                localStatus, _actorId, GeneratedActorMetadata<TActor>.ActorName, methodName,");
            builder.AppendLine("                \"Routed local actor post was rejected with result \" + localResult + \".\");");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(methodName, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        var invocation = CreateRemoteInvocation(methodName, request, node, out var correlationId);");
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _actors.Remote.TellAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(");
            builder.AppendLine("                result, _actorId, GeneratedActorMetadata<TActor>.ActorName, methodName, node, correlationId);");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.AppendLine("        {");
            builder.AppendLine("            _actors.DirectoryCache.Remove(_actorId);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
        }

        private static void AppendRouteRemoteHelpers(StringBuilder builder)
        {
            builder.AppendLine("    private global::Lakona.Game.Server.Actors.RemoteActorInvocation CreateRemoteInvocation<TRequest>(");
            builder.AppendLine("        string methodName,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::Lakona.Game.Cluster.NodeId node,");
            builder.AppendLine("        out string correlationId)");
            builder.AppendLine("    {");
            builder.AppendLine("        var payload = _actors.Serializer.Serialize(request);");
            builder.AppendLine("        correlationId = global::System.Guid.NewGuid().ToString(\"N\");");
            builder.AppendLine("        var deadline = global::System.DateTimeOffset.UtcNow.Add(_actors.Options.DefaultTimeout);");
            builder.AppendLine("        return new global::Lakona.Game.Server.Actors.RemoteActorInvocation(");
            builder.AppendLine("            node, _actorId, GeneratedActorMetadata<TActor>.ActorName, methodName, payload, deadline, correlationId);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Cluster.NodeId> ResolveNodeAsync(");
            builder.AppendLine("        string methodName,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (!_actors.DirectoryCache.TryGet(_actorId, out var node))");
            builder.AppendLine("        {");
            builder.AppendLine("            var record = await _actors.Directory.ResolveAsync(_actorId, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            if (record is null)");
            builder.AppendLine("            {");
            builder.AppendLine("                throw new global::Lakona.Game.Server.Actors.ActorNotFoundException(");
            builder.AppendLine("                    _actorId, GeneratedActorMetadata<TActor>.ActorName, methodName,");
            builder.AppendLine("                    \"Actor was not found in actor directory.\");");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine("            node = record.Node;");
            builder.AppendLine("            _actors.DirectoryCache.Set(_actorId, node);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        return node;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static bool IsLocationFailure(global::Lakona.Game.Server.Actors.ActorCallException exception)");
            builder.AppendLine("    {");
            builder.AppendLine("        return exception.Status == global::Lakona.Game.Server.Actors.ActorCallStatus.ActorNotFound");
            builder.AppendLine("            || exception.Status == global::Lakona.Game.Server.Actors.ActorCallStatus.NodeUnavailable;");
            builder.AppendLine("    }");
        }
    }
}
