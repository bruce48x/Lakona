using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Diagnostics;

public static class MessageLogServiceCollectionExtensions
{
    public static IServiceCollection AddMessageRecording(this IServiceCollection services, int maxEntriesPerActor = 4096)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IMessageLogStore>(_ => new InMemoryMessageLogStore(maxEntriesPerActor));
        services.TryAddSingleton<MessageReplayer>();

        return services;
    }
}
