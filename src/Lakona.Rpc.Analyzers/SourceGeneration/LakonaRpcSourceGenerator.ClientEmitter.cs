using System;
using System.Collections.Generic;
using System.Linq;

namespace Lakona.Rpc.Analyzers;

public sealed partial class LakonaRpcSourceGenerator
{
    private static class ClientSourceEmitter
    {
        public static string GenerateClient(RpcServiceModel service, string generatedNamespace)
        {
            var writer = new SourceWriter();
            writer.Header();
            writer.Line("using System;");
            writer.Line("using System.Threading;");
            writer.Line("using System.Threading.Tasks;");
            writer.Line($"using {CoreRuntimeUsing};");
            writer.Line();
            writer.OpenBlock($"namespace {generatedNamespace}");
            writer.OpenBlock($"public sealed class {Naming.GetClientTypeName(service.InterfaceName)} : {service.FullName}");
            writer.Line($"private const int ServiceId = {service.ServiceId};");

            foreach (var method in service.Methods)
            {
                var returnType = method.IsVoid ? "RpcVoid" : method.ReturnTypeName!;
                writer.Line($"private static readonly RpcMethod<{method.PayloadType}, {returnType}> {Naming.GetClientMethodFieldName(method.Name)} = new(ServiceId, {method.MethodId});");
            }

            writer.Line();
            writer.Line("private readonly IRpcClient _client;");
            writer.Line();
            writer.Line($"public {Naming.GetClientTypeName(service.InterfaceName)}(IRpcClient client) {{ _client = client; }}");
            writer.Line();

            foreach (var method in service.Methods)
            {
                var paramSig = Naming.GetParameterSignature(method.Parameters);
                var sigWithCt = string.IsNullOrEmpty(paramSig)
                    ? $"{method.Name}(CancellationToken ct)"
                    : $"{method.Name}({paramSig}, CancellationToken ct)";
                var fieldName = Naming.GetClientMethodFieldName(method.Name);

                if (method.IsVoid)
                {
                    writer.OpenBlock($"public ValueTask {method.Name}({paramSig})");
                    writer.Line($"return {method.Name}({method.PayloadValue}, CancellationToken.None);");
                    writer.CloseBlock();
                    writer.Line();
                    writer.OpenBlock($"public ValueTask {sigWithCt}");
                    writer.Line($"return RpcVoidTask.FromResult(_client.CallAsync({fieldName}, {method.PayloadValue}, ct));");
                    writer.CloseBlock();
                }
                else
                {
                    writer.OpenBlock($"public ValueTask<{method.ReturnTypeName}> {method.Name}({paramSig})");
                    writer.Line($"return {method.Name}({method.PayloadValue}, CancellationToken.None);");
                    writer.CloseBlock();
                    writer.Line();
                    writer.OpenBlock($"public ValueTask<{method.ReturnTypeName}> {sigWithCt}");
                    writer.Line($"return _client.CallAsync({fieldName}, {method.PayloadValue}, ct);");
                    writer.CloseBlock();
                }

                writer.Line();
            }

            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock($"public static class {Naming.GetClientExtensionTypeName(service.InterfaceName)}");
            writer.OpenBlock($"public static {service.FullName} {Naming.GetClientFactoryMethodName(service.InterfaceName)}(this IRpcClient client)");
            writer.Line("if (client is null) throw new ArgumentNullException(nameof(client));");
            writer.Line($"return new {Naming.GetClientTypeName(service.InterfaceName)}(client);");
            writer.CloseBlock();
            writer.CloseBlock();
            writer.CloseBlock();
            return writer.ToString();
        }

