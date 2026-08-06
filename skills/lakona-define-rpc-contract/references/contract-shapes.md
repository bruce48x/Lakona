# Lakona RPC Contract Shapes

Use project-local conventions when they differ from illustrative names below. Verify the installed Lakona package version and current source before assuming an API shape.

## Contract Ownership

Keep wire-facing declarations in Shared:

```csharp
public static partial class RpcContractIds
{
    public static class Services
    {
        public const int Stage = 101;
    }

    public static class StageServiceMethods
    {
        public const int GetProgressAsync = 1;
    }

    public static class StageNotifications
    {
        public const int ProgressChanged = 1;
    }
}
```

Large projects may split the partial ID registry by domain. Search the whole Shared assembly before assigning an ID.

## ID Stability

- Service IDs are globally unique within the application protocol and must be positive for business services.
- Method IDs are unique within their service.
- Notification IDs are unique within their notification contract.
- A published ID remains attached to the same semantic operation for the lifetime of that compatibility line.
- Reserve removed IDs. Add the next intentional value instead of filling an unexplained gap.
- Never derive a wire ID from declaration order, a hash, or a type name.

If project history does not establish whether an apparently unused value was published, stop and ask instead of reusing it.

## Service And Notification Shape

```csharp
[RpcService(
    RpcContractIds.Services.Stage,
    NotificationContract = typeof(IStageNotifications))]
public interface IStageService
{
    [RpcMethod(RpcContractIds.StageServiceMethods.GetProgressAsync)]
    ValueTask<GetStageProgressReply> GetProgressAsync(
        GetStageProgressRequest request);
}

[RpcNotificationContract]
public interface IStageNotifications
{
    [RpcNotification(RpcContractIds.StageNotifications.ProgressChanged)]
    void ProgressChanged(StageProgressChanged notification);
}
```

An RPC method has exactly one request DTO and returns `ValueTask` or `ValueTask<T>`. A notification has exactly one DTO parameter and returns `void`. Use an explicit empty request DTO rather than omitting the request.

## DTO Shape And Evolution

Follow the serializer already selected by the project. A current MemoryPack project may use:

```csharp
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class GetStageProgressReply
{
    [MemoryPackOrder(0)]
    public bool Success { get; set; }

    [MemoryPackOrder(1)]
    public int Progress { get; set; }

    [MemoryPackOrder(2)]
    public string ErrorCode { get; set; } = string.Empty;
}
```

This is not permission to convert a project using another serializer. Preserve its annotations, partial-type requirements, nullability style, class-versus-struct convention, and versioning rules.

When evolving a DTO:

- preserve the meaning and serialized order of existing members
- append fields using the next valid order when the serializer supports compatible addition
- use Shared-safe primitives, enums, and DTOs
- choose names that describe protocol meaning rather than implementation storage
- model expected rejection or domain failure explicitly when callers need to act on it

Do not place `GameSessionKey`, `RpcSession`, actor instances or references, service providers, delegates, transport connections, or other server runtime objects in a DTO.

## Unity And Generation Constraints

Shared commonly targets `netstandard2.1` for Unity as well as the server target. Inspect its project file and obey its declared C# language version; Unity 2022 compatibility commonly means C# 9 syntax.

Contract generators run during compilation. Do not create a `Generated` source tree, copy generator output, add marker files, or introduce a separate code-generation script unless the repository explicitly owns that workflow.

Validate through the real project graph:

```powershell
dotnet build <path-to-shared-project>
dotnet build <path-to-server-app-project>
```

Also compile the affected Unity or other client through its established validation path when the change alters client-visible contracts.
