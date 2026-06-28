using System.Text.Json;

namespace Server.Hotfix.Services;

internal static class StateStoreUserActorPlacement
{
    public const string FeatureName = "state-store";

    public const string EnsureUserActorKind = "agar.state-store.ensure-user-actor.v1";

    public static readonly TimeSpan EnsureUserActorTimeout = TimeSpan.FromSeconds(5);

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed class EnsureUserActorRequest
{
    public string UserId { get; set; } = "";
}
