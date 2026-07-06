using Microsoft.Extensions.DependencyInjection;

namespace Server.Hotfix.Services;

internal static class HotfixNotificationServices
{
    public static MatchmakingNotifier GetMatchmakingNotifier(IServiceProvider services)
    {
        return services.GetService<MatchmakingNotifier>() ??
            ActivatorUtilities.CreateInstance<MatchmakingNotifier>(services);
    }
}
