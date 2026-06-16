using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Features;

public sealed class LakonaGameFeatureHostedService : IHostedService
{
    private readonly LakonaGameFeatureCatalog _catalog;
    private readonly LakonaGameFeatureContext _context;
    private readonly ILogger<LakonaGameFeatureHostedService> _logger;
    private readonly List<LakonaGameFeature> _started = [];

    public LakonaGameFeatureHostedService(
        LakonaGameFeatureCatalog catalog,
        LakonaGameFeatureContext context,
        ILogger<LakonaGameFeatureHostedService> logger)
    {
        _catalog = catalog;
        _context = context;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var feature in _catalog.ActiveFeatures)
        {
            try
            {
                await feature.StartAsync(_context, cancellationToken).ConfigureAwait(false);
                _started.Add(feature);
            }
            catch
            {
                await StopStartedFeaturesAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopStartedFeaturesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task StopStartedFeaturesAsync(CancellationToken cancellationToken)
    {
        for (var i = _started.Count - 1; i >= 0; i--)
        {
            var feature = _started[i];
            try
            {
                await feature.StopAsync(_context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop Lakona game feature {FeatureType}.", feature.GetType().FullName);
            }
        }

        _started.Clear();
    }
}
