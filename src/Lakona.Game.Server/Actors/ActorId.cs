namespace Lakona.Game.Server.Actors;

public readonly record struct ActorId
{
    public ActorId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Actor id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }

    public static ActorId From(string value)
    {
        return new ActorId(value);
    }
}
