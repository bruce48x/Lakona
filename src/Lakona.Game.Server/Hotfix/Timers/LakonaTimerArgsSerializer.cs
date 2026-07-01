using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerArgsSerializer
{
    public const string SystemTextJsonSerializerId = "system-text-json-v1";

    private const int MaxDepth = 32;

    public SerializedTimerArgs Serialize<TArgs>(TArgs args)
    {
        var argsType = typeof(TArgs);
        if (argsType.IsGenericType && Nullable.GetUnderlyingType(argsType) is null)
        {
            throw new InvalidOperationException($"Timer args root type '{argsType.FullName}' must not be generic.");
        }

        byte[] payload;
        try
        {
            payload = SerializeObject(args, argsType);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException($"Timer args type '{argsType.FullName}' could not be serialized.", ex);
        }

        object? roundTrip;
        try
        {
            roundTrip = DeserializeObject(payload, argsType);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException($"Timer args type '{argsType.FullName}' failed the JSON round-trip check.", ex);
        }

        if (!RoundTripMatches(args, roundTrip, argsType))
        {
            throw new InvalidOperationException($"Timer args type '{argsType.FullName}' failed the JSON round-trip check.");
        }

        return new SerializedTimerArgs(
            argsType.Assembly.GetName().Name ?? argsType.Assembly.FullName!,
            argsType.FullName ?? argsType.Name,
            SystemTextJsonSerializerId,
            payload);
    }

    public object? Deserialize(string serializerId, ReadOnlyMemory<byte> jsonPayload, Type argsType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serializerId);
        ArgumentNullException.ThrowIfNull(argsType);
        if (!StringComparer.Ordinal.Equals(serializerId, SystemTextJsonSerializerId))
        {
            throw new InvalidOperationException($"Timer args serializer id '{serializerId}' is not supported.");
        }

        try
        {
            return DeserializeObject(jsonPayload.ToArray(), argsType);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException($"Timer args type '{argsType.FullName}' could not be deserialized.", ex);
        }
    }

    private static byte[] SerializeObject(object? value, Type valueType)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            var activeReferences = new HashSet<object>(ReferenceEqualityComparer.Instance);
            WriteValue(writer, value, valueType, depth: 0, activeReferences);
        }

        return stream.ToArray();
    }

    private static object? DeserializeObject(byte[] payload, Type valueType)
    {
        using var document = JsonDocument.Parse(payload);
        return ReadValue(document.RootElement, valueType, depth: 0);
    }

    private static bool RoundTripMatches(object? args, object? roundTrip, Type argsType)
    {
        if (args is null)
        {
            return roundTrip is null;
        }

        if (roundTrip is null)
        {
            return false;
        }

        try
        {
            var original = SerializeObject(args, argsType);
            var copy = SerializeObject(roundTrip, argsType);
            return original.AsSpan().SequenceEqual(copy);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static void WriteValue(
        Utf8JsonWriter writer,
        object? value,
        Type valueType,
        int depth,
        HashSet<object> activeReferences)
    {
        ThrowIfDepthExceeded(valueType, depth);
        var targetType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        ValidateDeclaredShape(targetType);
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (targetType.IsArray)
        {
            WriteArray(writer, (Array)value, targetType.GetElementType()!, depth, activeReferences);
        }
        else if (IsListType(targetType, out var listElementType))
        {
            WriteList(writer, (IEnumerable)value, listElementType, depth, activeReferences);
        }
        else if (targetType == typeof(string))
        {
            writer.WriteStringValue((string)value);
        }
        else if (targetType == typeof(char))
        {
            writer.WriteStringValue(((char)value).ToString());
        }
        else if (targetType == typeof(bool))
        {
            writer.WriteBooleanValue((bool)value);
        }
        else if (targetType == typeof(sbyte))
        {
            writer.WriteNumberValue((sbyte)value);
        }
        else if (targetType == typeof(byte))
        {
            writer.WriteNumberValue((byte)value);
        }
        else if (targetType == typeof(short))
        {
            writer.WriteNumberValue((short)value);
        }
        else if (targetType == typeof(ushort))
        {
            writer.WriteNumberValue((ushort)value);
        }
        else if (targetType == typeof(int))
        {
            writer.WriteNumberValue((int)value);
        }
        else if (targetType == typeof(uint))
        {
            writer.WriteNumberValue((uint)value);
        }
        else if (targetType == typeof(long))
        {
            writer.WriteNumberValue((long)value);
        }
        else if (targetType == typeof(ulong))
        {
            writer.WriteNumberValue((ulong)value);
        }
        else if (targetType == typeof(float))
        {
            writer.WriteNumberValue((float)value);
        }
        else if (targetType == typeof(double))
        {
            writer.WriteNumberValue((double)value);
        }
        else if (targetType == typeof(decimal))
        {
            writer.WriteNumberValue((decimal)value);
        }
        else if (targetType == typeof(Guid))
        {
            writer.WriteStringValue((Guid)value);
        }
        else if (targetType == typeof(DateTime))
        {
            writer.WriteStringValue((DateTime)value);
        }
        else if (targetType == typeof(DateTimeOffset))
        {
            writer.WriteStringValue((DateTimeOffset)value);
        }
        else if (targetType == typeof(TimeSpan))
        {
            writer.WriteStringValue(((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture));
        }
        else if (targetType.IsEnum)
        {
            writer.WriteStringValue(value.ToString());
        }
        else
        {
            WriteObject(writer, value, targetType, depth, activeReferences);
        }
    }

    private static void WriteArray(
        Utf8JsonWriter writer,
        Array values,
        Type elementType,
        int depth,
        HashSet<object> activeReferences)
    {
        EnterReference(values, activeReferences);
        try
        {
            writer.WriteStartArray();
            foreach (var value in values)
            {
                WriteValue(writer, value, elementType, depth + 1, activeReferences);
            }

            writer.WriteEndArray();
        }
        finally
        {
            activeReferences.Remove(values);
        }
    }

    private static void WriteList(
        Utf8JsonWriter writer,
        IEnumerable values,
        Type elementType,
        int depth,
        HashSet<object> activeReferences)
    {
        EnterReference(values, activeReferences);
        try
        {
            writer.WriteStartArray();
            foreach (var value in values)
            {
                WriteValue(writer, value, elementType, depth + 1, activeReferences);
            }

            writer.WriteEndArray();
        }
        finally
        {
            activeReferences.Remove(values);
        }
    }

    private static void WriteObject(
        Utf8JsonWriter writer,
        object value,
        Type valueType,
        int depth,
        HashSet<object> activeReferences)
    {
        EnterReference(value, activeReferences);
        try
        {
            writer.WriteStartObject();
            foreach (var property in GetSerializableProperties(valueType))
            {
                writer.WritePropertyName(property.Name);
                WriteValue(writer, property.GetValue(value), property.PropertyType, depth + 1, activeReferences);
            }

            writer.WriteEndObject();
        }
        finally
        {
            activeReferences.Remove(value);
        }
    }

    private static object? ReadValue(JsonElement element, Type valueType, int depth)
    {
        ThrowIfDepthExceeded(valueType, depth);
        var targetType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        ValidateDeclaredShape(targetType);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return valueType.IsValueType && Nullable.GetUnderlyingType(valueType) is null
                ? throw new JsonException($"Cannot assign null to '{valueType.FullName}'.")
                : null;
        }

        if (targetType.IsArray)
        {
            return ReadArray(element, targetType.GetElementType()!, depth);
        }

        if (IsListType(targetType, out var listElementType))
        {
            return ReadList(element, targetType, listElementType, depth);
        }

        if (targetType == typeof(string))
        {
            return element.GetString();
        }

        if (targetType == typeof(char))
        {
            var value = element.GetString();
            return value is { Length: 1 }
                ? value[0]
                : throw new JsonException($"Expected single-character string for '{valueType.FullName}'.");
        }

        if (targetType == typeof(bool))
        {
            return element.GetBoolean();
        }

        if (targetType == typeof(sbyte))
        {
            return checked((sbyte)element.GetInt32());
        }

        if (targetType == typeof(byte))
        {
            return element.GetByte();
        }

        if (targetType == typeof(short))
        {
            return element.GetInt16();
        }

        if (targetType == typeof(ushort))
        {
            return checked((ushort)element.GetInt32());
        }

        if (targetType == typeof(int))
        {
            return element.GetInt32();
        }

        if (targetType == typeof(uint))
        {
            return element.GetUInt32();
        }

        if (targetType == typeof(long))
        {
            return element.GetInt64();
        }

        if (targetType == typeof(ulong))
        {
            return element.GetUInt64();
        }

        if (targetType == typeof(float))
        {
            return element.GetSingle();
        }

        if (targetType == typeof(double))
        {
            return element.GetDouble();
        }

        if (targetType == typeof(decimal))
        {
            return element.GetDecimal();
        }

        if (targetType == typeof(Guid))
        {
            return element.GetGuid();
        }

        if (targetType == typeof(DateTime))
        {
            return element.GetDateTime();
        }

        if (targetType == typeof(DateTimeOffset))
        {
            return element.GetDateTimeOffset();
        }

        if (targetType == typeof(TimeSpan))
        {
            return TimeSpan.Parse(element.GetString()!, CultureInfo.InvariantCulture);
        }

        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, element.GetString()!, ignoreCase: false);
        }

        return ReadObject(element, targetType, depth);
    }

    private static Array ReadArray(JsonElement element, Type elementType, int depth)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Expected JSON array for '{elementType.FullName}[]'.");
        }

        var values = element.EnumerateArray().ToArray();
        var array = Array.CreateInstance(elementType, values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            array.SetValue(ReadValue(values[index], elementType, depth + 1), index);
        }

        return array;
    }

    private static object ReadList(JsonElement element, Type listType, Type elementType, int depth)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Expected JSON array for '{listType.FullName}'.");
        }

        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ReadValue(item, elementType, depth + 1));
        }

        return list;
    }

    private static object ReadObject(JsonElement element, Type valueType, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Expected JSON object for '{valueType.FullName}'.");
        }

        var properties = GetSerializableProperties(valueType);
        var defaultConstructor = valueType.GetConstructor(Type.EmptyTypes);
        if (defaultConstructor is not null)
        {
            var instance = Activator.CreateInstance(valueType)!;
            foreach (var property in properties.Where(static property => property.SetMethod is not null))
            {
                if (element.TryGetProperty(property.Name, out var propertyElement))
                {
                    property.SetValue(instance, ReadValue(propertyElement, property.PropertyType, depth + 1));
                }
            }

            return instance;
        }

        var constructor = valueType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .OrderByDescending(static candidate => candidate.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Timer args type '{valueType.FullName}' must have a public constructor.");
        var arguments = constructor
            .GetParameters()
            .Select(parameter =>
            {
                var property = properties.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                if (property is null || !element.TryGetProperty(property.Name, out var propertyElement))
                {
                    return parameter.HasDefaultValue
                        ? parameter.DefaultValue
                        : throw new JsonException($"Missing JSON property for constructor parameter '{parameter.Name}'.");
                }

                return ReadValue(propertyElement, parameter.ParameterType, depth + 1);
            })
            .ToArray();
        return constructor.Invoke(arguments);
    }

    private static IReadOnlyList<PropertyInfo> GetSerializableProperties(Type type)
    {
        return type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateDeclaredShape(Type type)
    {
        ValidateDeclaredShape(type, new HashSet<Type>());
    }

    private static void ValidateDeclaredShape(Type type, HashSet<Type> activeTypes)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsSupportedScalarType(type))
        {
            return;
        }

        if (type == typeof(object))
        {
            throw new NotSupportedException("Timer args declared member type 'System.Object' is not supported.");
        }

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            throw new NotSupportedException($"Timer args member type '{type.FullName}' is not supported.");
        }

        if (type.IsInterface)
        {
            throw new NotSupportedException($"Timer args interface member type '{type.FullName}' is not supported.");
        }

        if (type.IsArray)
        {
            ValidateDeclaredShape(type.GetElementType()!, activeTypes);
            return;
        }

        if (IsListType(type, out var elementType))
        {
            ValidateDeclaredShape(elementType, activeTypes);
            return;
        }

        if (type.IsAbstract)
        {
            throw new NotSupportedException($"Timer args abstract member type '{type.FullName}' is not supported.");
        }

        if (!type.IsClass)
        {
            throw new NotSupportedException($"Timer args member type '{type.FullName}' is not supported.");
        }

        if (IsFrameworkReferenceType(type))
        {
            throw new NotSupportedException($"Timer args framework reference type '{type.FullName}' is not supported.");
        }

        if (!activeTypes.Add(type))
        {
            return;
        }

        try
        {
            var publicField = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault();
            if (publicField is not null)
            {
                throw new NotSupportedException($"Timer args type '{type.FullName}' declares public field '{publicField.Name}', which is not supported. Use public properties for timer DTO state.");
            }

            foreach (var property in GetSerializableProperties(type))
            {
                ValidateDeclaredShape(property.PropertyType, activeTypes);
            }
        }
        finally
        {
            activeTypes.Remove(type);
        }
    }

    private static bool IsSupportedScalarType(Type type)
    {
        return type.IsEnum
            || type == typeof(string)
            || type == typeof(char)
            || type == typeof(bool)
            || type == typeof(sbyte)
            || type == typeof(byte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan);
    }

    private static bool IsFrameworkReferenceType(Type type)
    {
        return type.Namespace is not null
            && (string.Equals(type.Namespace, "System", StringComparison.Ordinal)
                || type.Namespace.StartsWith("System.", StringComparison.Ordinal)
                || type.Namespace.StartsWith("Microsoft.", StringComparison.Ordinal));
    }

    private static void ThrowIfDepthExceeded(Type type, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new NotSupportedException($"Timer args type '{type.FullName}' exceeds the maximum supported JSON depth of {MaxDepth}.");
        }
    }

    private static void EnterReference(object value, HashSet<object> activeReferences)
    {
        if (!activeReferences.Add(value))
        {
            throw new NotSupportedException($"Timer args contain a reference cycle at '{value.GetType().FullName}'.");
        }
    }

    private static bool IsListType(Type type, out Type elementType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        elementType = null!;
        return false;
    }
}

internal sealed record SerializedTimerArgs(
    string ArgsAssemblyName,
    string ArgsFullName,
    string SerializerId,
    ReadOnlyMemory<byte> JsonPayload);
