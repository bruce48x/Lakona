using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Lakona.Rpc.Analyzers;

public sealed partial class LakonaRpcSourceGenerator
{
    private static class Naming
    {
        public static string GetServiceTypeName(string interfaceName) =>
            interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1])
                ? interfaceName.Substring(1)
                : interfaceName;

        public static string GetClientTypeName(string interfaceName) => GetServiceTypeName(interfaceName) + "Client";
        public static string GetBinderTypeName(string interfaceName) => GetServiceTypeName(interfaceName) + "Binder";
        public static string GetNotificationProxyTypeName(string notificationContractInterfaceName) => GetServiceTypeName(notificationContractInterfaceName) + "Proxy";
        public static string GetNotificationBinderTypeName(string notificationContractInterfaceName) => GetServiceTypeName(notificationContractInterfaceName) + "Binder";
        public static string GetClientExtensionTypeName(string interfaceName) => GetServiceTypeName(interfaceName) + "ClientExtensions";
        public static string GetClientFactoryMethodName(string interfaceName) => "Create" + GetServiceTypeName(interfaceName);
        public static string GetClientMethodFieldName(string methodName) => ToCamelCase(methodName) + "RpcMethod";
        public static string GetNotificationMethodFieldName(string methodName) => ToCamelCase(methodName) + "NotificationMethod";
        public static string GetNotificationReceiverParamName(string notificationContractInterfaceName) => ToCamelCase(GetServiceTypeName(notificationContractInterfaceName));

        public static string GetParameterSignature(IReadOnlyList<RpcParameterModel> parameters) =>
            string.Join(", ", parameters.Select(static parameter => parameter.TypeName + " " + parameter.Name));

        public static string GetFacadeServicePropertyName(string interfaceName)
        {
            var name = GetServiceTypeName(interfaceName);
            if (name.EndsWith("Service", StringComparison.Ordinal) && name.Length > "Service".Length)
                name = name.Substring(0, name.Length - "Service".Length);

            return ToPascalIdentifier(name);
        }

        public static string GetFacadeGroupName(string fullName)
        {
            var noGlobal = fullName.StartsWith("global::", StringComparison.Ordinal)
                ? fullName.Substring("global::".Length)
                : fullName;
            var firstDot = noGlobal.IndexOf('.');
            return firstDot < 0 ? "Default" : ToPascalIdentifier(noGlobal.Substring(0, firstDot));
        }

        public static bool IsValidIdentifier(string value)
            => !string.IsNullOrWhiteSpace(value) && SyntaxFacts.IsValidIdentifier(value);

        private static string ToCamelCase(string value) =>
            string.IsNullOrEmpty(value) ? "value" : char.ToLowerInvariant(value[0]) + value.Substring(1);

        private static string ToPascalIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Default";

            var builder = new StringBuilder();
            var nextUpper = true;
            foreach (var ch in value)
            {
                if (!char.IsLetterOrDigit(ch))
                {
                    nextUpper = true;
                    continue;
                }

                builder.Append(nextUpper ? char.ToUpperInvariant(ch) : ch);
                nextUpper = false;
            }

            if (builder.Length == 0)
                return "Default";

            if (char.IsDigit(builder[0]))
                builder.Insert(0, '_');

            return builder.ToString();
        }
    }
}
