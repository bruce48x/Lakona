using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Rpc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RpcContractIdAnalyzer : DiagnosticAnalyzer
{
    public const string InvalidServiceIdDiagnosticId = "ULRPC001";
    public const string InvalidMethodIdDiagnosticId = "ULRPC002";
    public const string InvalidNotificationIdDiagnosticId = "ULRPC003";
    public const string DuplicateServiceIdDiagnosticId = "ULRPC004";
    public const string DuplicateMethodIdDiagnosticId = "ULRPC005";
    public const string DuplicateNotificationIdDiagnosticId = "ULRPC006";

    private static readonly DiagnosticDescriptor InvalidServiceIdRule = new(
        InvalidServiceIdDiagnosticId,
        "RPC service id must be greater than 0",
        "RPC service '{0}' uses invalid ServiceId {1}; [RpcService] id must be greater than 0",
        "Lakona.Rpc.Contracts",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: new[] { "CompilationEnd" });

    private static readonly DiagnosticDescriptor InvalidMethodIdRule = new(
        InvalidMethodIdDiagnosticId,
        "RPC method id must be greater than 0",
        "RPC method '{0}' uses invalid MethodId {1}; [RpcMethod] id must be greater than 0",
        "Lakona.Rpc.Contracts",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidNotificationIdRule = new(
        InvalidNotificationIdDiagnosticId,
        "RPC notification id must be greater than 0",
        "RPC notification method '{0}' uses invalid NotificationId {1}; [RpcNotification] id must be greater than 0",
        "Lakona.Rpc.Contracts",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateServiceIdRule = new(
        DuplicateServiceIdDiagnosticId,
        "RPC service id must be unique",
        "Duplicate ServiceId {0} found on RPC services: {1}",
        "Lakona.Rpc.Contracts",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: new[] { "CompilationEnd" });

    private static readonly DiagnosticDescriptor DuplicateMethodIdRule = new(
        DuplicateMethodIdDiagnosticId,
        "RPC method id must be unique within a service",
        "Duplicate MethodId {0} found in RPC service '{1}': {2}",
        "Lakona.Rpc.Contracts",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateNotificationIdRule = new(
        DuplicateNotificationIdDiagnosticId,
        "RPC notification id must be unique within a notification contract",
        "Duplicate NotificationId {0} found in RPC notification contract '{1}': {2}",
        "Lakona.Rpc.Contracts",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        InvalidServiceIdRule,
        InvalidMethodIdRule,
        InvalidNotificationIdRule,
        DuplicateServiceIdRule,
        DuplicateMethodIdRule,
        DuplicateNotificationIdRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var serviceIds = new Dictionary<int, List<IdOccurrence>>();
            var serviceIdsLock = new object();

            compilationContext.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.TypeKind != TypeKind.Interface)
                    return;

                var serviceAttribute = GetAttribute(type, "RpcServiceAttribute");
                if (serviceAttribute is not null &&
                    TryGetIntId(serviceAttribute, out var serviceId))
                {
                    if (serviceId <= 0)
                    {
                        symbolContext.ReportDiagnostic(Diagnostic.Create(
                            InvalidServiceIdRule,
                            GetAttributeLocation(serviceAttribute) ?? type.Locations.FirstOrDefault(),
                            type.Name,
                            serviceId));
                    }
                    else
                    {
                        lock (serviceIdsLock)
                        {
                            AddOccurrence(serviceIds, serviceId, type.Name, GetAttributeLocation(serviceAttribute) ?? type.Locations.FirstOrDefault());
                        }
                    }

                    AnalyzeMethodIds(symbolContext, type, "RpcMethodAttribute", InvalidMethodIdRule, DuplicateMethodIdRule);
                }

                if (GetAttribute(type, "RpcNotificationContractAttribute") is not null)
                    AnalyzeMethodIds(symbolContext, type, "RpcNotificationAttribute", InvalidNotificationIdRule, DuplicateNotificationIdRule);
            }, SymbolKind.NamedType);

            compilationContext.RegisterCompilationEndAction(compilationEndContext =>
            {
                KeyValuePair<int, List<IdOccurrence>>[] duplicateServiceIds;
                lock (serviceIdsLock)
                {
                    duplicateServiceIds = serviceIds
                        .Where(static pair => pair.Value.Count > 1)
                        .ToArray();
                }

                foreach (var group in duplicateServiceIds)
                {
                    var serviceNames = string.Join(", ", group.Value.Select(static occurrence => occurrence.Name));
                    foreach (var occurrence in group.Value)
                    {
                        compilationEndContext.ReportDiagnostic(Diagnostic.Create(
                            DuplicateServiceIdRule,
                            occurrence.Location,
                            group.Key,
                            serviceNames));
                    }
                }
            });
        });
    }

    private static void AnalyzeMethodIds(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        string attributeName,
        DiagnosticDescriptor invalidRule,
        DiagnosticDescriptor duplicateRule)
    {
        var ids = new Dictionary<int, List<IdOccurrence>>();
        foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
        {
            var attribute = GetAttribute(member, attributeName);
            if (attribute is null || !TryGetIntId(attribute, out var id))
                continue;

            var location = GetAttributeLocation(attribute) ?? member.Locations.FirstOrDefault();
            if (id <= 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(invalidRule, location, member.Name, id));
                continue;
            }

            AddOccurrence(ids, id, member.Name, location);
        }

        foreach (var group in ids.Where(static pair => pair.Value.Count > 1))
        {
            var methodNames = string.Join(", ", group.Value.Select(static occurrence => occurrence.Name));
            foreach (var occurrence in group.Value)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    duplicateRule,
                    occurrence.Location,
                    group.Key,
                    type.Name,
                    methodNames));
            }
        }
    }

    private static AttributeData? GetAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
                continue;

            var shortAttributeName = attributeName.EndsWith("Attribute", StringComparison.Ordinal)
                ? attributeName.Substring(0, attributeName.Length - "Attribute".Length)
                : attributeName;
            if (string.Equals(attributeClass.Name, attributeName, StringComparison.Ordinal) ||
                string.Equals(attributeClass.Name, shortAttributeName, StringComparison.Ordinal))
                return attribute;
        }

        return null;
    }

    private static bool TryGetIntId(AttributeData attribute, out int id)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Value is int intValue)
            {
                id = intValue;
                return true;
            }
        }

        id = default;
        return false;
    }

    private static Location? GetAttributeLocation(AttributeData attribute) =>
        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

    private static void AddOccurrence(
        Dictionary<int, List<IdOccurrence>> ids,
        int id,
        string name,
        Location? location)
    {
        if (!ids.TryGetValue(id, out var occurrences))
        {
            occurrences = new List<IdOccurrence>();
            ids.Add(id, occurrences);
        }

        occurrences.Add(new IdOccurrence(name, location));
    }

    private readonly struct IdOccurrence(string name, Location? location)
    {
        public string Name { get; } = name;
        public Location? Location { get; } = location;
    }
}
