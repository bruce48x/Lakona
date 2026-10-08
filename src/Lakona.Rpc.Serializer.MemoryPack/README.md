# Lakona.Rpc.Serializer.MemoryPack

MemoryPack payload serialization for Lakona.Rpc. Pass the serializer to your
RPC client options or server host builder.

## Install

```bash
dotnet add package Lakona.Rpc.Serializer.MemoryPack
```

## Usage

```csharp
using Lakona.Rpc.Serializer.MemoryPack;
var serializer = new MemoryPackRpcSerializer();
```

The constructor also accepts `MemoryPackSerializerOptions` for custom options.

## Guides

- [Transport and serializer extensions](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/architecture.md#transport-and-serializer-are-replaceable): composition and serialization contracts.
- [Source generation](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/source-generation.md): serializer-compatible contracts and generated APIs.
- [Define RPC contract](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-define-rpc-contract/SKILL.md): DTO authoring workflow.
