using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Xunit;

namespace Lakona.Tool.Tests.Cli;

public sealed class NewProjectPrompterTests
{
    [Fact]
    public void Complete_ThrowsWhenRequiredOptionsMissingAndInputRedirected()
    {
        var prompter = new NewProjectPrompter(ToolText.ForCulture(System.Globalization.CultureInfo.InvariantCulture), new FakeTerminal([], isInputRedirected: true));

        var exception = Assert.Throws<CliUsageException>(() => prompter.Complete(NewProjectOptionParser.Parse([])));

        Assert.Contains("Missing required options", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Complete_PromptsOnlyRequiredValues()
    {
        var terminal = new FakeTerminal(["Arena", "4", "2", "1"]);
        var prompter = new NewProjectPrompter(ToolText.ForCulture(System.Globalization.CultureInfo.InvariantCulture), terminal);

        var options = prompter.Complete(NewProjectOptionParser.Parse([]));

        Assert.Equal("Arena", options.ProjectName);
        Assert.Equal(ClientEngine.Console, options.ClientEngine);
        Assert.Equal(TransportKind.WebSocket, options.Transport);
        Assert.Equal(SerializerKind.Json, options.Serializer);
        Assert.Equal(PersistenceKind.None, options.Persistence);
        Assert.Equal(NuGetForUnitySource.OpenUpm, options.NuGetForUnitySource);
        Assert.Equal(DeploymentProfile.None, options.DeploymentProfile);
    }

    [Fact]
    public void Complete_ClientEnginePromptListsConsole()
    {
        var terminal = new FakeTerminal(["Arena", "1", "1", "1"]);
        var prompter = new NewProjectPrompter(ToolText.ForCulture(System.Globalization.CultureInfo.InvariantCulture), terminal);

        _ = prompter.Complete(NewProjectOptionParser.Parse([]));

        Assert.Contains(terminal.Output, line => line.Contains("console", StringComparison.Ordinal));
        Assert.DoesNotContain(terminal.Output, line => line.Contains("unity-cn", StringComparison.Ordinal));
    }

    private sealed class FakeTerminal : ICliTerminal
    {
        private readonly Queue<string?> input;
        private readonly List<string> output = [];

        public FakeTerminal(IEnumerable<string?> input, bool isInputRedirected = false)
        {
            this.input = new Queue<string?>(input);
            IsInputRedirected = isInputRedirected;
        }

        public bool IsInputRedirected { get; }
        public bool IsOutputRedirected => false;
        public IReadOnlyList<string> Output => output;

        public string? ReadLine() => input.Count > 0 ? input.Dequeue() : null;

        public void Write(string value)
        {
            output.Add(value);
        }

        public void WriteLine(string value)
        {
            output.Add(value);
        }

        public void WriteErrorLine(string value)
        {
        }
    }
}
