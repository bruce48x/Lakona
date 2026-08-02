using Game.Unity.MMO.Server.App.World;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Server.App.Generated;
using Shared.Interfaces;

namespace Game.Unity.MMO.Server.Hotfix.World;

[HotfixService(typeof(IWorldService))]
internal sealed class WorldService
{
    private readonly ActorAccess _actors;

    public WorldService(ActorAccess actors) => _actors = actors;

    public async ValueTask<EnterWorldReply> EnterWorldAsync(WorldServiceCall<EnterWorldRequest> call)
    {
        var name = (call.Request.CharacterName ?? "").Trim();
        if (name.Length is < 1 or > 24)
        {
            return new EnterWorldReply { Code = 1, Message = "Character name must contain 1-24 characters." };
        }

        var characterId = NormalizeCharacterId(name);
        var session = await call.GameServer.StartSessionAsync(characterId, call.ConnectionId).ConfigureAwait(false);
        var result = await _actors.Startup<ZoneActor>(new ZoneId(WorldProtocol.DefaultZoneId)).CallAsync(
            static behavior => behavior.EnterAsync,
            new ZoneEnterRequest { CharacterId = characterId, CharacterName = name, Session = session },
            CancellationToken.None).ConfigureAwait(false);
        return new EnterWorldReply
        {
            Code = result.Accepted ? 0 : 2,
            Message = result.Message,
            CharacterId = characterId,
            ZoneId = WorldProtocol.DefaultZoneId,
            Snapshot = result.Snapshot
        };
    }

    public async ValueTask SubmitCommandAsync(WorldServiceCall<CharacterCommand> call)
    {
        var session = call.CurrentSession;
        if (session is null) return;
        await _actors.Startup<ZoneActor>(new ZoneId(WorldProtocol.DefaultZoneId)).CallAsync(
            static behavior => behavior.SubmitCommandAsync,
            new ZoneCommandRequest { CharacterId = session.Value.OwnerKey, Session = session.Value, Command = call.Request },
            CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask LeaveWorldAsync(WorldServiceCall<LeaveWorldRequest> call)
    {
        var session = call.CurrentSession;
        if (session is null) return;
        await _actors.Startup<ZoneActor>(new ZoneId(WorldProtocol.DefaultZoneId)).CallAsync(
            static behavior => behavior.LeaveAsync,
            new ZoneLeaveRequest { CharacterId = session.Value.OwnerKey, Session = session.Value },
            CancellationToken.None).ConfigureAwait(false);
    }

    private static string NormalizeCharacterId(string name)
    {
        var chars = name.ToLowerInvariant().Where(static ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray();
        var slug = new string(chars);
        return string.IsNullOrWhiteSpace(slug) ? $"character-{Guid.NewGuid():N}" : $"character-{slug}";
    }
}
