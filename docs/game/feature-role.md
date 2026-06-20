# Feature Startup

Lakona.Game startup is assembled from `LakonaGameFeature` types. The current
configuration, discovery, and cluster publication model is documented in
[Distributed Feature And Cluster Model](distributed-feature-cluster-model.md).

## Concepts

| Concept | Responsibility |
|---------|---------------|
| `LakonaGameFeature` | A startup unit that registers services through `ConfigureServices(LakonaGameFeatureContext)`. |
| Feature name | The conventional kebab-case name derived from the type name, such as `BattleRuntimeFeature` -> `battle-runtime`. |
| `Lakona:Feature` | Optional compact configuration selection for which discovered features run in this process. |

The previous role/filter model and the older hand-written fluent catalog are
superseded for generated projects and new samples. Do not use
role-shaped configuration or endpoint names for new Lakona.Game startup code.

Feature classes belong in stable `Server.App`. A Feature describes which
capability this process starts and, when discoverable, publishes to the
cluster. It is not the place for replaceable game rules. Business behavior
behind a Feature belongs in `Server.Hotfix` services and actor behaviors.

## Define Features

```csharp
public sealed class BattleRuntimeFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        context.Services.AddSingleton<RoomRuntime>();
    }
}
```

The Feature may register a hosted service or runtime host. That hosted service
may raise hotfix runtime events through a stable App adapter. The Feature must
not decide matchmaking batches, room results, leaderboard ranks, login policy,
presence cleanup, or product DTO projection.

## Wire Program.cs

Generated projects should use convention-based discovery:

```csharp
builder.Services.AddLakonaGame(builder.Configuration);
```

Samples that need a bounded explicit set may pass feature types while still
using conventional names:

```csharp
builder.Services.AddLakonaGame(builder.Configuration, [
    typeof(DatabaseFeature),
    typeof(StateStoreFeature),
    typeof(BattleRuntimeFeature)
]);
```

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
