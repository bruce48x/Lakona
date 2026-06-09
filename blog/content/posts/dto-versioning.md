+++
title = "DTO Versioning"
date = 2026-05-12T09:30:00+08:00
+++

Lakona.Rpc contracts start from shared C# interfaces and DTOs. The source generator uses `[RpcService]`, `[RpcMethod]`, and DTO types to generate glue code for both sides. The core rule for versioning is: keep the wire shape compatible first, roll out gradually, then remove old fields or methods only after old clients are gone.

## Do Not Reuse Stable IDs

`[RpcService(id)]` and `[RpcMethod(id)]` are part of protocol routing. Do not reuse a service id or method id that has already shipped. After deleting a method, keep a record of its id so old clients cannot route requests to a new meaning by accident.

As in the default starter project, prefer collecting ids into `const int` groups such as `RpcContractIds`: put all service ids under `Services`, each service's method ids under the matching `XxxServiceMethods`, and notification ids under `XxxNotifications`. The ids remain explicit protocol contracts, while reviews become easier and ids are less likely to be scattered across many interface files.

`Lakona.Rpc.Analyzers` rejects non-positive ids, duplicate service ids, duplicate method ids within one service, and duplicate push ids within one callback interface, so these mistakes surface during normal C# editing and builds.

Changing a method signature affects generated code and payload types. For published methods, prefer adding a new method id, for example `GetInventoryV2Async`, then remove the old method only after old clients have been retired.

## JSON DTOs

`JsonRpcSerializer` is based on `System.Text.Json` and forces `IncludeFields = true`. That means both properties and fields may enter the JSON payload.

Relatively safe changes:

- Add optional fields or properties with defaults.
- Make the server tolerate clients that do not send new fields.
- Keep existing field names, types, and meanings unchanged.

High-risk changes:

- Renaming fields or properties.
- Changing a field type, such as from `int` to `string`.
- Turning an optional field into a required business field.
- Removing a field that old clients still send or read.

JSON is better for early integration and debugging because payloads are easier to inspect, but compatibility still depends on DTO design and serializer options on both sides.

## MemoryPack DTOs

In MemoryPack mode, the starter adds `[MemoryPackable]` and `[MemoryPackOrder(n)]` to default DTOs. MemoryPack's binary format depends more on member order and annotations, so versioning should be more conservative.

Recommendations:

- Keep stable `MemoryPackOrder` values for members that may evolve over time.
- Append new fields at the end with new order values.
- Do not reorder existing order values.
- Do not reuse an existing order value for another meaning.
- For breaking changes that need gradual rollout, add a new DTO or a new RPC method.

The current repository does not provide an additional DTO compatibility layer. MemoryPack compatibility follows MemoryPack's own rules. Before release, add cross-version serialization tests: feed old DTO payloads into new DTOs and cover the reverse direction as well.

## Gradual Rollout Order

Recommended rollout:

1. Make the server compatible with both old and new DTOs, or add V2 methods while keeping old methods.
2. Gradually upgrade clients to the new fields or methods.
3. Watch the remaining old-version share and server logs.
4. Delete old fields, methods, or handlers only after old clients no longer call them.

Game clients often survive longer than servers. Do not assume every client upgrades at the same time.

## Not Implemented Today

The current repository does not include a schema registry, automatic DTO diff checks, cross-version compatibility test generation, server routing by client version, or protocol-level deprecation markers. Teams need to enforce these rules in contract reviews and CI.
