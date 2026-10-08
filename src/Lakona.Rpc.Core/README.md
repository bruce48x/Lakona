# Lakona.Rpc.Core

Shared RPC contracts, transport and serializer interfaces, protocol primitives,
and compiler extensions. This package has no dependency on concrete transports
or serializers. Use it with `Lakona.Rpc.Client` or `Lakona.Rpc.Server`.

## Install

```bash
dotnet add package Lakona.Rpc.Core
```

## Guides

- [Public API boundaries](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/public-api-boundaries.md): application and extension interfaces.
- [Source generation](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/source-generation.md): contract attributes, analyzers, and generated APIs.
- [Wire protocol](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/wire-protocol-v1.md), [Status model](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/status-error-model.md): framing and failure contracts.
- [Define RPC contract](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-define-rpc-contract/SKILL.md): agent workflow for stable IDs and DTO evolution.
