using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Rpc.Analyzers;

public sealed partial class LakonaRpcSourceGenerator
{
    private sealed class GeneratorOptions
    {
        private const string ClientKey = "build_property.LakonaRpcGenerateClient";
        private const string ServerKey = "build_property.LakonaRpcGenerateServer";
        private const string ClientNamespaceKey = "build_property.LakonaRpcGeneratedNamespace";
        private const string ServerNamespaceKey = "build_property.LakonaRpcServerGeneratedNamespace";
        private const string GameClientKey = "build_property.LakonaGameGenerateClient";
        private const string ProjectRoleKey = "build_property.LakonaProjectRole";
        private const string RootNamespaceKey = "build_property.RootNamespace";
        private const string ServerAppRole = "ServerApp";
        private const string HotfixRole = "Hotfix";

        private GeneratorOptions(
            bool generateClient,
            bool generateServer,
            bool generateGameClient,
            bool hasExplicitGenerationMode,
            string clientNamespace,
            string serverNamespace,
            bool hasGameClientSetting,
            bool generateGameClientDisabled)
        {
            GenerateClient = generateClient;
            GenerateServer = generateServer;
            GenerateGameClient = generateGameClient;
            HasExplicitGenerationMode = hasExplicitGenerationMode;
            ClientNamespace = clientNamespace;
            ServerNamespace = serverNamespace;
            HasGameClientSetting = hasGameClientSetting;
            GenerateGameClientDisabled = generateGameClientDisabled;
        }

        public bool GenerateClient { get; }
        public bool GenerateServer { get; }
        public bool GenerateGameClient { get; }
        public bool HasExplicitGenerationMode { get; }
        public string ClientNamespace { get; }
        public string ServerNamespace { get; }
        public bool HasGameClientSetting { get; }
        public bool GenerateGameClientDisabled { get; }

        public GeneratorOptions WithAutoDetectedModes(Compilation compilation)
        {
            if (HasExplicitGenerationMode)
                return this;

            var hasRpcClientAssembly = compilation.GetTypeByMetadataName("Lakona.Rpc.Client.RpcClientRuntime") is not null;
            var hasServerRuntime = compilation.GetTypeByMetadataName("Lakona.Rpc.Server.RpcServiceRegistry") is not null;
            var hasGameClientAssembly = compilation.GetTypeByMetadataName("Lakona.Game.Client.LakonaGameClientCore") is not null;
            var isUnityCompilation = IsUnityCompilation(compilation);
            var autoGenerateClient = hasRpcClientAssembly && !hasServerRuntime;
            var autoGenerateUnityGameClient =
                isUnityCompilation &&
                hasGameClientAssembly &&
                hasRpcClientAssembly &&
                !hasServerRuntime &&
                !HasGameClientSetting &&
                !GenerateGameClientDisabled;

            return new GeneratorOptions(
                generateClient: autoGenerateClient,
                generateServer: hasServerRuntime && !hasRpcClientAssembly,
                generateGameClient: GenerateGameClient || autoGenerateUnityGameClient,
                hasExplicitGenerationMode: false,
                ClientNamespace,
                ServerNamespace,
                HasGameClientSetting,
                GenerateGameClientDisabled);
        }

        public static GeneratorOptions From(AnalyzerConfigOptionsProvider provider, Compilation compilation)
        {
            var global = provider.GlobalOptions;
            var hasClientSetting = global.TryGetValue(ClientKey, out var clientValue);
            var hasServerSetting = global.TryGetValue(ServerKey, out var serverValue);
            var hasGameClientSetting = global.TryGetValue(GameClientKey, out var gameClientValue);
            var projectRole = GetString(global, ProjectRoleKey, string.Empty);
            var isServerAppProject = string.Equals(projectRole, ServerAppRole, StringComparison.OrdinalIgnoreCase);
            var isHotfixProject = string.Equals(projectRole, HotfixRole, StringComparison.OrdinalIgnoreCase);
            var hasGameServerRole = isServerAppProject || isHotfixProject;
            var clientNamespace = GetString(global, ClientNamespaceKey, "Client.Generated");
            var hasClientMarker = TryGetClientGenerationAttribute(compilation, out var markerNamespace);
            var hasGameClientMarker = TryGetGameClientGenerationAttribute(compilation);
            if (hasClientMarker && !hasClientSetting && !string.IsNullOrWhiteSpace(markerNamespace))
                clientNamespace = markerNamespace!;

            var generateGameClient = IsEnabled(gameClientValue) || (!hasGameClientSetting && hasGameClientMarker);
            var generateClient = IsEnabled(clientValue) || (!hasClientSetting && hasClientMarker) || generateGameClient;

            return new GeneratorOptions(
                generateClient,
                hasGameServerRole ? isServerAppProject : IsEnabled(serverValue),
                generateGameClient,
                hasClientSetting || hasServerSetting || hasGameServerRole || hasClientMarker || (hasGameClientMarker && !hasGameClientSetting),
                clientNamespace,
                isServerAppProject
                    ? GetGeneratedServerNamespace(GetString(global, RootNamespaceKey, string.Empty))
                    : GetString(global, ServerNamespaceKey, "Server.Generated"),
                hasGameClientSetting,
                IsDisabled(gameClientValue));
        }

        private static bool IsUnityCompilation(Compilation compilation)
        {
            if (compilation.AssemblyName is not null &&
                compilation.AssemblyName.StartsWith("Assembly-CSharp", StringComparison.Ordinal))
            {
                return true;
            }

            return compilation.SourceModule.ReferencedAssemblySymbols.Any(static assembly =>
                assembly.Identity.Name.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                assembly.Identity.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
        }

        private static bool TryGetClientGenerationAttribute(Compilation compilation, out string? generatedNamespace)
        {
            generatedNamespace = null;
            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass is null)
                    continue;

                if (!string.Equals(attributeClass.Name, "LakonaRpcGenerateClientAttribute", StringComparison.Ordinal))
                    continue;

                generatedNamespace = attribute.ConstructorArguments
                    .Select(static argument => argument.Value as string)
                    .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
                return true;
            }

            return false;
        }

        private static bool TryGetGameClientGenerationAttribute(Compilation compilation)
        {
            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass is null)
                    continue;

                if (!string.Equals(attributeClass.Name, "LakonaGameGenerateClientAttribute", StringComparison.Ordinal))
                    continue;

                return true;
            }

            return false;
        }

        private static bool IsEnabled(string? value) =>
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "1", StringComparison.Ordinal);

        private static bool IsDisabled(string? value) =>
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "0", StringComparison.Ordinal);

        private static string GetString(AnalyzerConfigOptions options, string key, string fallback) =>
            options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : fallback;

        private static string GetGeneratedServerNamespace(string rootNamespace) =>
            string.IsNullOrWhiteSpace(rootNamespace)
                ? "Server.App.Generated"
                : rootNamespace.Trim() + ".Generated";

    }
}
