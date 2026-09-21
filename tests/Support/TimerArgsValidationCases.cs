using Lakona.Game.Server.Actors;

namespace Lakona.Testing.TimerArgs;

// Both compiler diagnostics and real candidate loading exercise this same contract matrix.
public static class TimerArgsValidationCases
{
    public static IEnumerable<object?[]> Cases()
    {
        yield return ["int" + string.Concat(Enumerable.Repeat("[]", 32)), null];
        yield return ["int" + string.Concat(Enumerable.Repeat("[]", 33)), "LAKONA20056"];
        foreach (var type in new[] { "int", "int?", "string", "decimal", "Guid", "DateTime", "DateTimeOffset", "TimeSpan", "StableEnum", "StableEnum?", "int[]", "int[][]", "ValidArgs", "RecursiveArgs", "ExplosiveGetter" })
            yield return [type, null];
        foreach (var type in new[] { "LocalArgs", "LocalArgs?", "LocalArgs[]", "List<LocalArgs>", "List<LocalArgs>[]", "GenericArgs<LocalArgs>[]" })
            yield return [type, "LAKONA20055"];
        foreach (var type in new[] { "List<int>", "GenericArgs<int>", "object", "Action", "IDisposable", "AbstractArgs", "CustomStruct", "int[,]", "ObjectArgs", "InterfaceArgs", "DelegateArgs", "FieldArgs", "InheritedFieldArgs", "HiddenObjectArgs", "DictionaryArgs", "ExpansionArgs" })
            yield return [type, "LAKONA20056"];
    }

    public static string Source(string argsType) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Lakona.Game.Server.Hotfix.Timers;
        using Lakona.Testing.TimerArgs;
        public enum LocalArgs { One }
        [HotfixBehaviorOf(typeof(ValidationActor))]
        public sealed partial class ValidationBehavior
        {
            [ActorTimer]
            private ValueTask Tick(ValidationActor self, TimerTick<{{argsType}}> tick) => default;
        }
        """;
}

public sealed class ValidationActor : Actor<string>;
public enum StableEnum { One }
public sealed class ValidArgs
{
    public List<int> Values { get; set; } = [];
    public int? Optional { get; set; }
    public StableEnum[] Flags { get; set; } = [];
}
public sealed class RecursiveArgs { public RecursiveArgs? Next { get; set; } }
public sealed class ExplosiveGetter { public int Value => throw new InvalidOperationException("Validation must not execute DTO getters."); }
public sealed class GenericArgs<T> { public T? Value { get; set; } }
public abstract class AbstractArgs;
public struct CustomStruct { public int Value { get; set; } }
public sealed class ObjectArgs { public object? Value { get; set; } }
public sealed class InterfaceArgs { public IDisposable? Value { get; set; } }
public sealed class DelegateArgs { public Action? Value { get; set; } }
public class FieldArgs { public int Value; }
public sealed class InheritedFieldArgs : FieldArgs;
public class BaseObjectArgs { public object? Value { get; set; } }
public sealed class HiddenObjectArgs : BaseObjectArgs { public new int Value { get; set; } }
public sealed class DictionaryArgs { public Dictionary<string, int> Values { get; set; } = []; }
public sealed class ExpansionArgs { public Expanding<int>? Value { get; set; } }
public sealed class Expanding<T> { public Expanding<List<T>>? Next { get; set; } }
