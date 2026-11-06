#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Rpc.Contracts;
using Client.Generated;

namespace Rpc.Testing
{
    internal sealed class QuestSessionModule : RpcClient.QuestNotificationsBase
    {
        private readonly IConnectionSessionHost _host;
        private QuestServiceClient? _service;

        public QuestSessionModule(IConnectionSessionHost host)
        {
            _host = host;
        }

        public void Attach(QuestServiceClient service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public async ValueTask<int> LoadAsync(CancellationToken cancellationToken)
        {
            var progress = await GetService().GetProgressAsync(new ProgressRequest(), cancellationToken);
            return progress.Progress;
        }

        public async ValueTask<int> PollAsync(CancellationToken cancellationToken)
        {
            var progress = await GetService().IncrProgress(new ProgressRequest(), cancellationToken);
            if (!_host.IsStopping)
                _host.UpdateQuestProgress(progress.Progress);
            return progress.Progress;
        }

        public override void OnQuestNotify(QuestNotify notify)
        {
            _host.HandleNotification("quest", notify.Message);
        }

        private QuestServiceClient GetService()
        {
            return _service ?? throw new InvalidOperationException("Quest service is not attached.");
        }
    }
}
