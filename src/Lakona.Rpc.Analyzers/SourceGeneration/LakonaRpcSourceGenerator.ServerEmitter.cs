using System;
using System.Collections.Generic;
using System.Linq;

namespace Lakona.Rpc.Analyzers;

public sealed partial class LakonaRpcSourceGenerator
{
    private static class ServerSourceEmitter
    {
        public static string GenerateBinder(RpcServiceModel service, string generatedNamespace)
        {
            var writer = new SourceWriter();
            writer.Header();
            writer.Line("using System;");
            writer.Line("using System.Threading.Tasks;");
            writer.Line($"using {CoreRuntimeUsing};");
            writer.Line($"using {ServerRuntimeUsing};");
            writer.Line();
            writer.OpenBlock($"namespace {generatedNamespace}");
            writer.OpenBlock($"public static class {Naming.GetBinderTypeName(service.InterfaceName)}");
            writer.Line($"private const int ServiceId = {service.ServiceId};");
            writer.Line();
            writer.OpenBlock($"public static void Bind(RpcServiceRegistry registry, {service.FullName} impl)");
            writer.Line("if (registry is null) throw new ArgumentNullException(nameof(registry));");
            writer.Line("if (impl is null) throw new ArgumentNullException(nameof(impl));");
            var serviceDisplayName = StripGlobalPrefix(service.FullName);
            writer.Line($"BindCore(registry.RegisterSingleton<{service.FullName}>(ServiceId, impl, serviceName: \"{serviceDisplayName}\"));");
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock($"public static void BindFactory(RpcServiceRegistry registry, Func<RpcConnectionInfo, {service.FullName}> implFactory)");
            writer.Line("if (registry is null) throw new ArgumentNullException(nameof(registry));");
            writer.Line("if (implFactory is null) throw new ArgumentNullException(nameof(implFactory));");
            writer.Line($"BindCore(registry.RegisterPerConnection<{service.FullName}>(ServiceId, (connection, _) => implFactory(connection), serviceName: \"{serviceDisplayName}\"));");
            writer.CloseBlock();
            if (service.HasNotificationContract)
            {
                writer.Line();
                writer.OpenBlock($"public static void Bind(RpcServiceRegistry registry, Func<{service.NotificationContractFullName}, {service.FullName}> implFactory)");
                writer.Line("if (registry is null) throw new ArgumentNullException(nameof(registry));");
                writer.Line("if (implFactory is null) throw new ArgumentNullException(nameof(implFactory));");
                writer.Line("BindFactory(registry, (_, callback) => implFactory(callback));");
                writer.CloseBlock();
                writer.Line();
                writer.OpenBlock($"public static void BindFactory(RpcServiceRegistry registry, Func<RpcConnectionInfo, {service.NotificationContractFullName}, {service.FullName}> implFactory)");
                writer.Line("if (registry is null) throw new ArgumentNullException(nameof(registry));");
                writer.Line("if (implFactory is null) throw new ArgumentNullException(nameof(implFactory));");
                writer.Line($"BindCore(registry.RegisterPerConnection<{service.FullName}>(ServiceId, (connection, notifications) => implFactory(connection, new {Naming.GetNotificationProxyTypeName(service.NotificationContractInterfaceName!)}(notifications)) ?? throw new InvalidOperationException(\"Service implementation factory returned null.\"), serviceName: \"{serviceDisplayName}\"));");
                writer.CloseBlock();
            }
            writer.Line();
            writer.OpenBlock($"private static void BindCore(RpcServiceRegistration<{service.FullName}> service)");
            foreach (var method in service.Methods)
            {
                if (method.IsVoid)
                {
                    writer.Line($"service.Register<{method.PayloadType}>({method.MethodId}, static (impl, arg, _) => impl.{method.Name}(arg), methodName: \"{method.Name}\");");
                }
                else
                {
                    writer.Line($"service.Register<{method.PayloadType}, {method.ReturnTypeName}>({method.MethodId}, static (impl, arg, _) => impl.{method.Name}(arg), methodName: \"{method.Name}\");");
                }
            }
            writer.CloseBlock();
            writer.CloseBlock();
            writer.CloseBlock();
            return writer.ToString();
        }

