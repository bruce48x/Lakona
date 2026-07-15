using System.Text.RegularExpressions;
using FrameworkBenchmark.Contracts;

namespace FrameworkBenchmark.Coordinator;

public static partial class CommandTemplateExpander
{
    public static ProcessCommand Expand(ProcessCommand command, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(values);

        return new ProcessCommand(
            ExpandText(command.FileName, values),
            command.Arguments.Select(argument => ExpandText(argument, values)).ToArray(),
            command.Environment?.ToDictionary(
                pair => pair.Key,
                pair => ExpandText(pair.Value, values),
                StringComparer.Ordinal));
    }

    public static string ExpandText(string text, IReadOnlyDictionary<string, string> values)
    {
        var expanded = text;
        foreach (var pair in values.OrderByDescending(static pair => pair.Key.Length))
        {
            expanded = expanded.Replace("${" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        var unknown = PlaceholderRegex().Match(expanded);
        if (unknown.Success)
        {
            throw new BenchmarkToolException($"Unknown command placeholder '{unknown.Value}'.");
        }

        return expanded;
    }

    [GeneratedRegex(@"\$\{[^{}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