        public static string GenerateNotificationBinder(RpcServiceModel service, string generatedNamespace)
        {
            var writer = new SourceWriter();
            writer.Header();
            writer.Line("using System;");
            writer.Line($"using {CoreRuntimeUsing};");
            writer.Line();
            writer.OpenBlock($"namespace {generatedNamespace}");
            writer.OpenBlock($"public static class {Naming.GetNotificationBinderTypeName(service.NotificationContractInterfaceName!)}");
            writer.Line($"private const int ServiceId = {service.ServiceId};");

            foreach (var method in service.NotificationMethods)
                writer.Line($"private static readonly RpcNotificationMethod<{method.PayloadType}> {Naming.GetNotificationMethodFieldName(method.Name)} = new(ServiceId, {method.MethodId});");

            writer.Line();
            writer.OpenBlock($"public static void Bind(IRpcClient client, {service.NotificationContractFullName} receiver)");
            foreach (var method in service.NotificationMethods)
            {
                writer.OpenBlock($"client.RegisterNotificationHandler({Naming.GetNotificationMethodFieldName(method.Name)}, arg =>");
                if (method.ReturnsValueTask)
                {
                    writer.Line(method.AcceptsCancellationToken
                        ? $"return receiver.{method.Name}(arg, global::System.Threading.CancellationToken.None);"
                        : $"return receiver.{method.Name}(arg);");
                }
                else
                {
                    writer.Line(method.AcceptsCancellationToken
                        ? $"receiver.{method.Name}(arg, global::System.Threading.CancellationToken.None);"
                        : $"receiver.{method.Name}(arg);");
                    writer.Line("return default;");
                }
                writer.CloseBlock(");");
            }

            writer.CloseBlock();
            writer.CloseBlock();
            writer.CloseBlock();
            return writer.ToString();
        }

        public static string GenerateFacade(List<RpcServiceModel> services, string generatedNamespace)
        {
            var groups = services
                .GroupBy(static service => service.ApiGroupName)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => new FacadeGroupModel(group.Key, group.OrderBy(static service => service.InterfaceName, StringComparer.Ordinal).ToList()))
                .ToList();

            var notificationContracts = services.Where(static service => service.HasNotificationContract).OrderBy(static service => service.NotificationContractInterfaceName, StringComparer.Ordinal).ToList();
            var writer = new SourceWriter();
            writer.Header();
            writer.Line("using System;");
            writer.Line("using System.Threading;");
            writer.Line("using System.Threading.Tasks;");
            writer.Line($"using {ClientRuntimeUsing};");
            writer.Line($"using {CoreRuntimeUsing};");
            writer.Line();
            writer.OpenBlock($"namespace {generatedNamespace}");
            writer.OpenBlock("public sealed class RpcApi");
            writer.OpenBlock("public RpcApi(IRpcClient client)");
            writer.Line("if (client is null) throw new ArgumentNullException(nameof(client));");
            foreach (var group in groups)
                writer.Line($"{group.GroupName} = new {group.GroupName}RpcGroup(client);");
            writer.CloseBlock();
            writer.Line();
            foreach (var group in groups)
                writer.Line($"public {group.GroupName}RpcGroup {group.GroupName} {{ get; }}");
            writer.CloseBlock();
            writer.Line();

            foreach (var group in groups)
            {
                writer.OpenBlock($"public sealed class {group.GroupName}RpcGroup");
                writer.OpenBlock($"public {group.GroupName}RpcGroup(IRpcClient client)");
                writer.Line("if (client is null) throw new ArgumentNullException(nameof(client));");
                foreach (var service in group.Services)
                    writer.Line($"{service.ApiName} = new {Naming.GetClientTypeName(service.InterfaceName)}(client);");
                writer.CloseBlock();
                writer.Line();
                foreach (var service in group.Services)
                    writer.Line($"public {service.FullName} {service.ApiName} {{ get; }}");
                writer.CloseBlock();
                writer.Line();
            }

            writer.OpenBlock("public sealed class RpcClient : IAsyncDisposable");
            writer.Line("private readonly RpcClientRuntime _runtime;");
            if (notificationContracts.Count > 0)
                writer.Line("private readonly RpcNotificationBindings? _notifications;");
            writer.Line($"private global::{generatedNamespace}.RpcApi? _api;");
            writer.Line();
            writer.OpenBlock("public RpcClient(RpcClientOptions options)");
            writer.Line("Options = options ?? throw new ArgumentNullException(nameof(options));");
            writer.Line("_runtime = new RpcClientRuntime(options);");
            writer.CloseBlock();
            writer.Line();

            if (notificationContracts.Count > 0)
            {
                writer.OpenBlock("public RpcClient(RpcClientOptions options, RpcNotificationBindings notifications) : this(options)");
                writer.Line("_notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));");
                writer.CloseBlock();
                writer.Line();
                EmitNotificationTypes(writer, notificationContracts, generatedNamespace);
            }

