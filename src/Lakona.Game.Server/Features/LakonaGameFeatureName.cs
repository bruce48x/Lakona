namespace Lakona.Game.Server.Features;

public static class LakonaGameFeatureName
{
    public static string FromType(Type featureType)
    {
        ArgumentNullException.ThrowIfNull(featureType);

        const string suffix = "Feature";
        if (!featureType.Name.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Feature type name must end with 'Feature'.", nameof(featureType));
        }

        var source = featureType.Name[..^suffix.Length];
        if (source.Length == 0)
        {
            throw new ArgumentException("Feature name is required.", nameof(featureType));
        }

        return ToKebabCase(source);
    }

    private static string ToKebabCase(string value)
    {
        var result = new List<char>(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current))
            {
                var hasPrevious = i > 0;
                var nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                var previousIsLowerOrDigit = hasPrevious && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]));
                var previousIsUpper = hasPrevious && char.IsUpper(value[i - 1]);

                if (result.Count > 0 && (previousIsLowerOrDigit || previousIsUpper && nextIsLower))
                {
                    result.Add('-');
                }

                result.Add(char.ToLowerInvariant(current));
                continue;
            }

            result.Add(current);
        }

        return new string(result.ToArray());
    }
}
