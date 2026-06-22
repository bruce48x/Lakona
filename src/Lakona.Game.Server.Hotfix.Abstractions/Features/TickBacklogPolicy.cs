namespace Lakona.Game.Server.Hotfix.Abstractions;

public enum TickBacklogPolicy
{
    Coalesce = 0,
    SkipIfPending = 1
}