        private static string StripGlobalPrefix(string name)
        {
            return name.StartsWith("global::", StringComparison.Ordinal)
                ? name.Substring("global::".Length)
                : name;
        }

        public static string GenerateNotificationProxy(RpcServiceModel service, string generatedNamespace)
        {
            var emitsFrameworkTerminationNotification = service.NotificationMethods
                .Any(method => IsFrameworkSessionTerminationNotification(service, method));
            var writer = new SourceWriter();
            writer.Header();
            writer.Line("using System;");
            writer.Line("using System.Threading;");
            writer.Line("using System.Threading.Tasks;");
            writer.Line("using System.Text.Json;");
            writer.Line("using Lakona.Rpc.Core;");
            if (emitsFrameworkTerminationNotification)
            {
                writer.Line("using Lakona.Game.Abstractions;");
                writer.Line("using Lakona.Game.Abstractions.Sessions;");
            }
            writer.Line($"using {ServerRuntimeUsing};");
            writer.Line();
            writer.OpenBlock($"namespace {generatedNamespace}");
            writer.OpenBlock($"public sealed class {Naming.GetNotificationProxyTypeName(service.NotificationContractInterfaceName!)} : {service.NotificationContractFullName}, IRpcNotificationDispatchTarget");
            writer.Line($"private const int ServiceId = {service.ServiceId};");
            writer.Line("private readonly RpcNotificationChannel _notifications;");
            writer.Line();
            writer.Line($"public {Naming.GetNotificationProxyTypeName(service.NotificationContractInterfaceName!)}(RpcNotificationChannel notifications) {{ _notifications = notifications; }}");
            writer.Line();
            foreach (var method in service.NotificationMethods)
            {
                if (method.ReturnsValueTask)
                    writer.OpenBlock($"public ValueTask {method.Name}({Naming.GetParameterSignature(method.Parameters)})");
                else
                    writer.OpenBlock($"public void {method.Name}({Naming.GetParameterSignature(method.Parameters)})");

                if (IsFrameworkSessionTerminationNotification(service, method))
                {
                    writer.Line($"var payload = LakonaInternalCodec.EncodeSessionTerminationNotice({method.PayloadValue});");
                    writer.Line("return _notifications.SendRawAsync(");
                    writer.Line("    GameSessionNotificationRpcIds.ServiceId,");
                    writer.Line("    GameSessionNotificationRpcIds.TerminatedNotificationId,");
                    writer.Line("    payload,");
                    writer.Line(method.AcceptsCancellationToken
                        ? "    metadata: null, cancellationToken: cancellationToken);"
                        : "    metadata: null, cancellationToken: default);");
                }
                else if (method.ReturnsValueTask)
                    writer.Line(method.AcceptsCancellationToken
                        ? $"return _notifications.SendAsync<{method.PayloadType}>(ServiceId, {method.MethodId}, {method.PayloadValue}, cancellationToken: cancellationToken);"
                        : $"return _notifications.SendAsync<{method.PayloadType}>(ServiceId, {method.MethodId}, {method.PayloadValue});");
                else
                    writer.Line(method.AcceptsCancellationToken
                        ? $"_notifications.SendAsync<{method.PayloadType}>(ServiceId, {method.MethodId}, {method.PayloadValue}, cancellationToken: cancellationToken).AsTask().GetAwaiter().GetResult();"
                        : $"_notifications.SendAsync<{method.PayloadType}>(ServiceId, {method.MethodId}, {method.PayloadValue}).AsTask().GetAwaiter().GetResult();");
                writer.CloseBlock();
                writer.Line();
            }

            writer.OpenBlock("ValueTask IRpcNotificationDispatchTarget.DispatchNotificationAsync<TPayload>(int serviceId, int methodId, TPayload payload, RpcPushMetadata? metadata, CancellationToken cancellationToken)");
            writer.Line("if (serviceId != ServiceId) throw new InvalidOperationException(\"Notification service id does not match this callback contract.\");");
            writer.OpenBlock("switch (methodId)");
            foreach (var method in service.NotificationMethods.OrderBy(static method => method.MethodId))
            {
                writer.Line($"case {method.MethodId}:");
                writer.Indent();
                if (IsFrameworkSessionTerminationNotification(service, method))
                {
                    writer.Line($"var typedPayload{method.MethodId} = ({method.PayloadType})(object)payload!;");
                    writer.Line($"var encodedPayload{method.MethodId} = LakonaInternalCodec.EncodeSessionTerminationNotice(typedPayload{method.MethodId});");
                    writer.Line($"return _notifications.SendRawAsync(GameSessionNotificationRpcIds.ServiceId, GameSessionNotificationRpcIds.TerminatedNotificationId, encodedPayload{method.MethodId}, metadata, cancellationToken);");
                }
                else
                {
                    writer.Line($"return _notifications.SendAsync<{method.PayloadType}>(serviceId, methodId, ({method.PayloadType})(object)payload!, metadata, cancellationToken);");
                }
                writer.Unindent();
            }

            writer.Line("default:");
            writer.Indent();
            writer.Line("throw new InvalidOperationException(\"Unknown notification method id: \" + methodId);");
            writer.Unindent();
            writer.CloseBlock();
            writer.CloseBlock();
            writer.Line();

            writer.OpenBlock("ValueTask IRpcNotificationDispatchTarget.DispatchNotificationAsync(int serviceId, int methodId, ReadOnlyMemory<byte> payload, RpcPushMetadata? metadata, CancellationToken cancellationToken)");
            writer.Line("if (serviceId != ServiceId) throw new InvalidOperationException(\"Notification service id does not match this callback contract.\");");
            writer.OpenBlock("switch (methodId)");
            foreach (var method in service.NotificationMethods.OrderBy(static method => method.MethodId))
            {
                var payloadVariable = "notificationPayload" + method.MethodId;
                writer.Line($"case {method.MethodId}:");
                writer.Indent();
                writer.Line($"var {payloadVariable} = JsonSerializer.Deserialize<{method.PayloadType}>(payload.Span)!;");
                if (IsFrameworkSessionTerminationNotification(service, method))
                {
                    writer.Line($"var encodedPayload{method.MethodId} = LakonaInternalCodec.EncodeSessionTerminationNotice({payloadVariable});");
                    writer.Line($"return _notifications.SendRawAsync(GameSessionNotificationRpcIds.ServiceId, GameSessionNotificationRpcIds.TerminatedNotificationId, encodedPayload{method.MethodId}, metadata, cancellationToken);");
                }
                else
                {
                    writer.Line($"return _notifications.SendAsync<{method.PayloadType}>(serviceId, methodId, {payloadVariable}, metadata, cancellationToken);");
                }
                writer.Unindent();
            }

            writer.Line("default:");
            writer.Indent();
            writer.Line("throw new InvalidOperationException(\"Unknown notification method id: \" + methodId);");
            writer.Unindent();
            writer.CloseBlock();
            writer.CloseBlock();
            writer.CloseBlock();
            writer.CloseBlock();
            return writer.ToString();
        }

