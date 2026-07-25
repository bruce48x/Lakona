namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Configures application HTTP ingress for one server process.
/// </summary>
public sealed class LakonaHttpOptions
{
    public IReadOnlyList<LakonaHttpListenerOptions> Listeners { get; init; } = [];

    public static LakonaHttpOptions Defaults()
    {
        return new LakonaHttpOptions();
    }

    internal static void Validate(IReadOnlyList<LakonaHttpListenerOptions> listeners)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < listeners.Count; index++)
        {
            var listener = listeners[index];
            var path = $"Lakona:Http:Listeners:{index}";
            if (string.IsNullOrWhiteSpace(listener.Id))
            {
                throw new InvalidOperationException($"{path}:Id must not be empty.");
            }

            if (!ids.Add(listener.Id))
            {
                throw new InvalidOperationException(
                    $"Lakona:Http:Listeners contains duplicate listener id '{listener.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(listener.Host))
            {
                throw new InvalidOperationException($"{path}:Host must not be empty.");
            }

            if (listener.Port is < 1 or > 65535)
            {
                throw new InvalidOperationException(
                    $"{path}:Port must be between 1 and 65535.");
            }

            if (listener.MaximumBodyBytes <= 0)
            {
                throw new InvalidOperationException(
                    $"{path}:MaximumBodyBytes must be greater than zero.");
            }

            if (listener.RequestTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException(
                    $"{path}:RequestTimeoutSeconds must be greater than zero.");
            }

            if (listener.Services.Count == 0
                || listener.Services.Any(static service => string.IsNullOrWhiteSpace(service)))
            {
                throw new InvalidOperationException(
                    $"{path}:Services must contain at least one non-empty service name.");
            }

            var duplicateService = listener.Services
                .GroupBy(static service => service, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(static group => group.Count() > 1);
            if (duplicateService is not null)
            {
                throw new InvalidOperationException(
                    $"{path}:Services contains duplicate service '{duplicateService.Key}'.");
            }
        }
    }
}

public sealed class LakonaHttpListenerOptions
{
    public const int DefaultMaximumBodyBytes = 1024 * 1024;
    public const int DefaultRequestTimeoutSeconds = 30;

    public string Id { get; init; } = "";

    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; }

    public LakonaHttpExposure Exposure { get; init; } = LakonaHttpExposure.Internal;

    public IReadOnlyList<string> Services { get; init; } = [];

    public int MaximumBodyBytes { get; init; } = DefaultMaximumBodyBytes;

    public int RequestTimeoutSeconds { get; init; } = DefaultRequestTimeoutSeconds;
}

public enum LakonaHttpExposure
{
    Internal,
    Public
}
