using Lakona.Tool.Domain;

namespace Lakona.Tool.Cli.Options;

internal static class NewProjectOptionParser
{
    private static readonly string[] NewOptions =
    [
        "--name",
        "--output",
        "--client-engine",
        "--transport",
        "--serializer",
        "--persistence",
        "--nugetforunity-source",
        "--deploy-profile"
    ];

    public static NewProjectOptions Parse(string[] args) => Parse(args, global::ToolText.Current);

    internal static NewProjectOptions Parse(string[] args, global::ToolText text)
    {
        string? name = null;
        string? outputPath = null;
        var clientEngine = ClientEngine.Unity;
        var transport = TransportKind.Kcp;
        var serializer = SerializerKind.MemoryPack;
        var persistence = PersistenceKind.None;
        var nuGetForUnitySource = NuGetForUnitySource.OpenUpm;
        var deployProfile = DeploymentProfile.None;
        var presence = NewProjectOptionPresence.None;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--name":
                    name = ReadOptionValue(args, ref index, "--name", text);
                    presence |= NewProjectOptionPresence.Name;
                    break;
                case "--output":
                    outputPath = ReadOptionValue(args, ref index, "--output", text);
                    presence |= NewProjectOptionPresence.OutputPath;
                    break;
                case "--client-engine":
                    clientEngine = ParseClientEngine(ReadOptionValue(args, ref index, "--client-engine", text), text);
                    presence |= NewProjectOptionPresence.ClientEngine;
                    break;
                case "--transport":
                    transport = ParseTransport(ReadOptionValue(args, ref index, "--transport", text), text);
                    presence |= NewProjectOptionPresence.Transport;
                    break;
                case "--serializer":
                    serializer = ParseSerializer(ReadOptionValue(args, ref index, "--serializer", text), text);
                    presence |= NewProjectOptionPresence.Serializer;
                    break;
                case "--persistence":
                    persistence = ParsePersistence(ReadOptionValue(args, ref index, "--persistence", text), text);
                    presence |= NewProjectOptionPresence.Persistence;
                    break;
                case "--nugetforunity-source":
                    nuGetForUnitySource = ParseNuGetForUnitySource(ReadOptionValue(args, ref index, "--nugetforunity-source", text), text);
                    presence |= NewProjectOptionPresence.NuGetForUnitySource;
                    break;
                case "--deploy-profile":
                    deployProfile = ParseDeploymentProfile(ReadOptionValue(args, ref index, "--deploy-profile", text), text);
                    presence |= NewProjectOptionPresence.DeployProfile;
                    break;
                default:
                    throw CreateUnsupportedArgumentException(args[index], NewOptions, text);
            }
        }

        return new NewProjectOptions(name, outputPath, clientEngine, transport, serializer, persistence, nuGetForUnitySource, deployProfile, presence);
    }

    private static ClientEngine ParseClientEngine(string value, global::ToolText text)
    {
        return ValidateChoice("--client-engine", value, new Dictionary<string, ClientEngine>(StringComparer.Ordinal)
        {
            ["unity"] = ClientEngine.Unity,
            ["unity-cn"] = ClientEngine.UnityCn,
            ["tuanjie"] = ClientEngine.Tuanjie,
            ["godot"] = ClientEngine.Godot
        }, text);
    }

    private static TransportKind ParseTransport(string value, global::ToolText text)
    {
        return ValidateChoice("--transport", value, new Dictionary<string, TransportKind>(StringComparer.Ordinal)
        {
            ["tcp"] = TransportKind.Tcp,
            ["websocket"] = TransportKind.WebSocket,
            ["kcp"] = TransportKind.Kcp
        }, text);
    }

    private static SerializerKind ParseSerializer(string value, global::ToolText text)
    {
        return ValidateChoice("--serializer", value, new Dictionary<string, SerializerKind>(StringComparer.Ordinal)
        {
            ["json"] = SerializerKind.Json,
            ["memorypack"] = SerializerKind.MemoryPack
        }, text);
    }

    private static PersistenceKind ParsePersistence(string value, global::ToolText text)
    {
        return ValidateChoice("--persistence", value, new Dictionary<string, PersistenceKind>(StringComparer.Ordinal)
        {
            ["none"] = PersistenceKind.None,
            ["mysql"] = PersistenceKind.MySql,
            ["postgres"] = PersistenceKind.Postgres
        }, text);
    }

    private static NuGetForUnitySource ParseNuGetForUnitySource(string value, global::ToolText text)
    {
        return ValidateChoice("--nugetforunity-source", value, new Dictionary<string, NuGetForUnitySource>(StringComparer.Ordinal)
        {
            ["embedded"] = NuGetForUnitySource.Embedded,
            ["openupm"] = NuGetForUnitySource.OpenUpm
        }, text);
    }

    private static DeploymentProfile ParseDeploymentProfile(string value, global::ToolText text)
    {
        return ValidateChoice("--deploy-profile", value, new Dictionary<string, DeploymentProfile>(StringComparer.Ordinal)
        {
            ["none"] = DeploymentProfile.None,
            ["compose"] = DeploymentProfile.Compose
        }, text);
    }

    private static string ReadOptionValue(string[] args, ref int index, string optionName, global::ToolText text)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException(text.MissingValue(optionName));
        }

        return args[++index];
    }

    private static T ValidateChoice<T>(string optionName, string value, IReadOnlyDictionary<string, T> supportedValues, global::ToolText text)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (supportedValues.TryGetValue(normalized, out var result))
        {
            return result;
        }

        var supportedKeys = supportedValues.Keys.ToArray();
        var suggestion = GetValueSuggestion(optionName, normalized, supportedKeys);
        throw new CliUsageException(text.UnsupportedValue(value, optionName, supportedKeys, suggestion));
    }

    private static global::CliUsageException CreateUnsupportedArgumentException(string argument, IReadOnlyList<string> supportedOptions, global::ToolText text)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            return new global::CliUsageException(text.UnexpectedArgument(argument));
        }

        var suggestion = GetClosestMatch(argument, supportedOptions);
        return new global::CliUsageException(text.UnsupportedOption(argument, suggestion));
    }

    private static string? GetValueSuggestion(string optionName, string value, IReadOnlyCollection<string> supportedValues)
    {
        if (optionName == "--transport" && value is "ws" or "websocket transport")
        {
            return "websocket";
        }

        return GetClosestMatch(value, supportedValues);
    }

    private static string? GetClosestMatch(string value, IReadOnlyCollection<string> candidates)
    {
        var best = candidates
            .Select(candidate => new { Value = candidate, Distance = GetEditDistance(value, candidate) })
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();

        return best is not null && best.Distance <= 2 ? best.Value : null;
    }

    private static int GetEditDistance(string left, string right)
    {
        var distances = new int[left.Length + 1, right.Length + 1];

        for (var i = 0; i <= left.Length; i++)
        {
            distances[i, 0] = i;
        }

        for (var j = 0; j <= right.Length; j++)
        {
            distances[0, j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[left.Length, right.Length];
    }
}
