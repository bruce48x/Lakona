namespace Lakona.Game.Server.Actors;

public interface IStartupActorInvoker
{
    ValueTask CallAsync<TActor, TKey, TRequest>(TKey key, string actorName, string methodName, ulong remoteMethodId, TRequest request, Func<ActorId, TRequest, CancellationToken, ValueTask> invokeLocal, CancellationToken cancellationToken = default) where TActor : class, IActor;
    ValueTask<TResult> CallAsync<TActor, TKey, TRequest, TResult>(TKey key, string actorName, string methodName, ulong remoteMethodId, TRequest request, Func<ActorId, TRequest, CancellationToken, ValueTask<TResult>> invokeLocal, CancellationToken cancellationToken = default) where TActor : class, IActor;
    ValueTask PostAsync<TActor, TKey, TRequest>(TKey key, string actorName, string methodName, ulong remoteMethodId, TRequest request, Func<ActorId, TRequest, CancellationToken, ValueTask<ActorTellResult>> invokeLocal, CancellationToken cancellationToken = default) where TActor : class, IActor;
}
