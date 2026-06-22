# Feature Startup

Lakona.Game startup is assembled from `LakonaGameFeature` types. The current
configuration, discovery, and cluster publication model is documented in
[Distributed Feature And Cluster Model](distributed-feature-cluster-model.md).

## Concepts

| Concept | Responsibility |
|---------|---------------|
| `LakonaGameFeature` | Stable framework infrastructure for local process startup. |
| Hotfix feature descriptor | A reloadable game feature declaration implemented with `HotfixGameFeature` in `Server.Hotfix`. |
| Feature name | The conventional or attributed kebab-case name, such as `battle-runtime`. |
| `Lakona:Feature` | Optional compact configuration selection for which discovered features run in this process. |

The previous role/filter model and the older hand-written fluent catalog are
superseded for generated projects and new samples. Do not use
role-shaped configuration or endpoint names for new Lakona.Game startup code.

Stable `LakonaGameFeature` belongs to framework infrastructure. User-authored
game feature declarations live in the hotfix assembly, where they can describe
reloadable actor runtime loops without adding application-specific runtime
hosts to `Server.App`. Business behavior behind a feature belongs in
`Server.Hotfix` services and actor behaviors.

## Define Features

```csharp
[HotfixFeature("battle-runtime")]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    public override void Configure(HotfixFeatureContext context)
    {
        context.ScheduleActorTick<MatchmakingActor>(
            "default",
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce);

        context.ScheduleActiveActorTicks<RoomActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending);
    }
}
```

The descriptor may declare actor ticks and other reloadable game capabilities.
It must not decide matchmaking batches, room results, leaderboard ranks, login
policy, presence cleanup, or product DTO projection directly; those decisions
belong in hotfix services and actor behaviors.

## Wire Program.cs

Generated projects should use convention-based discovery:

```csharp
builder.Services.AddLakonaGame(builder.Configuration);
```

Samples that need stable framework services should register those services
through normal dependency injection or framework-owned startup features. They
should not add game feature classes to stable `Server.App`.

## Select Features

Local development can omit `Lakona:Feature` and run every discovered feature.
Split processes can select a compact feature set:

```json
{
  "Lakona": {
    "Node": {
      "Id": "battle-1"
    },
    "Feature": [ "battle-runtime" ],
    "Endpoints": [
      {
        "Transport": "kcp",
        "Serializer": "memorypack",
        "Host": "0.0.0.0",
        "Port": 20001,
        "RpcServices": [ "battle" ]
      }
    ]
  }
}
```

Business concepts such as `matchmaking` or `battle-runtime` are feature names,
not endpoint names. RPC service exposure is configured per endpoint through
`RpcServices`.
