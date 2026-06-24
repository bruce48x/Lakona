using MemoryPack;

namespace Lakona.Game.Server.Sessions;

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ClientNotificationCommand
{
    [MemoryPackOrder(0)]
    public string OwnerKey { get; init; } = "";
    [MemoryPackOrder(1)]
    public string SessionId { get; init; } = "";
    [MemoryPackOrder(2)]
    public long Generation { get; init; }
    [MemoryPackOrder(3)]
    public string CallbackContractType { get; init; } = "";
    [MemoryPackOrder(4)]
    public string MethodName { get; init; } = "";
    [MemoryPackOrder(5)]
    public List<ClientNotificationArgument> Arguments { get; init; } = [];

    public GameSessionKey ToSessionKey()
    {
        return new GameSessionKey(OwnerKey, SessionId, Generation);
    }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ClientNotificationArgument
{
    [MemoryPackOrder(0)]
    public string TypeName { get; init; } = "";
    [MemoryPackOrder(1)]
    public byte[] Payload { get; init; } = [];
}
