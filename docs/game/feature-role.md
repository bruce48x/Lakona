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
