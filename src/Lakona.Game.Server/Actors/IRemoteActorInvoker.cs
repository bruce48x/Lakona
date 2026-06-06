namespace Lakona.Game.Server.Actors;

public interface IRemoteActorInvoker
{
    ValueTask<RemoteActorInvocationResult> AskAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken = default);

    ValueTask<RemoteActorInvocationResult> TellAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken = default);
}
