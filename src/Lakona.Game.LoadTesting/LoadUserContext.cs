namespace Lakona.Game.LoadTesting;

public sealed class LoadUserContext
{
    internal LoadUserContext(int userIndex, string userName)
    {
        UserIndex = userIndex;
        UserName = userName;
    }

    public int UserIndex { get; }

    public string UserName { get; }

    public ValueTask MeasureAsync(
        string operationName,
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        return action(cancellationToken);
    }
}
