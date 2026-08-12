using System.Text;
using static Lakona.Game.Server.Hotfix.Generators.HotfixActorGenerator;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class StartupActorEmitter
    {
        internal static void AppendStartupActorSelector(StringBuilder builder)
        {
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// Routes behavior calls through the lifecycle of a registered startup actor group.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("/// <typeparam name=\"TActor\">The startup actor implementation type.</typeparam>");
            builder.AppendLine("/// <typeparam name=\"TKey\">The startup group-key type.</typeparam>");
            builder.AppendLine("public readonly struct StartupActor<TActor, TKey>");
            builder.AppendLine("    where TActor : global::Lakona.Game.Server.Actors.Actor");
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IStartupActorInvoker _startup;");
            builder.AppendLine("    private readonly ActorAccess _actors;");
            builder.AppendLine("    private readonly TKey _key;");
            builder.AppendLine();
            builder.AppendLine("    internal StartupActor(");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IStartupActorInvoker startup,");
            builder.AppendLine("        ActorAccess actors,");
            builder.AppendLine("        TKey key)");
            builder.AppendLine("    {");
            builder.AppendLine("        _startup = startup;");
            builder.AppendLine("        _actors = actors;");
            builder.AppendLine("        _key = key;");
            builder.AppendLine("    }");
            builder.AppendLine();
            ActorAccessEmitter.AppendActorCallApi(builder);
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask CallCoreAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtime = _actors.Runtime;");
            builder.AppendLine("        var runtimeAccessor = _actors.HotfixRuntime;");
            builder.AppendLine("        var admissionGate = _actors.GetOptionalModule<global::Lakona.Game.Server.Hosting.IDistributedWorkAdmissionGate>();");
            builder.AppendLine("        return _startup.CallAsync<TActor, TKey, TRequest>(");
            builder.AppendLine("            _key,");
            builder.AppendLine("            GeneratedActorMetadata<TActor>.ActorName,");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            (actorId, value, ct) => global::Lakona.Game.Server.Actors.HotfixActorMailboxDispatch.TellAsync<TActor, TRequest>(");
            builder.AppendLine("                runtime, actorId, runtimeAccessor, method.RemoteMethodId, value, admissionGate, ct),");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask<TResult> CallCoreAsync<TRequest, TResult>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtime = _actors.Runtime;");
            builder.AppendLine("        var runtimeAccessor = _actors.HotfixRuntime;");
            builder.AppendLine("        var admissionGate = _actors.GetOptionalModule<global::Lakona.Game.Server.Hosting.IDistributedWorkAdmissionGate>();");
            builder.AppendLine("        return _startup.CallAsync<TActor, TKey, TRequest, TResult>(");
            builder.AppendLine("            _key,");
            builder.AppendLine("            GeneratedActorMetadata<TActor>.ActorName,");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            (actorId, value, ct) => global::Lakona.Game.Server.Actors.HotfixActorMailboxDispatch.AskAsync<TActor, TRequest, TResult>(");
            builder.AppendLine("                runtime, actorId, runtimeAccessor, method.RemoteMethodId, value, admissionGate, ct),");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask PostCoreAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtime = _actors.Runtime;");
            builder.AppendLine("        var runtimeAccessor = _actors.HotfixRuntime;");
            builder.AppendLine("        var admissionGate = _actors.GetOptionalModule<global::Lakona.Game.Server.Hosting.IDistributedWorkAdmissionGate>();");
            builder.AppendLine("        return _startup.PostAsync<TActor, TKey, TRequest>(");
            builder.AppendLine("            _key,");
            builder.AppendLine("            GeneratedActorMetadata<TActor>.ActorName,");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            (actorId, value, ct) => global::System.Threading.Tasks.ValueTask.FromResult(global::Lakona.Game.Server.Actors.HotfixActorMailboxDispatch.TryTell<TActor, TRequest>(");
            builder.AppendLine("                runtime, actorId, runtimeAccessor, method.RemoteMethodId, value, admissionGate, ct)),");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    internal ActorAccess Actors => _actors;");
            builder.AppendLine("}");
        }

    }
}
