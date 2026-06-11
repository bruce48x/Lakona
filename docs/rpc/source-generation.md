# RPC Source Generation

Status: active

Lakona.Rpc uses Roslyn source generators as the normal RPC glue route for
generated Lakona projects and hand-written applications.

## Contract

RPC service interfaces, method ids, notification contracts, and DTOs live in a
shared assembly. `Lakona.Rpc.Analyzers` reads those contracts at compilation
time and emits:

- client facades and service clients
- notification binders
- server binders
- generated binder assembly metadata

Generated RPC glue is compiler output. New Lakona projects must not contain
project-local `Generated/` RPC source folders, codegen scripts, editor
postprocessors, or tool manifests for day-to-day RPC generation.

## Project Configuration

Generated server projects opt into server glue with:

```xml
<LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>
<LakonaRpcServerGeneratedNamespace>Server.App.Generated</LakonaRpcServerGeneratedNamespace>
```

Generated client projects opt into client glue with the matching client
generation property or Unity-compatible analyzer configuration.

## Ownership

- `Lakona.Rpc.Core` owns runtime attributes and shared contracts.
- `Lakona.Rpc.Analyzers` owns compile-time diagnostics and source generation.
- `Lakona.Tool` owns generated project files and package references, but does
  not write generated RPC glue as source files.

Historical standalone starter decisions are archived under
`docs/rpc/archive/starter/`.
