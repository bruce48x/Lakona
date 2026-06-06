using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Features;

public abstract class LakonaGameFeature
{
    public virtual void ConfigureServices(LakonaGameFeatureContext context)
    {
    }
}
