using Lakona.Game.Server.Hotfix.Abstractions;
using StackExchange.Redis;

namespace Server.App.Infrastructure.Redis;

[ComponentState]
public sealed class RedisLeaderboardState : IDisposable
{
    public ConnectionMultiplexer? Connection;
    public string KeyPrefix = "";

    public void Dispose() => Interlocked.Exchange(ref Connection, null)?.Dispose();
}
