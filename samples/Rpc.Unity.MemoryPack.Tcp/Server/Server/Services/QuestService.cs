using Game.Rpc.Contracts;

namespace Samples.Server.Services;

public class QuestService : IQuestService
{
    private readonly IQuestNotifications _notifications;
    private int _progress;
    private bool _announced;

    public QuestService(IQuestNotifications notifications)
    {
        _notifications = notifications;
    }

    public ValueTask<ProgressReply> GetProgressAsync(ProgressRequest req)
    {
        if (!_announced)
        {
            _announced = true;
            _notifications.OnQuestNotify(new QuestNotify
            {
                Message = "Quest tracker ready."
            });
        }

        return new ValueTask<ProgressReply>(new ProgressReply
        {
            Progress = _progress
        });
    }

    public ValueTask<ProgressReply> IncrProgress(ProgressRequest req)
    {
        _progress++;
        _notifications.OnQuestNotify(new QuestNotify
        {
            Message = $"Quest progress => {_progress}"
        });
        return new ValueTask<ProgressReply>(new ProgressReply
        {
            Progress = _progress
        });
    }
}
