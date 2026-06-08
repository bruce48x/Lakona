internal sealed class NewCommandPrompter(ToolText text, ICliTerminal terminal)
{
    public NewCommandOptions Complete(NewCommandOptions options)
    {
        if (!RequiresPrompt(options))
        {
            return options;
        }

        if (terminal.IsInputRedirected)
        {
            throw new CliUsageException(text.MissingNonInteractiveNewOptions);
        }

        terminal.WriteLine(text.InteractiveNewHeader);

        if (!options.HasExplicit(NewCommandOptionPresence.Name))
        {
            options = options with
            {
                Name = PromptText(text.ProjectNamePrompt, ProjectConventions.DefaultProjectName),
                Presence = options.Presence | NewCommandOptionPresence.Name
            };
        }

        if (!options.HasExplicit(NewCommandOptionPresence.ClientEngine))
        {
            options = options with
            {
                ClientEngine = PromptChoice(text.ClientEnginePrompt, ProjectConventions.SupportedClientEngines, ProjectConventions.DefaultClientEngine),
                Presence = options.Presence | NewCommandOptionPresence.ClientEngine
            };
        }

        if (!options.HasExplicit(NewCommandOptionPresence.Transport))
        {
            options = options with
            {
                Transport = PromptChoice(text.TransportPrompt, ProjectConventions.SupportedTransports, ProjectConventions.DefaultTransport),
                Presence = options.Presence | NewCommandOptionPresence.Transport
            };
        }

        if (!options.HasExplicit(NewCommandOptionPresence.Serializer))
        {
            options = options with
            {
                Serializer = PromptChoice(text.SerializerPrompt, ProjectConventions.SupportedSerializers, ProjectConventions.DefaultSerializer),
                Presence = options.Presence | NewCommandOptionPresence.Serializer
            };
        }

        return options;
    }

    private static bool RequiresPrompt(NewCommandOptions options)
    {
        if (!options.HasExplicit(NewCommandOptionPresence.Name) ||
            !options.HasExplicit(NewCommandOptionPresence.ClientEngine) ||
            !options.HasExplicit(NewCommandOptionPresence.Transport) ||
            !options.HasExplicit(NewCommandOptionPresence.Serializer))
        {
            return true;
        }

        return false;
    }

    private string PromptText(string label, string defaultValue)
    {
        terminal.Write($"{label} ({defaultValue}): ");
        var value = terminal.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private string PromptChoice(string label, IReadOnlyList<string> values, string defaultValue)
    {
        while (true)
        {
            terminal.WriteLine($"{label}:");
            for (var i = 0; i < values.Count; i++)
            {
                terminal.WriteLine($"  {i + 1}) {values[i]}");
            }

            var defaultIndex = Array.IndexOf(values.ToArray(), defaultValue) + 1;
            terminal.Write($"{label} ({defaultIndex}): ");
            var value = terminal.ReadLine();
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (int.TryParse(value.Trim(), out var selection) &&
                selection >= 1 &&
                selection <= values.Count)
            {
                return values[selection - 1];
            }

            terminal.WriteLine(text.InvalidSelection(value.Trim(), values.Count));
        }
    }
}
