# Configuration

Lakona runtime configuration is bound from the `Lakona` root. The runtime model
is node, endpoints, actor hosting, startup actors, cluster infrastructure,
heartbeat, hotfix, and observability.

## Root Shape

```json
{
  "Lakona": {
    "Node": {
      "Id": "dev-1"
    },
    "ActorHosts": [ "user", "matchmaking", "leaderboard", "room" ],
    "StartupActors": [
      "matchmaking",
      {
        "Name": "leaderboard",
        "Options": {
          "period": "weekly"
        }
      }
    ],
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "memorypack",
        "Host": "127.0.0.1",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "player" ]
      }
    ],
    "Cluster": {
      "Endpoint": "tcp://127.0.0.1:21001",
      "Serializer": "memorypack",
      "Seeds": [ "tcp://127.0.0.1:21001" ]
    }
  }
}
```

## Environment Variables

.NET configuration supports arrays with numeric keys and JSON string values.
Lakona accepts both forms for arrays:

```bash
Lakona__ActorHosts__0=user
Lakona__ActorHosts__1=matchmaking
Lakona__StartupActors__0=matchmaking
```

```bash
Lakona__ActorHosts='["user","matchmaking"]'
Lakona__StartupActors='["matchmaking"]'
```

Use JSON string arrays in Docker Compose when compact values are easier to read:

```yaml
environment:
  Lakona__Node__Id: data-1
  Lakona__ActorHosts: '["user","matchmaking","leaderboard"]'
  Lakona__StartupActors: '["matchmaking","leaderboard"]'
  Lakona__Cluster__Endpoint: tcp://0.0.0.0:21001
  Lakona__Cluster__Serializer: memorypack
```

## Endpoints

`Lakona:Endpoints[]` declares client-facing RPC listeners. Each endpoint owns
its transport, serializer, bind host, advertised host, port, path, and exposed
RPC services. Endpoint serializers are separate from the cluster serializer.

## Cluster

`Lakona:Cluster` declares node-to-node infrastructure:

- `Endpoint`: local advertised cluster endpoint.
- `Serializer`: `json` or `memorypack`.
- `Seeds`: endpoints used to reach shared directory services.
- `Directory`: optional SQL directory provider configuration.

Shared directory storage belongs under `Lakona:Cluster:Directory`. Application
databases belong under application-owned configuration roots.

## Actor Hosting

`Lakona:ActorHosts` is the list of actor kinds this node may create locally.
`Lakona:StartupActors` is the list of named startup declarations activated on
this node. Startup entries may be strings or objects with `Name` and `Options`.

Actor placement policy belongs in code. Keep per-node configuration limited to
the node's allowed actor kinds and startup declarations.

## Validation

Readiness validation checks node identity, endpoints, cluster endpoint shape,
actor host names, startup actor names, hotfix source, heartbeat policy, and
observability settings. Enable the independent health HTTP host and request the
ready endpoint from a live process:

```bash
curl http://127.0.0.1:20080/_lakona/health/ready
```

The validation boundary should report configuration problems before runtime
listeners are opened.
