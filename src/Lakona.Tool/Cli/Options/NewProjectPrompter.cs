using Lakona.Tool.Domain;

namespace Lakona.Tool.Cli.Options;

internal sealed class NewProjectPrompter(global::ToolText text, global::ICliTerminal terminal)
{
    public NewProjectOptions Complete(NewProjectOptions options)
    {
        if (!RequiresPrompt(options))
        {
            return options;
        }

        if (terminal.IsInputRedirected)
        {
            throw new global::CliUsageException(text.MissingNonInteractiveNewOptions);
        }

        terminal.WriteLine(text.InteractiveNewHeader);

        if (!options.HasExplicit(NewProjectOptionPresence.Name))
        {
            options = options with
            {
                ProjectName = PromptText(text.ProjectNamePrompt, "MyGame"),
                Presence = options.Presence | NewProjectOptionPresence.Name
            };
        }

        if (!options.HasExplicit(NewProjectOptionPresence.ClientEngine))
        {
            options = options with
            {
                ClientEngine = PromptChoice(
                    text.ClientEnginePrompt,
                    [ClientEngine.Unity, ClientEngine.UnityCn, ClientEngine.Tuanjie, ClientEngine.Godot],
                    ClientEngine.Unity),
                Presence = options.Presence | NewProjectOptionPresence.ClientEngine
            };
        }

        if (!options.HasExplicit(NewProjectOptionPresence.Transport))
        {
            options = options with
            {
                Transport = PromptChoice(
                    text.TransportPrompt,
                    [TransportKind.Tcp, TransportKind.WebSocket, TransportKind.Kcp],
                    TransportKind.Kcp),
                Presence = options.Presence | NewProjectOptionPresence.Transport
            };
        }

        if (!options.HasExplicit(NewProjectOptionPresence.Serializer))
        {
            options = options with
            {
                Serializer = PromptChoice(
                    text.SerializerPrompt,
                    [SerializerKind.Json, SerializerKind.MemoryPack],
                    SerializerKind.MemoryPack),
                Presence = options.Presence | NewProjectOptionPresence.Serializer
            };
        }

        return options;
    }

    private static bool RequiresPrompt(NewProjectOptions options)
    {
        return !options.HasExplicit(NewProjectOptionPresence.Name)
            || !options.HasExplicit(NewProjectOptionPresence.ClientEngine)
            || !options.HasExplicit(NewProjectOptionPresence.Transport)
            || !options.HasExplicit(NewProjectOptionPresence.Serializer);
    }

    private string PromptText(string label, string defaultValue)
    {
        terminal.Write($"{label} ({defaultValue}): ");
        var value = terminal.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private T PromptChoice<T>(string label, IReadOnlyList<T> values, T defaultValue)
        where T : struct, Enum
    {
        while (true)
        {
            terminal.WriteLine($"{label}:");
            for (var i = 0; i < values.Count; i++)
            {
                terminal.WriteLine($"  {i + 1}) {ToPromptValue(values[i])}");
            }

            var defaultIndex = GetIndex(values, defaultValue) + 1;
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

    private static string ToPromptValue<T>(T value)
        where T : struct, Enum
    {
        return value switch
        {
            ClientEngine clientEngine => Rendering.ToolEnumText.ToCliValue(clientEngine),
            TransportKind transport => Rendering.ToolEnumText.ToCliValue(transport),
            SerializerKind serializer => Rendering.ToolEnumText.ToCliValue(serializer),
            _ => value.ToString()
        };
    }

    private static int GetIndex<T>(IReadOnlyList<T> values, T value)
        where T : struct, Enum
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value))
            {
                return index;
            }
        }

        return 0;
    }
}
