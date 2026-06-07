internal static class CliParser
{
    private static readonly string[] NewOptions =
    [
        "--name",
        "--output",
        "--client-engine",
        "--transport",
        "--network-profile",
        "--serializer",
        "--persistence",
        "--nugetforunity-source",
        "--deploy-profile"
    ];

    public static NewCommandOptions ParseNewOptions(string[] args) => ParseNewOptions(args, ToolText.Current);

    internal static NewCommandOptions ParseNewOptions(string[] args, ToolText text)
    {
        string? name = null;
        string? outputPath = null;
        var clientEngine = ProjectConventions.DefaultClientEngine;
        var transport = ProjectConventions.DefaultTransport;
        var serializer = ProjectConventions.DefaultSerializer;
        var persistence = ProjectConventions.DefaultPersistence;
        var nuGetForUnitySource = ProjectConventions.DefaultNuGetForUnitySource;
        var deployProfile = ProjectConventions.DefaultDeployProfile;
        var presence = NewCommandOptionPresence.None;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--name":
                    name = ReadOptionValue(args, ref index, "--name", text);
                    presence |= NewCommandOptionPresence.Name;
                    break;
                case "--output":
                    outputPath = ReadOptionValue(args, ref index, "--output", text);
                    presence |= NewCommandOptionPresence.OutputPath;
                    break;
                case "--client-engine":
                    clientEngine = ValidateChoice("--client-engine", ReadOptionValue(args, ref index, "--client-engine", text), ProjectConventions.SupportedClientEngines, text);
                    presence |= NewCommandOptionPresence.ClientEngine;
                    break;
                case "--transport":
                    transport = ValidateChoice("--transport", ReadOptionValue(args, ref index, "--transport", text), ProjectConventions.SupportedTransports, text);
                    presence |= NewCommandOptionPresence.Transport;
                    break;
                case "--network-profile":
                    ValidateChoice("--network-profile", ReadOptionValue(args, ref index, "--network-profile", text), ProjectConventions.SupportedNetworkProfiles, text);
                    presence |= NewCommandOptionPresence.NetworkProfile;
                    break;
                case "--serializer":
                    serializer = ValidateChoice("--serializer", ReadOptionValue(args, ref index, "--serializer", text), ProjectConventions.SupportedSerializers, text);
                    presence |= NewCommandOptionPresence.Serializer;
                    break;
                case "--persistence":
                    persistence = ValidateChoice("--persistence", ReadOptionValue(args, ref index, "--persistence", text), ProjectConventions.SupportedPersistence, text);
                    presence |= NewCommandOptionPresence.Persistence;
                    break;
                case "--nugetforunity-source":
                    nuGetForUnitySource = ValidateChoice("--nugetforunity-source", ReadOptionValue(args, ref index, "--nugetforunity-source", text), ProjectConventions.SupportedNuGetForUnitySources, text);
                    presence |= NewCommandOptionPresence.NuGetForUnitySource;
                    break;
                case "--deploy-profile":
                    deployProfile = ValidateChoice("--deploy-profile", ReadOptionValue(args, ref index, "--deploy-profile", text), ProjectConventions.SupportedDeployProfiles, text);
                    presence |= NewCommandOptionPresence.DeployProfile;
                    break;
                default:
                    throw CreateUnsupportedArgumentException(args[index], NewOptions, text);
            }
        }

        return new NewCommandOptions(name, outputPath, clientEngine, transport, ProjectConventions.DefaultNetworkProfile, serializer, persistence, nuGetForUnitySource, deployProfile, presence);
    }

    private static string ReadOptionValue(string[] args, ref int index, string optionName, ToolText text)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException(text.MissingValue(optionName));
        }

        return args[++index];
    }

    private static string ValidateChoice(string optionName, string value, IReadOnlyCollection<string> supportedValues, ToolText text)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (supportedValues.Contains(normalized))
        {
            return normalized;
        }

        var suggestion = GetValueSuggestion(optionName, normalized, supportedValues);
        throw new CliUsageException(text.UnsupportedValue(value, optionName, supportedValues, suggestion));
    }

    private static CliUsageException CreateUnsupportedArgumentException(string argument, IReadOnlyList<string> supportedOptions, ToolText text)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            return new CliUsageException(text.UnexpectedArgument(argument));
        }

        var suggestion = GetClosestMatch(argument, supportedOptions);
        return new CliUsageException(text.UnsupportedOption(argument, suggestion));
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
