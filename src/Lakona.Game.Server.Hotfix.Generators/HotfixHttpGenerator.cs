using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using static Lakona.Game.Server.Hotfix.Generators.GeneratorSymbolFacts;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class HotfixHttpGenerator
    {
        private const string HttpServiceAttributeName =
            "Lakona.Game.Server.Http.LakonaHttpServiceAttribute";
        private const string HttpEndpointAttributeName =
            "Lakona.Game.Server.Http.LakonaHttpEndpointAttribute";
        private const string HttpCallTypeName =
            "Lakona.Game.Server.Http.LakonaHttpCall";
        private const string HttpResponseTypeName =
            "Lakona.Game.Server.Http.LakonaHttpResponse";

        internal static void Register(
            IncrementalGeneratorInitializationContext context,
            IncrementalValueProvider<HotfixGeneratorOptions> options)
        {
            var services = context.CompilationProvider.Combine(options)
                .Select(static (input, cancellationToken) =>
                {
                    var (compilation, generatorOptions) = input;
                    return generatorOptions.IsHotfixProject
                        ? DiscoverHttpServices(compilation, cancellationToken)
                        : [];
                });

            context.RegisterSourceOutput(services, ValidateHttpServices);
        }

        private static HotfixHttpServiceInfo[] DiscoverHttpServices(
            Compilation compilation,
            CancellationToken cancellationToken)
        {
            if (compilation.GetTypeByMetadataName(HttpServiceAttributeName) is null)
            {
                return [];
            }

            return EnumerateTypes(compilation.Assembly.GlobalNamespace)
                .Select(contract =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return TryCreateHttpServiceInfo(contract, out var service)
                        ? service
                        : null;
                })
                .Where(static service => service is not null)
                .Cast<HotfixHttpServiceInfo>()
                .ToArray();
        }

        private static bool TryCreateHttpServiceInfo(
            INamedTypeSymbol serviceType,
            out HotfixHttpServiceInfo service)
        {
            var attribute = serviceType.GetAttributes()
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
            var methods = serviceType.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(static method =>
                    method.MethodKind == MethodKind.Ordinary
                    && !IsDisposalMethod(method))
                .Select(CreateHttpEndpointInfo)
                .ToArray();
            service = new HotfixHttpServiceInfo(serviceType, name, methods);
            return true;
        }

        private static HotfixHttpEndpointInfo CreateHttpEndpointInfo(IMethodSymbol method)
        {
            var attribute = method.GetAttributes()
                .FirstOrDefault(static candidate =>
                    candidate.AttributeClass?.ToDisplayString() == HttpEndpointAttributeName);
            if (attribute is null
                || attribute.ConstructorArguments.Length != 2)
            {
                return new HotfixHttpEndpointInfo(method, "", "", hasValidAttribute: false);
            }

            return new HotfixHttpEndpointInfo(
                method,
                attribute.ConstructorArguments[0].Value as string ?? "",
                attribute.ConstructorArguments[1].Value as string ?? "",
                hasValidAttribute: true);
        }

        private static void ValidateHttpServices(
            SourceProductionContext context,
            HotfixHttpServiceInfo[] services)
        {
            var duplicateNames = new HashSet<string>(services
                .Where(static service => !string.IsNullOrWhiteSpace(service.Name))
                .GroupBy(static service => service.Name, System.StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key),
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var service in services)
            {
                var validServiceShape =
                    service.Type.TypeKind == TypeKind.Class
                    && service.Type.DeclaredAccessibility == Accessibility.Public
                    && service.Type.IsSealed
                    && !service.Type.IsAbstract
                    && !service.Type.IsGenericType
                    && service.Type.ContainingType is null
                    && !string.IsNullOrWhiteSpace(service.Name)
                    && service.Endpoints.Length > 0;
                if (!validServiceShape)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HttpServiceContractShape,
                        service.Type.Locations.FirstOrDefault(
                            static location => location.IsInSource),
                        service.Type.ToDisplayString()));
                }

                if (duplicateNames.Contains(service.Name))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.DuplicateHttpServiceName,
                        service.Type.Locations.FirstOrDefault(
                            static location => location.IsInSource),
                        service.Name));
                }

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
                        continue;
                    }

                    var routeKey = endpoint.HttpMethod + " " + endpoint.RoutePattern;
                    if (!routes.Add(routeKey))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            HotfixGeneratorDiagnostics.DuplicateHttpEndpoint,
                            endpoint.Method.Locations.FirstOrDefault(
                                static location => location.IsInSource),
                            service.Type.ToDisplayString(),
                            routeKey));
                    }
                }
            }
        }

        private static bool IsValidHttpEndpointMethod(HotfixHttpEndpointInfo endpoint)
        {
            return endpoint.HasValidAttribute
                && endpoint.Method.DeclaredAccessibility == Accessibility.Public
                && !endpoint.Method.IsStatic
                && !endpoint.Method.IsGenericMethod
                && endpoint.Method.Parameters.Length == 1
                && endpoint.Method.Parameters[0].Type.ToDisplayString() == HttpCallTypeName
                && endpoint.Method.ReturnType is INamedTypeSymbol
                {
                    Name: "ValueTask",
                    ContainingNamespace.Name: "Tasks",
                    TypeArguments.Length: 1
                } returnType
                && returnType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
                && returnType.TypeArguments[0].ToDisplayString() == HttpResponseTypeName
                && !string.IsNullOrWhiteSpace(endpoint.HttpMethod)
                && !endpoint.HttpMethod.Any(char.IsWhiteSpace)
                && !string.IsNullOrWhiteSpace(endpoint.RoutePattern)
                && endpoint.RoutePattern.StartsWith(
                    "/",
                    System.StringComparison.Ordinal);
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

        private static bool IsDisposalMethod(IMethodSymbol method)
        {
            if (method.Name == "Dispose"
                && method.ReturnsVoid
                && method.Parameters.Length == 0)
            {
                return true;
            }

            return method.Name == "DisposeAsync"
                && method.Parameters.Length == 0
                && method.ReturnType.ToDisplayString() == "System.Threading.Tasks.ValueTask";
        }

        private sealed class HotfixHttpServiceInfo
        {
            public HotfixHttpServiceInfo(
                INamedTypeSymbol type,
                string name,
                HotfixHttpEndpointInfo[] endpoints)
            {
                Type = type;
                Name = name;
                Endpoints = endpoints;
            }

            public INamedTypeSymbol Type { get; }

            public string Name { get; }

            public HotfixHttpEndpointInfo[] Endpoints { get; }
        }

        private sealed class HotfixHttpEndpointInfo
        {
            public HotfixHttpEndpointInfo(
                IMethodSymbol method,
                string httpMethod,
                string routePattern,
                bool hasValidAttribute)
            {
                Method = method;
                HttpMethod = httpMethod;
                RoutePattern = routePattern;
                HasValidAttribute = hasValidAttribute;
            }

            public IMethodSymbol Method { get; }

            public string HttpMethod { get; }

            public string RoutePattern { get; }

            public bool HasValidAttribute { get; }
        }
    }
}
