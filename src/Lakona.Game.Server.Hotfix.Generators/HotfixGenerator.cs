using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators
{
    [Generator]
    public sealed partial class HotfixGenerator : IIncrementalGenerator
    {
        private const string DefaultGeneratedServerNamespace = "Server.App.Generated";
        private const string StableRpcServicesKey = "build_property.LakonaHotfixGenerateStableRpcServices";
        private const string HotfixProjectKey = "build_property.LakonaHotfixProject";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, cancellationToken) =>
            {
                _ = cancellationToken;
                return HotfixGeneratorOptions.From(provider.GlobalOptions);
            });

            var services = context.CompilationProvider.Combine(options)
                .Select(static (input, cancellationToken) =>
                {
                    var (compilation, generatorOptions) = input;
                    if (!generatorOptions.GenerateStableRpcServices)
                    {
                        return [];
                    }

                    return DiscoverRpcServiceContracts(compilation, cancellationToken)
                        .Select(static contract => new HotfixRpcServiceInfo(
                            contract,
                            DefaultGeneratedServerNamespace,
                            DefaultGeneratedServerNamespace))
                        .ToArray();
                });

            context.RegisterSourceOutput(services, GenerateRpcServices);

            var httpServices = context.CompilationProvider.Combine(options)
                .Select(static (input, cancellationToken) =>
                {
                    var (compilation, generatorOptions) = input;
                    return generatorOptions.IsHotfixProject
                        ? DiscoverHttpServices(compilation, cancellationToken)
                        : [];
                });

            context.RegisterSourceOutput(httpServices, ValidateHttpServices);

            var clientNotifications = context.CompilationProvider
                .Select(static (compilation, cancellationToken) =>
                {
                    if (compilation.GetTypeByMetadataName("Lakona.Game.Server.Sessions.ClientNotificationTarget`1") is null ||
                        compilation.GetTypeByMetadataName("Lakona.Game.Server.Sessions.GeneratedClientNotificationExtensions") is not null)
                    {
                        return [];
                    }

                    return DiscoverRpcServiceContracts(compilation, cancellationToken)
                        .Select(static contract => new HotfixRpcServiceInfo(
                            contract,
                            DefaultGeneratedServerNamespace,
                            DefaultGeneratedServerNamespace))
                        .Where(static service => GetNotificationContract(service.Contract) is not null)
                        .ToArray();
                });

            context.RegisterSourceOutput(clientNotifications, GenerateClientNotificationExtensions);

            var actorContracts = context.CompilationProvider.Combine(options)
                .Select(static (input, cancellationToken) =>
                {
                    var (compilation, _) = input;
                    return new HotfixActorGenerationInput(
                        compilation.Assembly.Identity.Name,
                        DiscoverHotfixBehaviors(compilation, cancellationToken).ToArray(),
                        DiscoverStartupRegistrations(compilation, cancellationToken).ToArray());
                });

            context.RegisterSourceOutput(actorContracts, GenerateActorContracts);

            var timerModules = context.CompilationProvider
                .Select(static (compilation, cancellationToken) =>
                    DiscoverHotfixTimers(compilation, cancellationToken).ToArray());
            context.RegisterSourceOutput(timerModules, GenerateTimerEntries);

            var components = context.CompilationProvider
                .Select(static (compilation, cancellationToken) =>
                    DiscoverHotfixComponents(compilation, cancellationToken).ToArray());
            context.RegisterSourceOutput(components, GenerateComponentRegistration);
        }

        private readonly struct HotfixGeneratorOptions : System.IEquatable<HotfixGeneratorOptions>
        {
            public HotfixGeneratorOptions(
                bool generateStableRpcServices,
                bool isHotfixProject)
            {
                GenerateStableRpcServices = generateStableRpcServices;
                IsHotfixProject = isHotfixProject;
            }

            public bool GenerateStableRpcServices { get; }

            public bool IsHotfixProject { get; }

            public static HotfixGeneratorOptions From(AnalyzerConfigOptions options)
            {
                return new HotfixGeneratorOptions(
                    IsEnabled(options, StableRpcServicesKey, defaultValue: true),
                    IsEnabled(options, HotfixProjectKey, defaultValue: false));
            }

            private static bool IsEnabled(AnalyzerConfigOptions options, string key, bool defaultValue)
            {
                if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    return defaultValue;
                }

                var trimmed = value.Trim();
                if (string.Equals(trimmed, "true", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "1", System.StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.Equals(trimmed, "false", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "0", System.StringComparison.Ordinal))
                {
                    return false;
                }

                return defaultValue;
            }

            public bool Equals(HotfixGeneratorOptions other)
            {
                return GenerateStableRpcServices == other.GenerateStableRpcServices &&
                    IsHotfixProject == other.IsHotfixProject;
            }

            public override bool Equals(object? obj)
            {
                return obj is HotfixGeneratorOptions other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (GenerateStableRpcServices, IsHotfixProject).GetHashCode();
            }
        }
    }
}
