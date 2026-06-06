# Feature Catalog Startup

Lakona.Game startup is assembled through the Feature Catalog. The canonical configuration and startup model is documented in [Lakona.Game Configuration And Startup Model](lakona-game-configuration-startup.md).

Features are ordered startup units. They register game services, declare ordering and feature dependencies, and state which endpoint transports they require. Endpoint transport hosting remains framework-owned and is resolved from `Lakona.Game:Endpoints[]`.

## Concepts

| Concept | Responsibility |
|---------|---------------|
| `LakonaGameFeature` | A startup unit that registers services through `ConfigureServices(LakonaGameFeatureContext)`. |
| Feature Catalog | The `Program.cs` declaration of known project features, their order, dependencies, and transport requirements. |
| `Lakona.Game:Feature` | Optional compact configuration selection for which catalog features run in this process. If omitted, all registered features run. |

The previous role/filter startup model is superseded. Do not use role-shaped configuration for new Lakona.Game startup code.

## Define Features

```csharp
public sealed class RealtimeFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        context.Services.AddSingleton<RoomRuntime>();
    }
}
```

```csharp
public sealed class MatchmakingFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        context.Services.AddSingleton<MatchmakingService>();
    }
}
```

## Wire Program.cs

```csharp
builder.Services.AddLakonaGame(builder.Configuration, game =>
{
    game.Feature<ClusterFeature>("cluster");

    game.Feature<MatchmakingFeature>("matchmaking")
        .After("cluster")
        .RequiresFeature("cluster")
        .RequiresTransport("websocket");

    game.Feature<RealtimeFeature>("realtime")
        .After("matchmaking")
        .RequiresFeature("matchmaking")
        .RequiresTransport("kcp");
});
```

`After(...)` controls startup order. `RequiresFeature(...)` fails fast when a selected feature is missing a dependency. `RequiresTransport(...)` fails fast when `Lakona.Game:Endpoints[]` does not provide the required transport.

## Select Features

Local development can omit `Lakona.Game:Feature` and run every registered feature. Split processes can select a compact feature set:

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "gateway-1"
    },
    "Feature": ["cluster", "matchmaking"],
    "Endpoints": [
      {
        "Transport": "websocket",
        "Host": "0.0.0.0",
        "Port": 20000,
        "Path": "/ws"
      }
    ]
  }
}
```

Business concepts such as `matchmaking` or `realtime` are feature names, not endpoint names. Endpoint routing is selected by transport requirements and the resolved endpoint catalog.
