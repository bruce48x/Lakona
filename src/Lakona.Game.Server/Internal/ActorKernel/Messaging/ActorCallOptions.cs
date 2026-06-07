namespace Lakona.Game.Server.Internal.ActorKernel;

internal sealed record ActorCallOptions(TimeSpan QueueTimeout, TimeSpan ResponseTimeout);
