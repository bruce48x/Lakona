using System.Text;
using static Lakona.Game.Server.Hotfix.Generators.HotfixActorGenerator;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class ActorRouteEmitter
    {
        internal static void AppendActorRouteSelector(StringBuilder builder)
        {
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// Routes behavior calls to the current activation of an existing logical actor.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("/// <typeparam name=\"TActor\">The actor implementation type.</typeparam>");
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
            ActorAccessEmitter.AppendActorCallApi(builder);
            builder.AppendLine();
            AppendRouteCallCoreMethods(builder);
            builder.AppendLine();
            AppendRouteTellMethod(builder);
            builder.AppendLine();
            AppendRouteAskMethod(builder);
            builder.AppendLine();
            AppendRouteLocalInvocationMethods(builder);
            builder.AppendLine();
            AppendRouteRemoteHelpers(builder);
            builder.AppendLine();
            builder.AppendLine("    internal ActorAccess Actors => _actors;");
            builder.AppendLine("}");
        }

        private static void AppendRouteCallCoreMethods(StringBuilder builder)
        {
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask CallCoreAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (_actors.Runtime.GetState(_actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.AppendLine("            await InvokeLocalTellAsync(method, request, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            return;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(method.MethodName, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        var invocation = CreateRemoteInvocation(method, request, node);");
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _actors.Remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(");
            builder.AppendLine("                result, _actorId, GeneratedActorMetadata<TActor>.ActorName, method.MethodName, node);");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.AppendLine("        {");
            builder.AppendLine("            _actors.DirectoryCache.Remove(_actorId);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask<TResult> CallCoreAsync<TRequest, TResult>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        return AskAsync<TRequest, TResult>(method, request, cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask PostCoreAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        return TellAsync(method, request, cancellationToken);");
            builder.AppendLine("    }");
        }

        private static void AppendRouteTellMethod(StringBuilder builder)
        {
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask TellAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (_actors.Runtime.GetState(_actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.AppendLine("            await InvokeLocalTellAsync(method, request, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            return;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(method.MethodName, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        var invocation = CreateRemoteInvocation(method, request, node);");
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _actors.Remote.TellAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(");
            builder.AppendLine("                result, _actorId, GeneratedActorMetadata<TActor>.ActorName, method.MethodName, node);");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.AppendLine("        {");
            builder.AppendLine("            _actors.DirectoryCache.Remove(_actorId);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
        }

        private static void AppendRouteAskMethod(StringBuilder builder)
        {
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<TResult> AskAsync<TRequest, TResult>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (_actors.Runtime.GetState(_actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.AppendLine("            return await InvokeLocalAskAsync<TRequest, TResult>(method, request, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(method.MethodName, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        var invocation = CreateRemoteInvocation<TRequest, TResult>(method, request, node);");
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _actors.Remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            return global::Lakona.Game.Server.Actors.RemoteActorCall.GetReply<TResult>(");
            builder.AppendLine("                result, _actorId, GeneratedActorMetadata<TActor>.ActorName, method.MethodName, node);");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.AppendLine("        {");
            builder.AppendLine("            _actors.DirectoryCache.Remove(_actorId);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
        }

        private static void AppendRouteLocalInvocationMethods(StringBuilder builder)
        {
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask InvokeLocalTellAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtimeAccessor = _actors.HotfixRuntime;");
            builder.AppendLine("        await global::Lakona.Game.Server.Actors.HotfixActorMailboxDispatch.TellAsync<TActor, TRequest>(");
            builder.AppendLine("            _actors.Runtime,");
            builder.AppendLine("            _actorId,");
            builder.AppendLine("            runtimeAccessor,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            _actors.GetOptionalModule<global::Lakona.Game.Server.Hosting.IDistributedWorkAdmissionGate>(),");
            builder.AppendLine("            cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<TResult> InvokeLocalAskAsync<TRequest, TResult>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtimeAccessor = _actors.HotfixRuntime;");
            builder.AppendLine("        return await global::Lakona.Game.Server.Actors.HotfixActorMailboxDispatch.AskAsync<TActor, TRequest, TResult>(");
            builder.AppendLine("            _actors.Runtime,");
            builder.AppendLine("            _actorId,");
            builder.AppendLine("            runtimeAccessor,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            _actors.GetOptionalModule<global::Lakona.Game.Server.Hosting.IDistributedWorkAdmissionGate>(),");
            builder.AppendLine("            cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
        }

        private static void AppendRouteRemoteHelpers(StringBuilder builder)
        {
            builder.AppendLine("    private global::Lakona.Game.Server.Actors.RemoteActorInvocation CreateRemoteInvocation<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::Lakona.Game.Cluster.NodeId node)");
            builder.AppendLine("    {");
            builder.AppendLine("        var deadline = global::System.DateTimeOffset.UtcNow.Add(_actors.Options.DefaultTimeout);");
            builder.AppendLine("        return global::Lakona.Game.Server.Actors.RemoteActorInvocation.Create(");
            builder.AppendLine("            node,");
            builder.AppendLine("            _actorId,");
            builder.AppendLine("            GeneratedActorMetadata<TActor>.ActorName,");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            deadline);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::Lakona.Game.Server.Actors.RemoteActorInvocation CreateRemoteInvocation<TRequest, TResult>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::Lakona.Game.Cluster.NodeId node)");
            builder.AppendLine("    {");
            builder.AppendLine("        var deadline = global::System.DateTimeOffset.UtcNow.Add(_actors.Options.DefaultTimeout);");
            builder.AppendLine("        return global::Lakona.Game.Server.Actors.RemoteActorInvocation.Create<TRequest, TResult>(");
            builder.AppendLine("            node,");
            builder.AppendLine("            _actorId,");
            builder.AppendLine("            GeneratedActorMetadata<TActor>.ActorName,");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            deadline);");
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
            builder.AppendLine("                    _actorId,");
            builder.AppendLine("                    GeneratedActorMetadata<TActor>.ActorName,");
            builder.AppendLine("                    methodName,");
            builder.AppendLine("                    \"Actor was not found in actor directory.\");");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine("            node = record.Node;");
            builder.AppendLine("            _actors.DirectoryCache.Set(record);");
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
