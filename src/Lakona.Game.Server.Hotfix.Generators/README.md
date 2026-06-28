# Lakona.Game.Server.Hotfix.Generators

Source generators for Lakona.Game server Hotfix behaviors and generated RPC service proxies.

The first generator slice discovers `[HotfixState]` partial classes and emits generated friend accessors for private fields.

```csharp
[HotfixState]
public sealed partial class PlayerActor : Actor<PlayerId>
{
    private int exp;
}
```

Generates an editor-hidden accessor similar to:

```csharp
public int __hotfix_exp()
{
    return exp;
}
```

Types marked `[HotfixState]` must be partial. Nested hotfix state also requires partial containing types. Compiler-generated backing fields, static fields, and const fields are ignored.

Generated accessors are public by necessity: they live in the stable assembly
and must be callable from the separate hotfix assembly. They are hidden from
normal IntelliSense but are not a security boundary. `[FriendOf]` identifies the
intended Hotfix behavior relationship; it does not prevent other code with a
stable actor reference from calling generated `__hotfix_` members.

For server app projects, the generator emits RPC service binders and required hotfix contract providers. `LakonaGameServer.RunAsync` discovers both automatically from the application assembly; generated projects do not call a builder extension. Generated proxies construct `HotfixServiceCall<TRequest>` wrappers, pass the active RPC connection id, and route calls through the hotfix dispatcher. Generated endpoint binders make hotfix-backed services visible to `Lakona:Endpoints[]:RpcServices` validation; service names come from `ApiName` when set, otherwise from the RPC interface name such as `IChatService` -> `chat`. Hand-written service marker files are no longer part of generated projects.
