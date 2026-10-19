using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Rpc.Analyzers;

public sealed partial class LakonaRpcSourceGenerator
{
    private sealed class GeneratorOptions
    {
        private const string ClientNamespaceKey = "build_property.LakonaRpcGeneratedNamespace";
        private const string ServerNamespaceKey = "build_property.LakonaRpcServerGeneratedNamespace";
        private const string ProjectRoleKey = "build_property.LakonaProjectRole";
        private const string RootNamespaceKey = "build_property.RootNamespace";
        private const string ServerAppRole = "ServerApp";
        private const string HotfixRole = "Hotfix";

        private GeneratorOptions(
            bool generateClient,
            bool generateServer,
            bool generateGameClient,
            string clientNamespace,
            string serverNamespace)
        {
            GenerateClient = generateClient;
            GenerateServer = generateServer;
            GenerateGameClient = generateGameClient;
            ClientNamespace = clientNamespace;
            ServerNamespace = serverNamespace;
        }

        public bool GenerateClient { get; }
        public bool GenerateServer { get; }
        public bool GenerateGameClient { get; }
        public string ClientNamespace { get; }
        public string ServerNamespace { get; }

        public static GeneratorOptions From(AnalyzerConfigOptionsProvider provider, Compilation compilation)
        {
            var global = provider.GlobalOptions;
            var projectRole = GetString(global, ProjectRoleKey, string.Empty);
            var isServerAppProject = string.Equals(projectRole, ServerAppRole, StringComparison.OrdinalIgnoreCase);
            var isHotfixProject = string.Equals(projectRole, HotfixRole, StringComparison.OrdinalIgnoreCase);
            var hasGameServerRole = isServerAppProject || isHotfixProject;
            var clientNamespace = GetString(global, ClientNamespaceKey, "Client.Generated");
            var hasClientRuntime = compilation.GetTypeByMetadataName("Lakona.Rpc.Client.RpcClientRuntime") is not null;
            var hasServerRuntime = compilation.GetTypeByMetadataName("Lakona.Rpc.Server.RpcServiceRegistry") is not null;
            var generateClient = !hasGameServerRole && hasClientRuntime;
            var generateServer = isServerAppProject || (!isHotfixProject && hasServerRuntime);
            var generateGameClient = generateClient && !hasServerRuntime &&
                compilation.GetTypeByMetadataName("Lakona.Game.Client.LakonaGameClientCore") is not null;

            return new GeneratorOptions(
                generateClient,
                generateServer,
                generateGameClient,
                clientNamespace,
                isServerAppProject
                    ? GetGeneratedServerNamespace(GetString(global, RootNamespaceKey, string.Empty))
                    : GetString(global, ServerNamespaceKey, "Server.Generated"));
        }

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