            writer.OpenBlock("public event Action<Exception?>? Disconnected");
            writer.Line("add => _runtime.Disconnected += value;");
            writer.Line("remove => _runtime.Disconnected -= value;");
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock("public event Action<RpcUnhandledNotificationContext>? UnhandledNotificationReceived");
            writer.Line("add => _runtime.UnhandledNotificationReceived += value;");
            writer.Line("remove => _runtime.UnhandledNotificationReceived -= value;");
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock("public event Action<RpcNotificationHandlerExceptionContext>? NotificationHandlerException");
            writer.Line("add => _runtime.NotificationHandlerException += value;");
            writer.Line("remove => _runtime.NotificationHandlerException -= value;");
            writer.CloseBlock();
            writer.Line();
            writer.Line("public RpcClientOptions Options { get; }");
            writer.Line("public global::Lakona.Rpc.Client.RpcClientRuntime Runtime => _runtime;");
            writer.Line($"public global::{generatedNamespace}.RpcApi Api => _api ??= new global::{generatedNamespace}.RpcApi(_runtime);");
            writer.Line();
            writer.OpenBlock("public ValueTask ConnectAsync(CancellationToken ct = default)");
            if (notificationContracts.Count > 0)
            {
                writer.Line("if (_notifications is not null)");
                writer.Line("    _notifications.Bind(_runtime);");
            }
            writer.Line("return _runtime.StartAsync(ct);");
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock("public ValueTask DisposeAsync()");
            writer.Line("return _runtime.DisposeAsync();");
            writer.CloseBlock();
            writer.CloseBlock();
            writer.CloseBlock();
            return writer.ToString();
        }

        public static string GenerateGameClientWrapper(
            List<RpcServiceModel> services,
            string generatedNamespace)
        {
            var notificationContracts = services.Where(static service => service.HasNotificationContract).OrderBy(static service => service.NotificationContractInterfaceName, StringComparer.Ordinal).ToList();
            var writer = new SourceWriter();
            writer.Header();
            writer.Line("using System;");
            writer.Line("using System.Threading;");
            writer.Line("using System.Threading.Tasks;");
            writer.Line("using Lakona.Game.Client;");
            writer.Line("using Lakona.Game.Client.Sessions;");
            writer.Line();
            writer.OpenBlock($"namespace {generatedNamespace}");
            writer.OpenBlock("public sealed class LakonaGameClient : IAsyncDisposable");
            writer.Line("private readonly LakonaGameClientLifecycle _lifecycle;");
            writer.Line($"private readonly global::{generatedNamespace}.RpcApi _api;");
            writer.Line();
            writer.OpenBlock("public LakonaGameClient(LakonaGameClientOptions options, params object[] callbackReceivers)");
            writer.Line("if (options is null) throw new ArgumentNullException(nameof(options));");
            writer.Line("ValidateCallbackReceivers(callbackReceivers);");
            if (notificationContracts.Count > 0)
                writer.Line("_lifecycle = new LakonaGameClientLifecycle(options, client => CreateNotificationBindings(callbackReceivers).Bind(client));");
            else
                writer.Line("_lifecycle = new LakonaGameClientLifecycle(options);");
            writer.Line($"_api = new global::{generatedNamespace}.RpcApi(_lifecycle.Dispatcher);");
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock("public event Action<Exception?>? Disconnected");
            writer.Line("add => _lifecycle.Disconnected += value;");
            writer.Line("remove => _lifecycle.Disconnected -= value;");
            writer.CloseBlock();
            writer.Line();
            writer.Line("public ClientSessionSnapshot Snapshot => _lifecycle.Snapshot;");
            writer.Line("public bool ReliablePushEnabled => _lifecycle.ReliablePushEnabled;");
            writer.Line("public bool ReliablePushAckRequired => _lifecycle.ReliablePushAckRequired;");
            writer.Line("public global::System.TimeSpan SessionResumeWindow => _lifecycle.SessionResumeWindow;");
            writer.Line();
            writer.OpenBlock("public ValueTask StartSessionAsync(string sessionId, CancellationToken cancellationToken = default)");
            writer.Line("return _lifecycle.StartSessionAsync(sessionId, cancellationToken);");
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock($"public global::{generatedNamespace}.RpcApi Api");
            writer.OpenBlock("get");
            writer.Line("_lifecycle.EnsureApiReady();");
            writer.Line("return _api;");
            writer.CloseBlock();
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock("public ValueTask ConnectAsync(CancellationToken ct = default)");
            writer.Line("return _lifecycle.ConnectAsync(ct);");
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock("public ValueTask DisposeAsync()");
            writer.Line("return _lifecycle.DisposeAsync();");
            writer.CloseBlock();
            writer.Line();
            if (notificationContracts.Count > 0)
            {
                writer.OpenBlock($"private static global::{generatedNamespace}.RpcClient.RpcNotificationBindings CreateNotificationBindings(object[] callbackReceivers)");
                writer.Line("ValidateCallbackReceivers(callbackReceivers);");
                writer.Line($"var bindings = new global::{generatedNamespace}.RpcClient.RpcNotificationBindings();");
                writer.OpenBlock("foreach (var receiver in callbackReceivers)");
                foreach (var service in notificationContracts)
                {
                    var receiver = Naming.GetNotificationReceiverParamName(service.NotificationContractInterfaceName!);
                    writer.OpenBlock($"if (receiver is {service.NotificationContractFullName} {receiver})");
                    writer.Line($"bindings.Add({receiver});");
                    writer.CloseBlock();
                }
                writer.CloseBlock();
                writer.Line("return bindings;");
                writer.CloseBlock();
                writer.Line();
            }

            writer.OpenBlock("private static void ValidateCallbackReceivers(object[] callbackReceivers)");
            writer.Line("if (callbackReceivers is null) throw new ArgumentNullException(nameof(callbackReceivers));");
            writer.OpenBlock("foreach (var receiver in callbackReceivers)");
            writer.OpenBlock("if (receiver is null)");
            writer.Line("throw new ArgumentNullException(nameof(callbackReceivers), \"Callback receiver cannot be null.\");");
            writer.CloseBlock();
            writer.CloseBlock();
            writer.CloseBlock();
            writer.CloseBlock();
            writer.CloseBlock();
            return writer.ToString();
        }

