using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

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
        var actorName = ActorType<TActor>.Name;
        return ActorId.From(actorName + "/" + Uri.EscapeDataString(KeyFormatter<TKey>.Instance.Format(key)));
    }

    internal static ActorId CreateOrUseExact<TActor, TKey>(TKey key)
        where TActor : class, IActor
        where TKey : notnull =>
        key is ActorId exact ? exact : Create<TActor, TKey>(key);

    private static class ActorType<TActor>
        where TActor : class, IActor
    {
        public static readonly string Name = Resolve();

        private static string Resolve()
        {
            var name = ActorNameResolver.Resolve(typeof(TActor));
            if (name.Contains('/', StringComparison.Ordinal))
                throw new InvalidOperationException($"Actor name '{name}' cannot contain '/'.");
            return name;
        }
    }

    private interface IKeyFormatter<T>
    {
        string Format(T value);
    }

    private static class KeyFormatter<T>
        where T : notnull
    {
        public static readonly IKeyFormatter<T> Instance = Create();

        private static IKeyFormatter<T> Create()
        {
            if (ScalarFormatter<T>.IsSupported)
                return new ScalarKeyFormatter<T>();

            var wrapperType = typeof(T);
            var property = wrapperType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            if (property is null || property.GetIndexParameters().Length != 0
                || property.GetMethod is null || !ScalarType.IsSupported(property.PropertyType))
                return new UnsupportedKeyFormatter<T>();

            var formatterType = typeof(WrapperKeyFormatter<,>).MakeGenericType(wrapperType, property.PropertyType);
            return (IKeyFormatter<T>)Activator.CreateInstance(formatterType, property)!;
        }
    }

    private sealed class ScalarKeyFormatter<T> : IKeyFormatter<T>
        where T : notnull
    {
        public string Format(T value) => ScalarFormatter<T>.Format(value);
    }

    private sealed class UnsupportedKeyFormatter<T> : IKeyFormatter<T>
        where T : notnull
    {
        public string Format(T value) => throw new ArgumentException(
            $"Actor key type '{typeof(T).FullName}' is not a supported scalar or scalar Value wrapper.",
            nameof(value));
    }

    private sealed class WrapperKeyFormatter<TWrapper, TValue> : IKeyFormatter<TWrapper>
        where TWrapper : notnull
    {
        private delegate TValue StructGetter(ref TWrapper wrapper);

        private readonly Func<TWrapper, TValue>? referenceGetter;
        private readonly StructGetter? structGetter;

        public WrapperKeyFormatter(PropertyInfo property)
        {
            var getter = property.GetMethod!;
            if (typeof(TWrapper).IsValueType)
                structGetter = getter.CreateDelegate<StructGetter>();
            else
                referenceGetter = getter.CreateDelegate<Func<TWrapper, TValue>>();
        }

        public string Format(TWrapper wrapper)
        {
            var value = structGetter is not null
                ? structGetter(ref wrapper)
                : referenceGetter!(wrapper);
            if (value is null)
                throw new ArgumentException("Actor key Value cannot be null.", nameof(wrapper));
            return ScalarFormatter<TValue>.Format(value);
        }
    }

    private static class ScalarType
    {
        public static bool IsSupported(Type type) =>
            type.IsEnum || type == typeof(string) || type == typeof(bool) || type == typeof(char)
            || type == typeof(byte) || type == typeof(sbyte) || type == typeof(short)
            || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
            || type == typeof(long) || type == typeof(ulong) || type == typeof(Guid);
    }

    private static class ScalarFormatter<T>
    {
        public static readonly bool IsSupported = ScalarType.IsSupported(typeof(T));

        public static string Format(T value)
        {
            if (typeof(T) == typeof(string)) return Unsafe.As<T, string>(ref value);
            if (typeof(T) == typeof(bool)) return Unsafe.As<T, bool>(ref value) ? "true" : "false";
            if (typeof(T) == typeof(char)) return Unsafe.As<T, char>(ref value).ToString();
            if (typeof(T) == typeof(byte)) return Unsafe.As<T, byte>(ref value).ToString(CultureInfo.InvariantCulture);
            if (typeof(T) == typeof(sbyte)) return Unsafe.As<T, sbyte>(ref value).ToString(CultureInfo.InvariantCulture);
            if (typeof(T) == typeof(short)) return Unsafe.As<T, short>(ref value).ToString(CultureInfo.InvariantCulture);
            if (typeof(T) == typeof(ushort)) return Unsafe.As<T, ushort>(ref value).ToString(CultureInfo.InvariantCulture);
            if (typeof(T) == typeof(int)) return Unsafe.As<T, int>(ref value).ToString(CultureInfo.InvariantCulture);
            if (typeof(T) == typeof(uint)) return Unsafe.As<T, uint>(ref value).ToString(CultureInfo.InvariantCulture);
            if (typeof(T) == typeof(long)) return Unsafe.As<T, long>(ref value).ToString(CultureInfo.InvariantCulture);
            if (typeof(T) == typeof(ulong)) return Unsafe.As<T, ulong>(ref value).ToString(CultureInfo.InvariantCulture);
            if (typeof(T) == typeof(Guid)) return Unsafe.As<T, Guid>(ref value).ToString("D", CultureInfo.InvariantCulture);
            if (typeof(T).IsEnum) return FormatEnum(value);
            throw new ArgumentException($"Actor key Value type '{typeof(T).FullName}' is not a supported scalar.", nameof(value));
        }

        private static string FormatEnum(T value)
        {
            var underlying = Enum.GetUnderlyingType(typeof(T));
            if (underlying == typeof(byte)) return Unsafe.As<T, byte>(ref value).ToString(CultureInfo.InvariantCulture);
            if (underlying == typeof(sbyte)) return checked((ulong)Unsafe.As<T, sbyte>(ref value)).ToString(CultureInfo.InvariantCulture);
            if (underlying == typeof(short)) return checked((ulong)Unsafe.As<T, short>(ref value)).ToString(CultureInfo.InvariantCulture);
            if (underlying == typeof(ushort)) return Unsafe.As<T, ushort>(ref value).ToString(CultureInfo.InvariantCulture);
            if (underlying == typeof(int)) return checked((ulong)Unsafe.As<T, int>(ref value)).ToString(CultureInfo.InvariantCulture);
            if (underlying == typeof(uint)) return Unsafe.As<T, uint>(ref value).ToString(CultureInfo.InvariantCulture);
            if (underlying == typeof(long)) return checked((ulong)Unsafe.As<T, long>(ref value)).ToString(CultureInfo.InvariantCulture);
            return Unsafe.As<T, ulong>(ref value).ToString(CultureInfo.InvariantCulture);
        }
    }
}
