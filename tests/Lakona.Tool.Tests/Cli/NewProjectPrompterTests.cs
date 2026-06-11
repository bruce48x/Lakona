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
        Assert.Equal(ClientEngine.Godot, options.ClientEngine);
        Assert.Equal(TransportKind.WebSocket, options.Transport);
        Assert.Equal(SerializerKind.Json, options.Serializer);
        Assert.Equal(PersistenceKind.None, options.Persistence);
        Assert.Equal(NuGetForUnitySource.OpenUpm, options.NuGetForUnitySource);
        Assert.Equal(DeploymentProfile.None, options.DeploymentProfile);
    }

    private sealed class FakeTerminal : ICliTerminal
    {
        private readonly Queue<string?> input;

        public FakeTerminal(IEnumerable<string?> input, bool isInputRedirected = false)
        {
            this.input = new Queue<string?>(input);
            IsInputRedirected = isInputRedirected;
        }

        public bool IsInputRedirected { get; }
        public bool IsOutputRedirected => false;

        public string? ReadLine() => input.Count > 0 ? input.Dequeue() : null;

        public void Write(string value)
        {
        }

        public void WriteLine(string value)
        {
        }

        public void WriteErrorLine(string value)
        {
        }
    }
}
