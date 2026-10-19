using FrameworkBenchmark.Lakona.Peers.Generated;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace FrameworkBenchmark.Lakona.Server;

[ComponentState]
public sealed class BenchmarkState : IAsyncDisposable
{
    internal LakonaGameClient? Worker;

    public async ValueTask DisposeAsync()
    {
        if (Worker is not null)
            await Worker.DisposeAsync();
    }
}
