using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Features;

public abstract class LakonaGameFeature
{
    public virtual bool Discoverable => true;

    public virtual IReadOnlyDictionary<string, string> Metadata => new Dictionary<string, string>(StringComparer.Ordinal);

    public virtual void ConfigureServices(LakonaGameFeatureContext context)
    {
    }

    public virtual ValueTask StartAsync(
        LakonaGameFeatureContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }

    public virtual ValueTask StopAsync(
        LakonaGameFeatureContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }
}
