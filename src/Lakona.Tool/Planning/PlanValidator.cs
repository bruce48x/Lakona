namespace Lakona.Tool.Planning;

internal sealed record PlanDiagnostic(
    PlanDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Path = null);

internal enum PlanDiagnosticSeverity
{
    Error,
    Warning
}

internal static class PlanValidator
{
    private static readonly string LegacyStarterName = string.Concat("Rpc", "Starter");
    private static readonly string LegacyRpcBrand = string.Concat("ULink", "RPC");
    private static readonly string LegacyGameBrand = string.Concat("ULink", "Game");
    private static readonly string LegacyServerDirectory = string.Concat("Server", "/Server", "/");

    public static GenerationPlan Validate(GenerationPlan plan)
    {
        var diagnostics = new List<PlanDiagnostic>(plan.Diagnostics);
        AddDuplicatePathDiagnostics(plan, diagnostics);

        foreach (var file in plan.Files)
        {
            ValidatePath(file.RelativePath, diagnostics);
            ValidateContent(file, diagnostics);
        }

        foreach (var directory in plan.Directories)
        {
            ValidatePath(directory.RelativePath, diagnostics);
        }

        foreach (var archive in plan.Archives ?? [])
        {
            ValidatePath(archive.RelativeDestinationPath, diagnostics);
        }

        return plan with { Diagnostics = diagnostics };
    }

    private static void AddDuplicatePathDiagnostics(GenerationPlan plan, List<PlanDiagnostic> diagnostics)
    {
        foreach (var group in plan.Files.GroupBy(file => NormalizePath(file.RelativePath), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                diagnostics.Add(Error("LTPLAN001", $"Duplicate generated path: {group.First().RelativePath}", group.First().RelativePath));
            }
        }

        foreach (var group in (plan.Archives ?? [])
            .GroupBy(archive => NormalizePath(archive.RelativeDestinationPath), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                diagnostics.Add(Error("LTPLAN007", $"Duplicate generated archive destination: {group.First().RelativeDestinationPath}", group.First().RelativeDestinationPath));
            }
        }
    }

    private static void ValidatePath(string relativePath, List<PlanDiagnostic> diagnostics)
    {
        var normalized = NormalizePath(relativePath);
        if (Path.IsPathRooted(relativePath) || normalized.StartsWith("../", StringComparison.Ordinal) || normalized.Contains("/../", StringComparison.Ordinal))
        {
            diagnostics.Add(Error("LTPLAN002", $"Generated path escapes project root: {relativePath}", relativePath));
        }

        if (normalized.Contains(LegacyServerDirectory, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error("LTPLAN003", $"Generated path contains legacy nested server directory: {relativePath}", relativePath));
        }

        if (normalized.Contains("/Generated/", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("/Rpc/", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error("LTPLAN004", $"Generated path contains project-local RPC glue: {relativePath}", relativePath));
        }
    }

    private static void ValidateContent(GeneratedFile file, List<PlanDiagnostic> diagnostics)
    {
        if (file.Content.Contains(LegacyStarterName, StringComparison.Ordinal)
            || file.Content.Contains(LegacyRpcBrand, StringComparison.Ordinal)
            || file.Content.Contains(LegacyGameBrand, StringComparison.Ordinal))
        {
            diagnostics.Add(Error("LTPLAN005", $"Generated content contains legacy starter text: {file.RelativePath}", file.RelativePath));
        }

        if (file.Content.Contains("\"Cluster\": { \"Enabled\"", StringComparison.Ordinal)
            || file.Content.Contains("\"Hotfix\": { \"Enabled\"", StringComparison.Ordinal)
            || file.Content.Contains("\"ReliablePush\": { \"Enabled\"", StringComparison.Ordinal)
            || file.Content.Contains("Cluster.Enabled", StringComparison.Ordinal)
            || file.Content.Contains("Hotfix.Enabled", StringComparison.Ordinal)
            || file.Content.Contains("ReliablePush.Enabled", StringComparison.Ordinal))
        {
            diagnostics.Add(Error("LTPLAN006", $"Generated content contains deprecated enabled config: {file.RelativePath}", file.RelativePath));
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static PlanDiagnostic Error(string code, string message, string? path = null)
    {
        return new PlanDiagnostic(PlanDiagnosticSeverity.Error, code, message, path);
    }
}
