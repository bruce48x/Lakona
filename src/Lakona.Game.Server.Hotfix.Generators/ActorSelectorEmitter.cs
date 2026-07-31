using System.Text;
using static Lakona.Game.Server.Hotfix.Generators.HotfixActorGenerator;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class ActorSelectorEmitter
    {
        internal static void AppendLocalActorSelector(StringBuilder builder)
        {
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// Identifies an actor activation that the caller has proven is hosted by the current process.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("/// <typeparam name=\"TActor\">The actor implementation type.</typeparam>");
            builder.AppendLine("public readonly struct LocalActor<TActor>");
            builder.AppendLine("    where TActor : global::Lakona.Game.Server.Actors.Actor");
            builder.AppendLine("{");
            builder.AppendLine("    private readonly ActorAccess _actors;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.ActorId _actorId;");
            builder.AppendLine();
            builder.AppendLine("    internal LocalActor(");
            builder.AppendLine("        ActorAccess actors,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.ActorId actorId)");
            builder.AppendLine("    {");
            builder.AppendLine("        _actors = actors;");
            builder.AppendLine("        _actorId = actorId;");
            builder.AppendLine("    }");
            builder.AppendLine();
            ActorAccessEmitter.AppendActorCallApi(builder);
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask CallCoreAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        return TellAsync(method, request, cancellationToken);");
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
            builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
            builder.AppendLine("        var result = TryTell(method, request, cancellationToken);");
            builder.AppendLine("        if (result == global::Lakona.Game.Server.Actors.ActorTellResult.Accepted)");
            builder.AppendLine("        {");
            builder.AppendLine("            return default;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var status = result == global::Lakona.Game.Server.Actors.ActorTellResult.MailboxFull");
            builder.AppendLine("            ? global::Lakona.Game.Server.Actors.ActorCallStatus.Backpressure");
            builder.AppendLine("            : result == global::Lakona.Game.Server.Actors.ActorTellResult.ActorNotFound");
            builder.AppendLine("                ? global::Lakona.Game.Server.Actors.ActorCallStatus.ActorNotFound");
            builder.AppendLine("                : global::Lakona.Game.Server.Actors.ActorCallStatus.Failed;");
            builder.AppendLine("        throw new global::Lakona.Game.Server.Actors.ActorCallException(");
            builder.AppendLine("            status,");
            builder.AppendLine("            _actorId,");
            builder.AppendLine("            GeneratedActorMetadata<TActor>.ActorName,");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            \"Local actor post was rejected with result \" + result + \".\");");
            builder.AppendLine("    }");
            builder.AppendLine();
            AppendLocalTellMethod(builder);
            builder.AppendLine();
            AppendLocalTryTellMethod(builder);
            builder.AppendLine();
            AppendLocalAskMethod(builder);
            builder.AppendLine();
            builder.AppendLine("    internal ActorAccess Actors => _actors;");
            builder.AppendLine("}");
        }

        private static void AppendLocalTellMethod(StringBuilder builder)
        {
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask TellAsync<TRequest>(");
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
            builder.AppendLine("            cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
        }

        private static void AppendLocalTryTellMethod(StringBuilder builder)
        {
            builder.AppendLine("    private global::Lakona.Game.Server.Actors.ActorTellResult TryTell<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtimeAccessor = _actors.HotfixRuntime;");
            builder.AppendLine("        return global::Lakona.Game.Server.Actors.HotfixActorMailboxDispatch.TryTell<TActor, TRequest>(");
            builder.AppendLine("            _actors.Runtime,");
            builder.AppendLine("            _actorId,");
            builder.AppendLine("            runtimeAccessor,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
        }

        private static void AppendLocalAskMethod(StringBuilder builder)
        {
            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<TResult> AskAsync<TRequest, TResult>(");
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
            builder.AppendLine("            cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
        }

        internal static void AppendActorPlacementSelector(StringBuilder builder)
        {
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// Provides cluster-aware creation operations for one logical actor identity.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("/// <typeparam name=\"TActor\">The actor implementation type.</typeparam>");
            builder.AppendLine("/// <typeparam name=\"TKey\">The actor's stable business-key type.</typeparam>");
            builder.AppendLine("public readonly struct ActorPlacement<TActor, TKey>");
            builder.AppendLine("    where TActor : global::Lakona.Game.Server.Actors.Actor<TKey>");
            builder.AppendLine("    where TKey : notnull");
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorPlacementService _placement;");
            builder.AppendLine("    private readonly TKey _id;");
            builder.AppendLine();
            builder.AppendLine("    internal ActorPlacement(global::Lakona.Game.Server.Actors.IActorPlacementService placement, TKey id)");
            builder.AppendLine("    {");
            builder.AppendLine("        _placement = placement;");
            builder.AppendLine("        _id = id;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    /// <summary>");
            builder.AppendLine("    /// Creates a new activation and fails when the logical actor already has an activation.");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine("    /// <param name=\"cancellationToken\">Cancels placement and activation.</param>");
            builder.AppendLine("    /// <returns>The newly created activation and its owner.</returns>");
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Server.Actors.ActorPlacementResult> CreateAsync(");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        return _placement.PlaceAsync<TActor, TKey>(");
            builder.AppendLine("            _id,");
            builder.AppendLine("            global::Lakona.Game.Server.Actors.ActorPlacementCreateMode.Create,");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    /// <summary>");
            builder.AppendLine("    /// Returns the existing activation or creates one when the logical actor is absent.");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine("    /// <param name=\"cancellationToken\">Cancels placement and activation.</param>");
            builder.AppendLine("    /// <returns>The existing or newly created activation and its owner.</returns>");
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Server.Actors.ActorPlacementResult> EnsureAsync(");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        return _placement.PlaceAsync<TActor, TKey>(");
            builder.AppendLine("            _id,");
            builder.AppendLine("            global::Lakona.Game.Server.Actors.ActorPlacementCreateMode.Ensure,");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }
    }
}
