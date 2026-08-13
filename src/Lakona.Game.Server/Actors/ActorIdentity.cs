using System.Globalization;
using System.Reflection;

namespace Lakona.Game.Server.Actors;

/// <summary>
/// Creates the canonical logical identity shared by every generated actor selector.
/// </summary>
public static class ActorIdentity
{
    internal static string GetKey(ActorId id)
    {
        var separator = id.Value.IndexOf('/');
        if (separator < 0)
        {
            return Uri.UnescapeDataString(id.Value);
        }

        if (separator == 0 || separator == id.Value.Length - 1)
        {
            throw new ArgumentException(
                $"Actor id '{id.Value}' is not a canonical actor identity.",
                nameof(id));
        }

        return Uri.UnescapeDataString(id.Value[(separator + 1)..]);
    }

    /// <summary>
    /// Creates the canonical identity for <typeparamref name="TActor"/> and <paramref name="key"/>.
    /// </summary>
    public static ActorId Create<TActor, TKey>(TKey key)
        where TActor : class, IActor
        where TKey : notnull
    {
        var actorName = ActorNameResolver.Resolve(typeof(TActor));
        if (actorName.Contains('/', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Actor name '{actorName}' cannot contain '/'.");
        }

        return ActorId.From(actorName + "/" + Uri.EscapeDataString(FormatKey(key)));
    }

    private static string FormatKey<TKey>(TKey key)
        where TKey : notnull
    {
        object value = key;
        var type = value.GetType();
        if (type.IsEnum)
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
        }

        return value switch
        {
            string text => text,
            bool boolean => boolean ? "true" : "false",
            char character => character.ToString(),
            byte number => number.ToString(CultureInfo.InvariantCulture),
            sbyte number => number.ToString(CultureInfo.InvariantCulture),
            short number => number.ToString(CultureInfo.InvariantCulture),
            ushort number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            ulong number => number.ToString(CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
            _ => FormatWrapper(value, type)
        };
    }

    private static string FormatWrapper(object wrapper, Type wrapperType)
    {
        var property = wrapperType.GetProperty(
            "Value",
            BindingFlags.Instance | BindingFlags.Public);
        if (property is null || property.GetIndexParameters().Length != 0)
        {
            throw new ArgumentException(
                $"Actor key type '{wrapperType.FullName}' is not a supported scalar or scalar Value wrapper.",
                nameof(wrapper));
        }

        var value = property.GetValue(wrapper)
            ?? throw new ArgumentException("Actor key Value cannot be null.", nameof(wrapper));
        var method = typeof(ActorIdentity)
            .GetMethod(nameof(FormatObject), BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [value])!;
    }

    private static string FormatObject(object value)
    {
        var type = value.GetType();
        if (type.IsEnum)
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
        }

        return value switch
        {
            string text => text,
            bool boolean => boolean ? "true" : "false",
            char character => character.ToString(),
            byte number => number.ToString(CultureInfo.InvariantCulture),
            sbyte number => number.ToString(CultureInfo.InvariantCulture),
            short number => number.ToString(CultureInfo.InvariantCulture),
            ushort number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            ulong number => number.ToString(CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
            _ => throw new ArgumentException(
                $"Actor key Value type '{type.FullName}' is not a supported scalar.",
                nameof(value))
        };
    }
}