        private static void EmitNotificationTypes(SourceWriter writer, List<RpcServiceModel> notificationContracts, string generatedNamespace)
        {
            writer.OpenBlock("public sealed class RpcNotificationBindings");
            foreach (var service in notificationContracts)
            {
                var field = "_" + Naming.GetNotificationReceiverParamName(service.NotificationContractInterfaceName!);
                var parameter = Naming.GetNotificationReceiverParamName(service.NotificationContractInterfaceName!);
                writer.Line($"private {service.NotificationContractFullName}? {field};");
                writer.OpenBlock($"public void Add({service.NotificationContractFullName} {parameter})");
                writer.Line($"if ({parameter} is null) throw new ArgumentNullException(nameof({parameter}));");
                writer.OpenBlock($"if ({field} is not null)");
                writer.Line($"throw new InvalidOperationException(\"Notification receiver for '{service.NotificationContractInterfaceName}' is already registered.\");");
                writer.CloseBlock();
                writer.Line($"{field} = {parameter};");
                writer.CloseBlock();
                writer.Line();
            }

            writer.OpenBlock("internal void Bind(IRpcClient client)");
            writer.Line("if (client is null) throw new ArgumentNullException(nameof(client));");
            foreach (var service in notificationContracts)
            {
                var field = "_" + Naming.GetNotificationReceiverParamName(service.NotificationContractInterfaceName!);
                writer.OpenBlock($"if ({field} is not null)");
                writer.Line($"global::{generatedNamespace}.{Naming.GetNotificationBinderTypeName(service.NotificationContractInterfaceName!)}.Bind(client, {field});");
                writer.CloseBlock();
            }
            writer.CloseBlock();
            writer.CloseBlock();
            writer.Line();

            foreach (var service in notificationContracts)
            {
                writer.OpenBlock($"public abstract class {Naming.GetServiceTypeName(service.NotificationContractInterfaceName!)}Base : {service.NotificationContractFullName}");
                foreach (var method in service.NotificationMethods.OrderBy(static method => method.MethodId))
                {
                    writer.Line();
                    if (method.ReturnsValueTask)
                        writer.OpenBlock($"public virtual ValueTask {method.Name}({Naming.GetParameterSignature(method.Parameters)})");
                    else
                        writer.OpenBlock($"public virtual void {method.Name}({Naming.GetParameterSignature(method.Parameters)})");
                    if (method.ReturnsValueTask)
                        writer.Line("return default;");
                    writer.CloseBlock();
                }
                writer.CloseBlock();
                writer.Line();
            }
        }

        private static string EscapeString(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
