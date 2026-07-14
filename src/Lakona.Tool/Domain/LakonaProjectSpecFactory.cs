using Lakona.Tool.Cli.Options;

namespace Lakona.Tool.Domain;

// Adapts CLI options for legacy unit-test seams. All project defaults and
// validation remain owned by ProjectSystem's ProjectSpecFactory.
internal sealed class LakonaProjectSpecFactory
{
    private readonly ProjectSpecFactory inner = new();

    public LakonaProjectSpec Create(NewProjectOptions options)
    {
        return inner.Create(options.ToCreationRequest());
    }
}
