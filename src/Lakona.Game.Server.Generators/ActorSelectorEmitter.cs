using System.Text;

namespace Lakona.Game.Server.Generators
{
    public sealed partial class TypedActorGenerator
    {
        private static void AppendLocalActorSelector(StringBuilder builder)
        {
            builder.AppendLine("public readonly struct LocalActor<TActor>");
            builder.AppendLine("    where TActor : global::Lakona.Game.Server.Actors.Actor");
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.ActorId _actorId;");
            builder.AppendLine();
            builder.AppendLine("    internal LocalActor(global::Lakona.Game.Server.Actors.IActorRuntime runtime, global::Lakona.Game.Server.Actors.ActorId actorId)");
            builder.AppendLine("    {");
            builder.AppendLine("        _runtime = runtime;");
            builder.AppendLine("        _actorId = actorId;");
            builder.AppendLine("    }");
            builder.AppendLine();
            AppendActorCallApi(builder);
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask CallCoreAsync<TRequest>(");
            builder.AppendLine("        global::System.Func<TActor, TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask> method,");
            builder.AppendLine("        string methodName,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        return _runtime.TellAsync<TActor>(_actorId, (actor, ct) => method(actor, request, ct), cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask<TResult> CallCoreAsync<TRequest, TResult>(");
            builder.AppendLine("        global::System.Func<TActor, TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<TResult>> method,");
            builder.AppendLine("        string methodName,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        return _runtime.AskAsync<TActor, TResult>(_actorId, (actor, ct) => method(actor, request, ct), cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask PostCoreAsync<TRequest>(");
            builder.AppendLine("        global::System.Func<TActor, TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask> method,");
            builder.AppendLine("        string methodName,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
            builder.AppendLine("        var result = _runtime.TryTell<TActor>(_actorId, (actor, ct) => method(actor, request, ct), cancellationToken);");
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
            builder.AppendLine("            status, _actorId, GeneratedActorMetadata<TActor>.ActorName, methodName,");
            builder.AppendLine("            \"Local actor post was rejected with result \" + result + \".\");");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static void AppendActorPlacementSelector(StringBuilder builder)
        {
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
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Server.Actors.ActorPlacementResult> CreateAsync(");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        return _placement.PlaceAsync<TActor, TKey>(");
            builder.AppendLine("            _id, global::Lakona.Game.Server.Actors.ActorPlacementCreateMode.Create, cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Server.Actors.ActorPlacementResult> EnsureAsync(");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        return _placement.PlaceAsync<TActor, TKey>(");
            builder.AppendLine("            _id, global::Lakona.Game.Server.Actors.ActorPlacementCreateMode.Ensure, cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }
    }
}
