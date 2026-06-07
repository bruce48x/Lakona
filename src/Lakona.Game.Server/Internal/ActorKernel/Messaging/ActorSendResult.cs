namespace Lakona.Game.Server.Internal.ActorKernel;

internal enum ActorSendResult
{
    Accepted = 0,
    MailboxFull = 1,
    ActorUnavailable = 2
}
