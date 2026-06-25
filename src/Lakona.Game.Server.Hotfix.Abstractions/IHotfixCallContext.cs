namespace Lakona.Game.Server.Hotfix.Abstractions;

public interface IHotfixCallContext
{
    IServiceProvider Services { get; }
}
