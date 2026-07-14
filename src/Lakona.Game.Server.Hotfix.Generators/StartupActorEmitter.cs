using System.Text;

namespace Lakona.Game.Server.Hotfix.Generators
{
    public sealed partial class HotfixGenerator
    {
        private static void AppendStartupActorSelector(StringBuilder builder)
        {
            builder.AppendLine("public readonly struct StartupActor<TActor, TKey>");
            builder.AppendLine("    where TActor : global::Lakona.Game.Server.Actors.Actor");
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IStartupActorInvoker _startup;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.AppendLine("    private readonly TKey _key;");
            builder.AppendLine();
            builder.AppendLine("    internal StartupActor(");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IStartupActorInvoker startup,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime runtime,");
            builder.AppendLine("        TKey key)");
            builder.AppendLine("    {");
            builder.AppendLine("        _startup = startup;");
            builder.AppendLine("        _runtime = runtime;");
            builder.AppendLine("        _key = key;");
            builder.AppendLine("    }");
            builder.AppendLine();
            AppendActorCallApi(builder);
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask CallCoreAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtime = _runtime;");
            builder.AppendLine("        return _startup.CallAsync<TActor, TKey, TRequest>(");
            builder.AppendLine("            _key,");
            builder.AppendLine("            GeneratedActorMetadata<TActor>.ActorName,");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            (actorId, value, ct) => runtime.TellAsync<TActor>(");
            builder.AppendLine("                actorId,");
            AppendStartupDispatchLambda(builder, isResult: false, isTryTell: false);
            builder.AppendLine("                ct),");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask<TResult> CallCoreAsync<TRequest, TResult>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtime = _runtime;");
            builder.AppendLine("        return _startup.CallAsync<TActor, TKey, TRequest, TResult>(");
            builder.AppendLine("            _key,");
            builder.AppendLine("            GeneratedActorMetadata<TActor>.ActorName,");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            (actorId, value, ct) => runtime.AskAsync<TActor, TResult>(");
            builder.AppendLine("                actorId,");
            AppendStartupDispatchLambda(builder, isResult: true, isTryTell: false);
            builder.AppendLine("                ct),");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask PostCoreAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtime = _runtime;");
            builder.AppendLine("        return _startup.PostAsync<TActor, TKey, TRequest>(");
            builder.AppendLine("            _key,");
            builder.AppendLine("            GeneratedActorMetadata<TActor>.ActorName,");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            builder.AppendLine("            (actorId, value, ct) => global::System.Threading.Tasks.ValueTask.FromResult(runtime.TryTell<TActor>(");
            builder.AppendLine("                actorId,");
            AppendStartupDispatchLambda(builder, isResult: false, isTryTell: true);
            builder.AppendLine("                ct)),");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static void AppendStartupDispatchLambda(StringBuilder builder, bool isResult, bool isTryTell)
        {
            builder.Append("                (actor, innerCt) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeValueTaskAsync");
            if (isResult)
            {
                builder.Append("<TResult>");
            }

            builder.AppendLine("(");
            builder.AppendLine("                    typeof(TActor),");
            builder.AppendLine("                    method.MethodName,");
            builder.AppendLine("                    actor,");
            builder.AppendLine("                    method.PassCancellationToken");
            builder.AppendLine("                        ? new global::System.Type[] { typeof(TRequest), typeof(global::System.Threading.CancellationToken) }");
            builder.AppendLine("                        : new global::System.Type[] { typeof(TRequest) },");
            builder.AppendLine("                    method.PassCancellationToken");
            builder.AppendLine("                        ? new object[] { value, innerCt }");
            builder.Append("                        : new object[] { value }),");
            if (isTryTell)
            {
                builder.AppendLine();
            }
            else
            {
                builder.AppendLine();
            }
        }
    }
}
