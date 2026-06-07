using System.Reflection;

namespace Lakona.Tool.RpcStarter;

internal static class StarterTemplateRenderer
{
    private const string ResourcePrefix = "Lakona.Tool.RpcStarter.Templates.";

    public static string Render(string relativeResourcePath, IReadOnlyDictionary<string, string>? values = null)
    {
        var resourceName = ResourcePrefix + relativeResourcePath.Replace('/', '.').Replace('\\', '.');
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded starter template not found: {resourceName}");
        using var reader = new StreamReader(stream);

        var content = reader.ReadToEnd();
        if (values is null || values.Count == 0)
            return content;

        foreach (var (key, value) in values)
            content = content.Replace("{{" + key + "}}", value, StringComparison.Ordinal);

        return content;
    }
}
