using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal readonly struct HotfixGeneratorOptions : System.IEquatable<HotfixGeneratorOptions>
    {
        private const string ProjectRoleKey = "build_property.LakonaProjectRole";
        private const string RootNamespaceKey = "build_property.RootNamespace";
        private const string ServerAppRole = "ServerApp";
        private const string HotfixRole = "Hotfix";

        public HotfixGeneratorOptions(
            bool generateStableRpcServices,
            bool isHotfixProject,
            string generatedServerNamespace)
        {
            GenerateStableRpcServices = generateStableRpcServices;
            IsHotfixProject = isHotfixProject;
            GeneratedServerNamespace = generatedServerNamespace;
        }

        public bool GenerateStableRpcServices { get; }

        public bool IsHotfixProject { get; }

        public string GeneratedServerNamespace { get; }

        public static HotfixGeneratorOptions From(AnalyzerConfigOptions options)
        {
            var projectRole = GetString(options, ProjectRoleKey);
            var isServerAppProject = string.Equals(
                projectRole,
                ServerAppRole,
                System.StringComparison.OrdinalIgnoreCase);
            var isHotfixProject = string.Equals(
                projectRole,
                HotfixRole,
                System.StringComparison.OrdinalIgnoreCase);
            return new HotfixGeneratorOptions(
                isServerAppProject,
                isHotfixProject,
                isServerAppProject
                    ? GetGeneratedServerNamespace(GetString(options, RootNamespaceKey))
                    : "Server.App.Generated");
        }

        private static string GetString(AnalyzerConfigOptions options, string key)
        {
            if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim();
        }

        private static string GetGeneratedServerNamespace(string rootNamespace) =>
            string.IsNullOrWhiteSpace(rootNamespace)
                ? "Server.App.Generated"
                : rootNamespace + ".Generated";

        public bool Equals(HotfixGeneratorOptions other)
        {
            return GenerateStableRpcServices == other.GenerateStableRpcServices &&
                IsHotfixProject == other.IsHotfixProject &&
                string.Equals(
                    GeneratedServerNamespace,
                    other.GeneratedServerNamespace,
                    System.StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is HotfixGeneratorOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (GenerateStableRpcServices, IsHotfixProject, GeneratedServerNamespace).GetHashCode();
        }
    }
}
