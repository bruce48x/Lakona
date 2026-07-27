namespace Lakona.Game.Server.Hotfix;

public interface IHotfixServiceProviderAccessor
{
    IServiceProvider Current { get; }
}
