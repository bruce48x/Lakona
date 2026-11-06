#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Rpc.Contracts;
using Client.Generated;

namespace Rpc.Testing
{
    internal sealed class PlayerSessionModule : RpcClient.PlayerNotificationsBase
    {
        private readonly IConnectionSessionHost _host;
        private PlayerServiceClient? _service;

        public PlayerSessionModule(IConnectionSessionHost host)
        {
            _host = host;
        }

        public void Attach(PlayerServiceClient service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public ValueTask<LoginReply> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
        {
            return GetService().LoginAsync(request, cancellationToken);
        }

        public async ValueTask<int> PollAsync(CancellationToken cancellationToken)
        {
            var step = await GetService().IncrStep(new StepRequest(), cancellationToken);
            if (!_host.IsStopping)
                _host.UpdatePlayerStep(step.Step);
            return step.Step;
        }

        public override void OnPlayerNotify(PlayerNotify notify)
        {
            _host.HandleNotification("player", notify.Message);
        }

        private PlayerServiceClient GetService()
        {
            return _service ?? throw new InvalidOperationException("Player service is not attached.");
        }
    }
}
