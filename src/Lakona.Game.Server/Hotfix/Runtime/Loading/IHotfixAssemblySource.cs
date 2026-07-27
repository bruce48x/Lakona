namespace Lakona.Game.Server.Hotfix.Loading;

public interface IHotfixAssemblySource
{
    ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default);
}
