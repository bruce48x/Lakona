using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Lakona.Game.Server.Hotfix.Generators
{
    public sealed partial class HotfixGenerator
    {
        private const string HttpServiceAttributeName =
            "Lakona.Game.Server.Http.LakonaHttpServiceAttribute";
        private const string HttpEndpointAttributeName =
            "Lakona.Game.Server.Http.LakonaHttpEndpointAttribute";
        private const string HttpRequestTypeName =
            "Lakona.Game.Server.Http.LakonaHttpRequest";
        private const string HttpResponseTypeName =
            "Lakona.Game.Server.Http.LakonaHttpResponse";

        private static IEnumerable<HotfixHttpServiceInfo> DiscoverHttpServiceContracts(
            Compilation compilation,
            CancellationToken cancellationToken)
        {
            if (compilation.GetTypeByMetadataName(HttpServiceAttributeName) is null
                || compilation.GetTypeByMetadataName(
                    "Lakona.Game.Server.Hosting.LakonaGameServerBuilder") is null)
            {
                yield break;
            }

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var contract in EnumerateTypes(compilation.Assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryCreateHttpServiceInfo(contract, out var service)
                    && seen.Add(contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                {
                    yield return service;
                }
            }

            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var contract in EnumerateTypes(assembly.GlobalNamespace))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryCreateHttpServiceInfo(contract, out var service)
                        && seen.Add(contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                    {
                        yield return service;
                    }
                }
            }
        }

        private static bool TryCreateHttpServiceInfo(
            INamedTypeSymbol contract,
            out HotfixHttpServiceInfo service)
        {
            var attribute = contract.GetAttributes()
                .FirstOrDefault(static candidate =>
                    candidate.AttributeClass?.ToDisplayString() == HttpServiceAttributeName);
            if (attribute is null)
            {
                service = null!;
                return false;
            }

            var name = attribute.ConstructorArguments.Length == 1
                ? attribute.ConstructorArguments[0].Value as string ?? ""
                : "";
            var methods = contract.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(static method => method.MethodKind == MethodKind.Ordinary)
                .Select(CreateHttpEndpointInfo)
                .ToArray();
            service = new HotfixHttpServiceInfo(contract, name, methods);
            return true;
        }

        private static HotfixHttpEndpointInfo CreateHttpEndpointInfo(IMethodSymbol method)
        {
            var attributes = method.GetAttributes()
                .Where(static candidate =>
                    candidate.AttributeClass?.ToDisplayString() == HttpEndpointAttributeName)
                .ToArray();
            if (attributes.Length != 1 || attributes[0].ConstructorArguments.Length != 3)
            {
                return new HotfixHttpEndpointInfo(method, 0, "", "", hasValidAttribute: false);
            }

            var arguments = attributes[0].ConstructorArguments;
            return new HotfixHttpEndpointInfo(
                method,
                arguments[0].Value is int methodId ? methodId : 0,
                arguments[1].Value as string ?? "",
                arguments[2].Value as string ?? "",
                hasValidAttribute: true);
        }

        private static void GenerateHttpServices(
            SourceProductionContext context,
            HotfixHttpServiceInfo[] services)
        {
            if (services.Length == 0)
            {
                return;
            }

            var duplicateNames = new HashSet<string>(
                services
                    .Where(static service => !string.IsNullOrWhiteSpace(service.Name))
                    .GroupBy(
                        static service => service.Name,
                        System.StringComparer.OrdinalIgnoreCase)
                    .Where(static group => group.Count() > 1)
                    .Select(static group => group.Key),
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var duplicateName in duplicateNames.OrderBy(
                static name => name,
                System.StringComparer.OrdinalIgnoreCase))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.DuplicateHttpServiceName,
                    Location.None,
                    duplicateName));
            }

            var supported = new List<HotfixHttpServiceInfo>();
            foreach (var service in services.OrderBy(
                static item => item.Contract.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat),
                System.StringComparer.Ordinal))
            {
                if (!ValidateHttpService(context, service, duplicateNames))
                {
                    continue;
                }

                supported.Add(service);
            }

            if (supported.Count == 0)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.AppendLine("namespace Server.App.Generated;");
            builder.AppendLine();
            builder.AppendLine("internal sealed class GeneratedLakonaHttpServiceRegistration :");
            builder.AppendLine("    global::Lakona.Game.Server.Hosting.ILakonaGameGeneratedServiceRegistration");
            builder.AppendLine("{");
            builder.AppendLine("    public void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            builder.AppendLine("    {");
            foreach (var service in supported)
            {
                var contract = service.Contract.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat);
                foreach (var endpoint in service.Endpoints.OrderBy(
                    static endpoint => endpoint.MethodId))
                {
                    builder.Append("        global::Lakona.Game.Server.Http.LakonaHttpServiceCollectionExtensions.AddLakonaHttpEndpoint<")
                        .Append(contract)
                        .Append(">(services, \"")
                        .Append(EscapeStringLiteral(service.Name))
                        .Append("\", \"")
                        .Append(EscapeStringLiteral(endpoint.HttpMethod.ToUpperInvariant()))
                        .Append("\", \"")
                        .Append(EscapeStringLiteral(endpoint.RoutePattern))
                        .Append("\", ")
                        .Append(endpoint.MethodId)
                        .AppendLine(");");
                }
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("internal sealed class GeneratedLakonaHttpRequiredServiceContracts :");
            builder.AppendLine("    global::Lakona.Game.Server.Hotfix.Abstractions.IHotfixRequiredServiceContracts");
            builder.AppendLine("{");
            builder.AppendLine("    public global::System.Collections.Generic.IReadOnlyList<global::System.Type> ServiceContracts { get; } =");
            builder.AppendLine("    [");
            foreach (var service in supported)
            {
                builder.Append("        typeof(")
                    .Append(service.Contract.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine("),");
            }

            builder.AppendLine("    ];");
            builder.AppendLine("}");
            context.AddSource(
                "GeneratedLakonaHttpServices.g.cs",
                SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private static bool ValidateHttpService(
            SourceProductionContext context,
            HotfixHttpServiceInfo service,
            HashSet<string> duplicateNames)
        {
            if (service.Contract.TypeKind != TypeKind.Interface
                || service.Contract.TypeParameters.Length != 0
                || string.IsNullOrWhiteSpace(service.Name)
                || duplicateNames.Contains(service.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.HttpServiceContractShape,
                    service.Contract.Locations.FirstOrDefault(
                        static location => location.IsInSource),
                    service.Contract.ToDisplayString()));
                return false;
            }

            var valid = true;
            var methodIds = new HashSet<int>();
            var routes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var endpoint in service.Endpoints)
            {
                if (!IsValidHttpEndpointMethod(endpoint))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HttpEndpointMethodShape,
                        endpoint.Method.Locations.FirstOrDefault(
                            static location => location.IsInSource),
                        endpoint.Method.ToDisplayString()));
                    valid = false;
                    continue;
                }

                if (IsManagementRoute(endpoint.RoutePattern))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HttpManagementRouteReserved,
                        endpoint.Method.Locations.FirstOrDefault(
                            static location => location.IsInSource),
                        endpoint.Method.ToDisplayString(),
                        endpoint.RoutePattern));
                    valid = false;
                    continue;
                }

                var routeKey = endpoint.HttpMethod + " " + endpoint.RoutePattern;
                if (!methodIds.Add(endpoint.MethodId) || !routes.Add(routeKey))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.DuplicateHttpEndpoint,
                        endpoint.Method.Locations.FirstOrDefault(
                            static location => location.IsInSource),
                        service.Contract.ToDisplayString(),
                        routeKey));
                    valid = false;
                }
            }

            return valid && service.Endpoints.Length > 0;
        }

        private static bool IsValidHttpEndpointMethod(HotfixHttpEndpointInfo endpoint)
        {
            if (!endpoint.HasValidAttribute
                || endpoint.MethodId <= 0
                || string.IsNullOrWhiteSpace(endpoint.HttpMethod)
                || endpoint.HttpMethod.Any(char.IsWhiteSpace)
                || string.IsNullOrWhiteSpace(endpoint.RoutePattern)
                || !endpoint.RoutePattern.StartsWith(
                    "/",
                    System.StringComparison.Ordinal)
                || endpoint.Method.Parameters.Length != 1
                || endpoint.Method.Parameters[0].Type.ToDisplayString() != HttpRequestTypeName)
            {
                return false;
            }

            if (endpoint.Method.ReturnType is not INamedTypeSymbol returnType
                || returnType.Name != "ValueTask"
                || returnType.ContainingNamespace.ToDisplayString()
                    != "System.Threading.Tasks"
                || returnType.TypeArguments.Length != 1)
            {
                return false;
            }

            return returnType.TypeArguments[0].ToDisplayString() == HttpResponseTypeName;
        }

        private static bool IsManagementRoute(string routePattern)
        {
            return routePattern.Equals(
                    "/_lakona",
                    System.StringComparison.OrdinalIgnoreCase)
                || routePattern.StartsWith(
                    "/_lakona/",
                    System.StringComparison.OrdinalIgnoreCase);
        }

        private sealed class HotfixHttpServiceInfo
        {
            public HotfixHttpServiceInfo(
                INamedTypeSymbol contract,
                string name,
                HotfixHttpEndpointInfo[] endpoints)
            {
                Contract = contract;
                Name = name;
                Endpoints = endpoints;
            }

            public INamedTypeSymbol Contract { get; }

            public string Name { get; }

            public HotfixHttpEndpointInfo[] Endpoints { get; }
        }

        private sealed class HotfixHttpEndpointInfo
        {
            public HotfixHttpEndpointInfo(
                IMethodSymbol method,
                int methodId,
                string httpMethod,
                string routePattern,
                bool hasValidAttribute)
            {
                Method = method;
                MethodId = methodId;
                HttpMethod = httpMethod;
                RoutePattern = routePattern;
                HasValidAttribute = hasValidAttribute;
            }

            public IMethodSymbol Method { get; }

            public int MethodId { get; }

            public string HttpMethod { get; }

            public string RoutePattern { get; }

            public bool HasValidAttribute { get; }
        }
    }
}
