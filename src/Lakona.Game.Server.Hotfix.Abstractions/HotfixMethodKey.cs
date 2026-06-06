namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class HotfixMethodKey : IEquatable<HotfixMethodKey>
{
    private readonly string[] parameterTypeNames;

    public HotfixMethodKey(
        string StateTypeName,
        string MethodName,
        string ReturnTypeName,
        IReadOnlyList<string> ParameterTypeNames)
    {
        this.StateTypeName = RequireNonWhiteSpace(StateTypeName, nameof(StateTypeName));
        this.MethodName = RequireNonWhiteSpace(MethodName, nameof(MethodName));
        this.ReturnTypeName = RequireNonWhiteSpace(ReturnTypeName, nameof(ReturnTypeName));
        parameterTypeNames = CopyParameterTypeNames(ParameterTypeNames);
        this.ParameterTypeNames = Array.AsReadOnly(parameterTypeNames);
    }

    public string StateTypeName { get; }

    public string MethodName { get; }

    public string ReturnTypeName { get; }

    public IReadOnlyList<string> ParameterTypeNames { get; }

    public static bool operator ==(HotfixMethodKey? left, HotfixMethodKey? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(HotfixMethodKey? left, HotfixMethodKey? right)
    {
        return !Equals(left, right);
    }

    public bool Equals(HotfixMethodKey? other)
    {
        return other is not null
            && StringComparer.Ordinal.Equals(StateTypeName, other.StateTypeName)
            && StringComparer.Ordinal.Equals(MethodName, other.MethodName)
            && StringComparer.Ordinal.Equals(ReturnTypeName, other.ReturnTypeName)
            && parameterTypeNames.SequenceEqual(other.parameterTypeNames, StringComparer.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is HotfixMethodKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StateTypeName, StringComparer.Ordinal);
        hash.Add(MethodName, StringComparer.Ordinal);
        hash.Add(ReturnTypeName, StringComparer.Ordinal);

        foreach (var parameterTypeName in parameterTypeNames)
        {
            hash.Add(parameterTypeName, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        return $"{StateTypeName}.{MethodName}({string.Join(", ", ParameterTypeNames)}) -> {ReturnTypeName}";
    }

    private static string RequireNonWhiteSpace(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be null or whitespace.", parameterName)
            : value;
    }

    private static string[] CopyParameterTypeNames(IReadOnlyList<string> parameterTypeNames)
    {
        ArgumentNullException.ThrowIfNull(parameterTypeNames);

        var copy = new string[parameterTypeNames.Count];
        for (var index = 0; index < parameterTypeNames.Count; index++)
        {
            copy[index] = RequireNonWhiteSpace(parameterTypeNames[index], $"{nameof(ParameterTypeNames)}[{index}]");
        }

        return copy;
    }
}
