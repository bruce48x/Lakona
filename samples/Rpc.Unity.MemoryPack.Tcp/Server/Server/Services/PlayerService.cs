using Game.Rpc.Contracts;

namespace Samples.Server.Services;

public class PlayerService: IPlayerService
{
    private readonly IPlayerNotifications _notifications;
    private int _step;

    public PlayerService(IPlayerNotifications notifications)
    {
        _notifications = notifications;
    }

    public ValueTask<LoginReply> LoginAsync(LoginRequest req)
    {
        _notifications.OnPlayerNotify(new PlayerNotify
        {
            Message = $"Welcome {req.Account}, player login accepted."
        });

        // Example: accept any account, return a dummy token.
        // Replace with your own auth logic.
        return new ValueTask<LoginReply>(new LoginReply
        {
            Code = 0,
            Token = $"token-{req.Account}-{Guid.NewGuid():N}"
        });
    }

    public ValueTask<StepReply> IncrStep(StepRequest req)
    {
        _step++;
        _notifications.OnPlayerNotify(new PlayerNotify
        {
            Message = $"Player step => {_step}"
        });
        return new ValueTask<StepReply>(new StepReply
        {
            Step = _step
        });
    }
}
