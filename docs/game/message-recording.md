# Message Recording

Message recording captures every actor message dispatch with its result (success or error) into a pluggable log store. It enables offline debugging via replay of the exact message sequence that led to a bug.

## Architecture

Recording happens **inside the actor dispatch pipeline** — in `ActorCell.DispatchAsync()`. When `IMessageLogStore` is registered in DI, every message is automatically recorded after processing:

```
TellAsync / AskAsync
    └─ ActorCell.DispatchAsync()
         ├─ activate + execute callback
         ├─ if error: capture exception type
         └─ finally:
              if IMessageLogStore registered
              └─ MessageLogEntry(timestamp, state, error?)
                   └─ store.RecordAsync(actorId, entry)
```

This is done with a fire-and-forget pattern (`_ = store.RecordAsync(...)`) in the finally block — recording never blocks message processing.

## Usage

### Register recording

```csharp
// Program.cs
builder.Services.AddMessageRecording(maxEntriesPerActor: 4096);
```

One line. That's it. `AddMessageRecording()` registers:
- `IMessageLogStore` as `InMemoryMessageLogStore` (singleton)
- `MessageReplayer` (singleton)

### Query recorded messages

```csharp
var store = provider.GetRequiredService<IMessageLogStore>();
var log = await store.GetLogAsync(ActorId.From("player/alice"));

foreach (var entry in log)
{
    Console.WriteLine($"{entry.Timestamp:HH:mm:ss.fff} | error={entry.Error ?? "ok"}");
}
```

### Clear recorded messages

```csharp
await store.ClearAsync(ActorId.From("player/alice"));
```

## MessageLogEntry

```csharp
public sealed record MessageLogEntry(
    DateTimeOffset Timestamp,
    object Message,        // the delegate state from ActorRuntimeEnvelope
    string? Error);        // exception type FullName, or null if success
```

## Plugging a custom store

Implement `IMessageLogStore` to persist to a file, database, or remote service:

```csharp
public interface IMessageLogStore
{
    ValueTask RecordAsync(ActorId actorId, MessageLogEntry entry, CancellationToken ct);
    ValueTask<IReadOnlyList<MessageLogEntry>> GetLogAsync(ActorId actorId, CancellationToken ct);
    ValueTask ClearAsync(ActorId actorId, CancellationToken ct);
}
```

Then replace the default:

```csharp
services.AddSingleton<IMessageLogStore, MyFileMessageLogStore>();
services.AddMessageRecording(); // MessageReplayer still registered
```

## InMemoryMessageLogStore

The built-in store is a ring buffer: when `maxEntriesPerActor` is exceeded, the oldest entry is evicted. Default capacity is 4096 entries per actor. Entry lists are stored in a `ConcurrentDictionary<ActorId, List<MessageLogEntry>>` with per-actor locks.

## Known limitations

- The `Message` field contains the raw `ActorRuntimeEnvelope.State` object — a delegate or callback — not a user-facing business message. For human-readable recording, wrap the delegate in a type that implements `ToString()`.
- Recording happens in a fire-and-forget pattern. If the store's `RecordAsync` throws, the exception is silently swallowed (no dead letter, no log). A custom store should handle its own error logging.
