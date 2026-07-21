namespace Lakona.Game.Server.Sessions;

public interface IGameSessionIdFactory
{
    string Create();
}

internal sealed class RandomGameSessionIdFactory : IGameSessionIdFactory
{
    public string Create() => Guid.NewGuid().ToString("N");
}