        private static bool IsFrameworkSessionTerminationNotification(
            RpcServiceModel service,
            RpcNotificationMethodModel method)
        {
            return method.ReturnsValueTask &&
                method.HasTrailingDefaultCancellationToken &&
                string.Equals(
                    NormalizeGlobalName(service.NotificationContractFullName),
                    "Lakona.Game.Abstractions.ILakonaGameSessionCallback",
                    StringComparison.Ordinal) &&
                string.Equals(method.Name, "OnSessionTerminatedAsync", StringComparison.Ordinal) &&
                string.Equals(
                    NormalizeGlobalName(method.PayloadType),
                    "Lakona.Game.Abstractions.SessionTerminationNotice",
                    StringComparison.Ordinal);
        }

        private static string? NormalizeGlobalName(string? typeName) =>
            typeName is not null && typeName.StartsWith("global::", StringComparison.Ordinal)
                ? typeName.Substring("global::".Length)
                : typeName;

        public static string GenerateAllServicesBinder(List<RpcServiceModel> services, string generatedNamespace)
        {
            var writer = new SourceWriter();
            writer.Header();
            writer.Line("using System;");
            writer.Line("using System.Linq;");
            writer.Line("using System.Reflection;");
            writer.Line($"using {ServerRuntimeUsing};");
            writer.Line();
            writer.Line($"[assembly: RpcGeneratedServicesBinder(typeof({generatedNamespace}.AllServicesBinder))]");
            writer.Line();
            writer.OpenBlock($"namespace {generatedNamespace}");
            writer.OpenBlock("public static class AllServicesBinder");
            writer.OpenBlock("public static void BindAll(RpcServiceRegistry registry)");
            foreach (var service in services)
            {
                var binder = Naming.GetBinderTypeName(service.InterfaceName);
                if (service.HasNotificationContract)
                    writer.Line($"{binder}.Bind(registry, CreateNotificationServiceFactory<{service.FullName}, {service.NotificationContractFullName}>());");
                else
                    writer.Line($"{binder}.BindFactory(registry, CreateServiceFactory<{service.FullName}>());");
            }
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock("private static Func<RpcConnectionInfo, TService> CreateServiceFactory<TService>()");
            writer.Line("var implType = ResolveImplementationType(typeof(TService));");
            writer.Line("var ctor = implType.GetConstructor(Type.EmptyTypes);");
            writer.OpenBlock("if (ctor is null)");
            writer.Line("throw new InvalidOperationException($\"No public parameterless constructor found for service implementation '{implType.FullName}'.\");");
            writer.CloseBlock();
            writer.Line("return _ => (TService)ctor.Invoke(Array.Empty<object?>());");
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock("private static Func<TNotificationContract, TService> CreateNotificationServiceFactory<TService, TNotificationContract>()");
            writer.Line("var implType = ResolveImplementationType(typeof(TService));");
            writer.Line("var notificationContractType = typeof(TNotificationContract);");
            writer.Line("var notificationCtor = implType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)");
            writer.Line("    .SingleOrDefault(static ctor =>");
            writer.Line("    {");
            writer.Line("        var parameters = ctor.GetParameters();");
            writer.Line("        return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(typeof(TNotificationContract));");
            writer.Line("    });");
            writer.OpenBlock("if (notificationCtor is not null)");
            writer.Line("return notifications => (TService)notificationCtor.Invoke(new object?[] { notifications });");
            writer.CloseBlock();
            writer.Line("var defaultCtor = implType.GetConstructor(Type.EmptyTypes);");
            writer.OpenBlock("if (defaultCtor is not null)");
            writer.Line("return _ => (TService)defaultCtor.Invoke(Array.Empty<object?>());");
            writer.CloseBlock();
            writer.Line("throw new InvalidOperationException($\"No suitable public constructor found for service implementation '{implType.FullName}'. Expected either a parameterless constructor or one accepting '{notificationContractType.FullName}'.\");");
            writer.CloseBlock();
            writer.Line();
            writer.OpenBlock("private static Type ResolveImplementationType(Type serviceType)");
            writer.Line("var implementations = typeof(AllServicesBinder).Assembly.GetTypes()");
            writer.Line("    .Where(type => !type.IsAbstract && !type.IsInterface && !type.IsNested && serviceType.IsAssignableFrom(type))");
            writer.Line("    .ToArray();");
            writer.OpenBlock("if (implementations.Length == 1)");
            writer.Line("return implementations[0];");
            writer.CloseBlock();
            writer.OpenBlock("if (implementations.Length == 0)");
            writer.Line("throw new InvalidOperationException($\"No service implementation found for '{serviceType.FullName}' in assembly '{typeof(AllServicesBinder).Assembly.GetName().Name}'.\");");
            writer.CloseBlock();
            writer.Line("var names = string.Join(\", \", implementations.Select(static type => type.FullName));");
            writer.Line("throw new InvalidOperationException($\"Multiple service implementations found for '{serviceType.FullName}': {names}. Use individual generated binders when you need explicit service instances or factories instead.\");");
            writer.CloseBlock();
            writer.CloseBlock();
            writer.CloseBlock();
            return writer.ToString();
        }
    }
}
