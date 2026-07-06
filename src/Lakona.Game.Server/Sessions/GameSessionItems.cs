namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionItems
{
    private readonly IReadOnlyDictionary<string, GameSessionItemValue> _items;

    public static GameSessionItems Empty { get; } = new(new Dictionary<string, GameSessionItemValue>(StringComparer.Ordinal));

    internal GameSessionItems(IReadOnlyDictionary<string, GameSessionItemValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = new Dictionary<string, GameSessionItemValue>(items, StringComparer.Ordinal);
    }

    public int Count => _items.Count;

    public bool TryGetValue(string key, out GameSessionItemValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _items.TryGetValue(key, out value);
    }

    public GameSessionItemValue? GetValueOrDefault(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _items.TryGetValue(key, out var value) ? value : null;
    }

    public string? GetString(string key)
    {
        return GetValueOrDefault(key)?.TryGetString();
    }

    public long? GetInt64(string key)
    {
        return GetValueOrDefault(key)?.TryGetInt64();
    }

    public bool? GetBoolean(string key)
    {
        return GetValueOrDefault(key)?.TryGetBoolean();
    }
}
