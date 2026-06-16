namespace Lakona.Game.Server.Sessions;

public sealed class ClientNotificationCommand
{
    public string OwnerKey { get; init; } = "";
    public string SessionId { get; init; } = "";
    public long Generation { get; init; }
    public string CallbackContractType { get; init; } = "";
    public string MethodName { get; init; } = "";
    public List<ClientNotificationArgument> Arguments { get; init; } = [];

    public GameSessionKey ToSessionKey()
    {
        return new GameSessionKey(OwnerKey, SessionId, Generation);
    }
}

public sealed class ClientNotificationArgument
{
    public string TypeName { get; init; } = "";
    public byte[] Payload { get; init; } = [];
}
