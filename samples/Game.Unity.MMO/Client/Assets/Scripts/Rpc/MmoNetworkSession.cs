#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Generated;
using Lakona.Game.Client;
using Shared.Interfaces;

namespace Game.Unity.MMO.Client.Rpc
{
    public sealed class MmoNetworkSession : IAsyncDisposable
    {
        private LakonaGameClient? _client;
        private IWorldService? _world;

        public bool IsConnected { get; private set; }

        public async Task<EnterWorldReply> ConnectAndEnterAsync(
            string host,
            int port,
            string path,
            string characterName,
            IWorldCallback callback,
            CancellationToken cancellationToken)
        {
            _client = new LakonaGameClient(WebSocketClientFactory.CreateOptions(host, port, path), callback);
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _world = _client.Api.Shared.World;
            var reply = await _world.EnterWorldAsync(new EnterWorldRequest { CharacterName = characterName }).ConfigureAwait(false);
            IsConnected = reply.Code == 0;
            return reply;
        }

        public ValueTask SubmitAsync(CharacterCommand command)
        {
            return _world is null ? default : _world.SubmitCommandAsync(command);
        }

        public async ValueTask DisposeAsync()
        {
            if (_world is not null && IsConnected)
            {
                try { await _world.LeaveWorldAsync(new LeaveWorldRequest()).ConfigureAwait(false); } catch { }
            }
            if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
            _world = null;
            _client = null;
            IsConnected = false;
        }
    }
}
