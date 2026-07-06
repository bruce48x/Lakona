namespace Lakona.Game.Server.Sessions;

public readonly struct GameSessionItemValue : IEquatable<GameSessionItemValue>
{
    private readonly string? _stringValue;
    private readonly long _int64Value;
    private readonly bool _booleanValue;

    private GameSessionItemValue(GameSessionItemKind kind, string? stringValue, long int64Value, bool booleanValue)
    {
        Kind = kind;
        _stringValue = stringValue;
        _int64Value = int64Value;
        _booleanValue = booleanValue;
    }

    public GameSessionItemKind Kind { get; }

    public bool IsDefined => Kind is GameSessionItemKind.String or GameSessionItemKind.Int64 or GameSessionItemKind.Boolean;

    public static GameSessionItemValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new GameSessionItemValue(GameSessionItemKind.String, value, 0, false);
    }

    public static GameSessionItemValue FromInt64(long value)
    {
        return new GameSessionItemValue(GameSessionItemKind.Int64, null, value, false);
    }

    public static GameSessionItemValue FromBoolean(bool value)
    {
        return new GameSessionItemValue(GameSessionItemKind.Boolean, null, 0, value);
    }

    public string? TryGetString()
    {
        return Kind == GameSessionItemKind.String ? _stringValue : null;
    }

    public string GetString()
    {
        return Kind == GameSessionItemKind.String
            ? _stringValue!
            : throw new InvalidOperationException($"Session item kind is {Kind}, not {GameSessionItemKind.String}.");
    }

    public long? TryGetInt64()
    {
        return Kind == GameSessionItemKind.Int64 ? _int64Value : null;
    }

    public long GetInt64()
    {
        return Kind == GameSessionItemKind.Int64
            ? _int64Value
            : throw new InvalidOperationException($"Session item kind is {Kind}, not {GameSessionItemKind.Int64}.");
    }

    public bool? TryGetBoolean()
    {
        return Kind == GameSessionItemKind.Boolean ? _booleanValue : null;
    }

    public bool GetBoolean()
    {
        return Kind == GameSessionItemKind.Boolean
            ? _booleanValue
            : throw new InvalidOperationException($"Session item kind is {Kind}, not {GameSessionItemKind.Boolean}.");
    }

    public bool Equals(GameSessionItemValue other)
    {
        return Kind == other.Kind &&
            string.Equals(_stringValue, other._stringValue, StringComparison.Ordinal) &&
            _int64Value == other._int64Value &&
            _booleanValue == other._booleanValue;
    }

    public override bool Equals(object? obj)
    {
        return obj is GameSessionItemValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, _stringValue, _int64Value, _booleanValue);
    }

    public override string ToString()
    {
        return Kind switch
        {
            GameSessionItemKind.String => _stringValue ?? string.Empty,
            GameSessionItemKind.Int64 => _int64Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            GameSessionItemKind.Boolean => _booleanValue ? "true" : "false",
            _ => string.Empty
        };
    }
}
