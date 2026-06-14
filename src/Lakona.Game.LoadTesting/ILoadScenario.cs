namespace Lakona.Game.LoadTesting;

public interface ILoadScenario
{
    string Name { get; }

    ValueTask RunUserAsync(
        LoadUserContext context,
        CancellationToken cancellationToken);
}
