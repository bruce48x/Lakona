using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal readonly struct HotfixGeneratorOptions : System.IEquatable<HotfixGeneratorOptions>
    {
        private const string StableRpcServicesKey =
            "build_property.LakonaHotfixGenerateStableRpcServices";
        private const string HotfixProjectKey =
            "build_property.LakonaHotfixProject";

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
