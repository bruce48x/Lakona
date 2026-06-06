using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Features;

public interface IFeature
{
    void Configure(IServiceCollection services, IConfiguration config);
}
