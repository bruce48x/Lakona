using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Lakona.Game.Server.Hotfix.Generators;

internal static class HotfixTimerArgsValidation
{
    internal static void Validate(SourceProductionContext context, Compilation compilation, IMethodSymbol method, ITypeSymbol args)
    {
        var exceededDepth = false;
        var unstable = FindUnstable(args, compilation.Assembly, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), 0, ref exceededDepth);
        if (unstable is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(HotfixGeneratorDiagnostics.TimerArgsMustBeStable,
                method.Parameters[1].Locations.FirstOrDefault(), method.ToDisplayString(), unstable.ToDisplayString()));
            return;
        }
        var reason = exceededDepth
            ? "declared type nesting exceeds 32 levels; simplify the DTO"
            : HasGenericArguments(args) && !IsNullable(args)
            ? "the root must not be generic; wrap collections in a stable named DTO with public properties"
            : FindUnsupported(args, args.ToDisplayString(), new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), 0);
        if (reason is not null)
            context.ReportDiagnostic(Diagnostic.Create(HotfixGeneratorDiagnostics.TimerArgsShape,
                method.Parameters[1].Locations.FirstOrDefault(), method.ToDisplayString(), args.ToDisplayString(), reason));
    }

    private static ITypeSymbol? FindUnstable(ITypeSymbol type, IAssemblySymbol hotfix, HashSet<ITypeSymbol> visited, int depth, ref bool exceededDepth)
    {
        if (!visited.Add(type)) return null;
        if (depth > 32)
        {
            exceededDepth = true;
            return null;
        }
        if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, hotfix)) return type;
        if (type is IArrayTypeSymbol array) return FindUnstable(array.ElementType, hotfix, visited, depth + 1, ref exceededDepth);
        foreach (var argument in TypeArguments(type))
        {
            var found = FindUnstable(argument, hotfix, visited, depth + 1, ref exceededDepth);
            if (found is not null) return found;
        }
        if (IsFramework(type)) return null;
        foreach (var property in Properties(type))
        {
            var found = FindUnstable(property.Type, hotfix, visited, depth + 1, ref exceededDepth);
            if (found is not null) return found;
        }
        return null;
    }

    private static string? FindUnsupported(ITypeSymbol type, string path, HashSet<ITypeSymbol> active, int depth)
    {
        if (IsNullable(type)) type = ((INamedTypeSymbol)type).TypeArguments[0];
        if (IsScalar(type) || active.Contains(type)) return null;
        if (depth > 32) return path + ": declared type nesting exceeds 32 levels; simplify the DTO";
        if (type is IArrayTypeSymbol array)
            return array.Rank != 1 || !array.IsSZArray
                ? path + ": use a single-dimensional zero-based array"
                : FindUnsupported(array.ElementType, path + "[]", active, depth + 1);
        if (type is INamedTypeSymbol list && list.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
            return FindUnsupported(list.TypeArguments[0], path + "[]", active, depth + 1);
        if (type.SpecialType == SpecialType.System_Object || type.TypeKind != TypeKind.Class || type.IsAbstract || IsFramework(type))
            return path + ": '" + type.ToDisplayString() + "' is unsupported; use supported scalars or a concrete stable DTO with public properties";
        active.Add(type);
        try
        {
            for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
                if (current.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => !field.IsStatic && field.DeclaredAccessibility == Accessibility.Public) is { } field)
                    return path + "." + field.Name + ": public fields are unsupported; use a public property";
            foreach (var property in Properties(type))
            {
                var reason = FindUnsupported(property.Type, path + "." + property.Name, active, depth + 1);
                if (reason is not null) return reason;
            }
            return null;
        }
        finally { active.Remove(type); }
    }

    private static IEnumerable<IPropertySymbol> Properties(ITypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>().OrderBy(item => item.Name, StringComparer.Ordinal))
                if (!property.IsStatic && !property.IsIndexer && property.GetMethod is not null &&
                    (property.GetMethod.DeclaredAccessibility == Accessibility.Public || property.SetMethod?.DeclaredAccessibility == Accessibility.Public) &&
                    seen.Add(property.Name + "|" + property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                    yield return property;
    }

    private static IEnumerable<ITypeSymbol> TypeArguments(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.ContainingType)
            foreach (var argument in current.TypeArguments) yield return argument;
    }

    private static bool HasGenericArguments(ITypeSymbol type) => TypeArguments(type).Any();
    private static bool IsNullable(ITypeSymbol type) => type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    private static bool IsFramework(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString();
        return ns is not null && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal));
    }

    private static bool IsScalar(ITypeSymbol type) => type.TypeKind == TypeKind.Enum || type.SpecialType is
        SpecialType.System_String or SpecialType.System_Char or SpecialType.System_Boolean or
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal or SpecialType.System_DateTime ||
        type.ToDisplayString() is "System.Guid" or "System.DateTimeOffset" or "System.TimeSpan";
}
