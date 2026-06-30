using System.Text.Json;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerArgsSerializer
{
    public const string SystemTextJsonSerializerId = "system-text-json";

    public SerializedTimerArgs Serialize<TArgs>(TArgs args)
    {
        var argsType = typeof(TArgs);
        if (argsType.IsGenericType)
        {
            throw new InvalidOperationException($"Timer args root type '{argsType.FullName}' must not be generic.");
        }

        byte[] payload;
        try
        {
            payload = JsonSerializer.SerializeToUtf8Bytes(args, argsType);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Timer args type '{argsType.FullName}' could not be serialized.", ex);
        }

        object? roundTrip;
        try
        {
            roundTrip = JsonSerializer.Deserialize(payload, argsType);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Timer args type '{argsType.FullName}' failed the JSON round-trip check.", ex);
        }

        if (!RoundTripMatches(args, roundTrip))
        {
            throw new InvalidOperationException($"Timer args type '{argsType.FullName}' failed the JSON round-trip check.");
        }

        return new SerializedTimerArgs(
            argsType.Assembly.GetName().Name ?? argsType.Assembly.FullName!,
            argsType.FullName ?? argsType.Name,
            SystemTextJsonSerializerId,
            payload);
    }

    private static bool RoundTripMatches<TArgs>(TArgs args, object? roundTrip)
    {
        if (args is null)
        {
            return roundTrip is null;
        }

        if (roundTrip is not TArgs typed)
        {
            return false;
        }

        if (EqualityComparer<TArgs>.Default.Equals(args, typed))
        {
            return true;
        }

        try
        {
            var original = JsonSerializer.SerializeToUtf8Bytes(args, typeof(TArgs));
            var copy = JsonSerializer.SerializeToUtf8Bytes(typed, typeof(TArgs));
            return original.AsSpan().SequenceEqual(copy);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }
}

internal sealed record SerializedTimerArgs(
    string ArgsAssemblyName,
    string ArgsFullName,
    string SerializerId,
    ReadOnlyMemory<byte> JsonPayload);
