using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Server.Hotfix.Abstractions.Actors;

public delegate ValueTask HotfixActorPost<in TActor, in TRequest>(
    TActor self,
    TRequest request,
    CancellationToken cancellationToken);

public delegate ValueTask HotfixActorPostNoCancellation<in TActor, in TRequest>(
    TActor self,
    TRequest request);

public delegate ValueTask<TResult> HotfixActorCall<in TActor, in TRequest, TResult>(
    TActor self,
    TRequest request,
    CancellationToken cancellationToken);

public delegate ValueTask<TResult> HotfixActorCallNoCancellation<in TActor, in TRequest, TResult>(
    TActor self,
    TRequest request);
