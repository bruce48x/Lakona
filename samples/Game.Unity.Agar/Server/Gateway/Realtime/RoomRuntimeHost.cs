using Microsoft.Extensions.DependencyInjection;
using Agar.Sample.State.Contracts.Rooms;
using Shared.Interfaces;

namespace Gateway.Realtime;

internal sealed class RoomRuntimeHost
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RoomRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly IServiceProvider _services;

    public RoomRuntimeHost(IServiceProvider services)
    {
        _services = services;
    }

    public async Task EnsureRoomReadyAsync(RoomSnapshot room)
    {
        RoomRuntime runtime;
        lock (_gate)
        {
            if (!_runtimes.TryGetValue(room.RoomId, out runtime!))
            {
                runtime = ActivatorUtilities.CreateInstance<RoomRuntime>(_services, room);
                _runtimes.Add(room.RoomId, runtime);
            }
        }

        foreach (var player in room.Players)
        {
            await runtime.AddOrUpdatePlayerAsync(player.UserId).ConfigureAwait(false);
        }
    }

    public async Task SubmitInputAsync(string roomId, string playerId, InputMessage input)
    {
        RoomRuntime? runtime;
        lock (_gate)
        {
            _runtimes.TryGetValue(roomId, out runtime);
        }

        if (runtime is not null)
        {
            await runtime.SubmitInputAsync(playerId, input).ConfigureAwait(false);
        }
    }

    public async Task RemovePlayerAsync(string roomId, string playerId)
    {
        RoomRuntime? runtime;
        lock (_gate)
        {
            _runtimes.TryGetValue(roomId, out runtime);
        }

        if (runtime is null)
        {
            return;
        }

        var shouldDispose = await runtime.RemovePlayerAsync(playerId).ConfigureAwait(false);
        if (!shouldDispose)
        {
            return;
        }

        lock (_gate)
        {
            _runtimes.Remove(roomId);
        }

        await runtime.DisposeAsync().ConfigureAwait(false);
    }
}
