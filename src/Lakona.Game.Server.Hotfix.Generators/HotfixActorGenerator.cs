using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Lakona.Game.Server.Hotfix.Generators.GeneratorSymbolFacts;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class HotfixActorGenerator
    {
        private const string HotfixBehaviorOfAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixBehaviorOfAttribute";
        private const string ActorStartAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.ActorStartAttribute";
        private const string ActorStopAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.ActorStopAttribute";
        private const string ActorHostBuilderName = "Lakona.Game.Server.Hotfix.Abstractions.ActorHostBuilder";

        private static readonly DiagnosticDescriptor UnsupportedHotfixBehaviorWrapperTarget = new DiagnosticDescriptor(
            "LKNHOTFIX021",
            "Hotfix behavior cannot receive generated actor ref wrappers",
            "Hotfix behavior '{0}' cannot receive generated actor entries because generation requires a non-file-local, non-generic, top-level sealed partial class",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        internal static void Register(IncrementalGeneratorInitializationContext context)
        {
            var contracts = context.CompilationProvider
                .Select(static (compilation, cancellationToken) =>
                    new HotfixActorGenerationInput(
                        compilation.Assembly.Identity.Name,
                        DiscoverHotfixBehaviors(compilation, cancellationToken).ToArray(),
                        DiscoverStartupRegistrations(compilation, cancellationToken).ToArray()));

            context.RegisterSourceOutput(contracts, GenerateActorContracts);
        }

        private static void GenerateActorContracts(SourceProductionContext context, HotfixActorGenerationInput input)
        {
            if (input.Behaviors.Length == 0)
            {
                return;
            }

            var supported = new List<HotfixActorApiInfo>();

            foreach (var actorGroup in input.Behaviors
                .GroupBy(static behavior => behavior.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), System.StringComparer.Ordinal))
            {
                var behaviors = actorGroup.ToArray();
                if (behaviors.Length > 1)
                {
                    foreach (var duplicate in behaviors.Skip(1))
                    {
                        var location = duplicate.Behavior.Locations.FirstOrDefault(static item => item.IsInSource);
                        context.ReportDiagnostic(Diagnostic.Create(
                            HotfixGeneratorDiagnostics.DuplicateHotfixBehaviorForActor,
                            location,
                            duplicate.Actor.ToDisplayString()));
                    }

                    continue;
                }

                var behavior = behaviors[0];
                if (IsUnsupportedBehaviorWrapperTarget(behavior))
                {
                    var location = behavior.Behavior.Locations.FirstOrDefault(static item => item.IsInSource);
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedHotfixBehaviorWrapperTarget,
                        location,
                        behavior.Behavior.ToDisplayString()));
                    continue;
                }

                var startupKeyType = input.StartupRegistrations
                    .Where(registration => SymbolEqualityComparer.Default.Equals(registration.Actor, behavior.Actor))
                    .Select(static registration => registration.KeyType)
                    .FirstOrDefault();
                var contract = CreateBehaviorActorContract(behavior, input.AssemblyName, startupKeyType);
                foreach (var diagnostic in contract.Diagnostics)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        diagnostic.Descriptor,
                        diagnostic.Location,
                        diagnostic.Arguments));
                }

                if (contract.IsSupported)
                {
                    supported.Add(contract);
                }
            }

            var supportedArray = supported
                .OrderBy(static contract => contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .ToArray();

            if (supportedArray.Length == 0)
            {
                return;
            }

            context.AddSource(
                "GeneratedHotfixActorRefs.g.cs",
                SourceText.From(
                    GenerateActorContractsSource(supportedArray),
                    Encoding.UTF8));
        }

        private static bool IsUnsupportedBehaviorWrapperTarget(HotfixBehaviorInfo behavior)
        {
            return IsFileLocalBehavior(behavior) ||
                behavior.ContainingTypes.Length > 0 ||
                behavior.Behavior.TypeKind != TypeKind.Class ||
                behavior.Behavior.IsStatic ||
                !behavior.Behavior.IsSealed ||
                behavior.Behavior.TypeParameters.Length > 0 ||
                !IsPartial(behavior.Declaration);
        }

        private static bool IsFileLocalBehavior(HotfixBehaviorInfo behavior)
        {
            return HasFileModifier(behavior.Declaration) ||
                behavior.ContainingTypes.Any(static containingType => HasFileModifier(containingType.Declaration));
        }

        private static string GenerateActorContractsSource(HotfixActorApiInfo[] contracts)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine();

            foreach (var contract in contracts)
            {
                AppendActorApiMetadata(builder, contract);
                builder.AppendLine();
            }

            ActorAccessEmitter.Append(builder, contracts);
            builder.AppendLine();
            ActorBehaviorSelectorEmitter.Append(builder, contracts);
            builder.AppendLine();
            AppendActorRegistration(builder, contracts);
            return builder.ToString();
        }

        private static void AppendActorApiMetadata(StringBuilder builder, HotfixActorApiInfo contract)
        {
            var namespaceName = contract.Actor.ContainingNamespace.IsGlobalNamespace
                ? null
                : contract.Actor.ContainingNamespace.ToDisplayString();

            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine();
                builder.AppendLine("{");
            }

            AppendHotfixActorApiMetadata(builder, contract);

            if (namespaceName != null)
            {
                builder.AppendLine("}");
            }
        }

        private static void AppendHotfixActorApiMetadata(StringBuilder builder, HotfixActorApiInfo contract)
        {
            builder.Append("internal static class ").Append(contract.Actor.Name).AppendLine("ApiMetadata");
            builder.AppendLine("{");
            builder.AppendLine("    public const string MethodKeyMetadataName = \"lakona-game.actor-api.method-key\";");
            builder.AppendLine("    public const string MethodIdMetadataName = \"lakona-game.actor-api.method-id\";");
            builder.AppendLine("    public const string ActorTypeMetadataName = \"lakona-game.actor-api.actor-type\";");
            builder.AppendLine("    public const string MethodMetadataName = \"lakona-game.actor-api.method\";");
            builder.AppendLine("    public const string RequestTypeMetadataName = \"lakona-game.actor-api.request-type\";");
            builder.AppendLine("    public const string ResultTypeMetadataName = \"lakona-game.actor-api.result-type\";");
            foreach (var method in contract.Methods)
            {
                builder.Append("    public const string ")
                    .Append(CreateActorApiMethodKeyConstantName(method))
                    .Append(" = \"")
                    .Append(EscapeStringLiteral(method.MethodKey))
                    .AppendLine("\";");
                builder.Append("    public const ulong ")
                    .Append(CreateActorApiMethodIdConstantName(method))
                    .Append(" = ")
                    .Append(GetRemoteMethodId(method))
                    .AppendLine("UL;");
            }

            builder.AppendLine("}");
        }

        private static void AppendActorRegistration(StringBuilder builder, HotfixActorApiInfo[] contracts)
        {
            builder.AppendLine("public sealed class GeneratedHotfixActorRegistration :");
            builder.AppendLine("    global::Lakona.Game.Server.Hotfix.IHotfixGeneratedServiceRegistration");
            builder.AppendLine("{");
            builder.AppendLine("    public void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            builder.AppendLine("    {");
            builder.AppendLine("        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<global::Lakona.Game.Server.Hotfix.ActorAccess>(services);");

            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static IEnumerable<StartupRegistrationInfo> DiscoverStartupRegistrations(
            Compilation compilation,
            CancellationToken cancellationToken)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var invocation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
                        method.Name != "RegisterStartup" ||
                        method.TypeArguments.Length != 2 ||
                        method.ContainingType.ToDisplayString() != ActorHostBuilderName)
                    {
                        continue;
                    }

                    yield return new StartupRegistrationInfo(
                        (INamedTypeSymbol)method.TypeArguments[0],
                        method.TypeArguments[1]);
                }
            }
        }

        private static IEnumerable<HotfixBehaviorInfo> DiscoverHotfixBehaviors(Compilation compilation, CancellationToken cancellationToken)
        {
            foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryCreateHotfixBehavior(type, out var behavior))
                {
                    yield return behavior!;
                }
            }
        }

        private static bool TryCreateHotfixBehavior(INamedTypeSymbol type, out HotfixBehaviorInfo? info)
        {
            info = null;
            var attribute = type.GetAttributes()
                .FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == HotfixBehaviorOfAttributeName);
            if (attribute is null ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol actor)
            {
                return false;
            }

            var declaration = type.DeclaringSyntaxReferences
                .Select(static reference => reference.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault();
            if (declaration is null)
            {
                return false;
            }

            info = new HotfixBehaviorInfo(type, actor, declaration, CreateContainingTypes(type, declaration));
            return true;
        }

        private static HotfixActorApiInfo CreateBehaviorActorContract(
            HotfixBehaviorInfo behavior,
            string hotfixAssemblyName,
            ITypeSymbol? startupKeyType)
        {
            var diagnostics = new List<HotfixGeneratorDiagnosticInfo>();
            var keyType = GetActorKeyType(behavior.Actor);
            if (keyType == null)
            {
                diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                    HotfixGeneratorDiagnostics.HotfixBehaviorTargetMustDeriveActor,
                    behavior.Behavior.Locations.FirstOrDefault(),
                    behavior.Behavior.ToDisplayString(),
                    behavior.Actor.ToDisplayString()));
            }

            var methods = new List<HotfixActorMethodInfo>();
            foreach (var method in behavior.Behavior.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary ||
                    method.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (HasAttribute(method, ActorStartAttributeName) ||
                    HasAttribute(method, ActorStopAttributeName))
                {
                    continue;
                }

                if (!TryCreateBehaviorActorMethod(behavior, method, hotfixAssemblyName, diagnostics, out var methodInfo))
                {
                    continue;
                }

                methods.Add(methodInfo!);
            }

            foreach (var duplicate in methods
                .GroupBy(static method => method.MethodKey)
                .Where(static group => group.Count() > 1))
            {
                foreach (var duplicateMethod in duplicate.Skip(1))
                {
                    diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                        HotfixGeneratorDiagnostics.DuplicateHotfixBehaviorActorApiMethodKey,
                        duplicateMethod.Location,
                        duplicateMethod.Name,
                        duplicate.Key));
                }
            }

            foreach (var duplicate in methods
                .SelectMany(static method => EnumerateGeneratedActorWrapperSignatures(method)
                    .Select(signature => new
                    {
                        Method = method,
                        Signature = signature
                    }))
                .GroupBy(static item => item.Signature)
                .Where(static group => group.Count() > 1))
            {
                foreach (var duplicateMethod in duplicate.Skip(1))
                {
                    diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                        HotfixGeneratorDiagnostics.DuplicateHotfixBehaviorActorApiGeneratedSignature,
                        duplicateMethod.Method.Location,
                        duplicateMethod.Method.Name,
                        duplicate.Key));
                }
            }

            var resolvedKeyType = keyType ?? behavior.Actor;
            var apiAccessibility = IsPubliclyExposable(behavior.Actor) && IsPubliclyExposable(resolvedKeyType)
                ? "public"
                : "internal";

            return new HotfixActorApiInfo(
                behavior.Behavior,
                behavior.Actor,
                resolvedKeyType,
                startupKeyType,
                apiAccessibility,
                diagnostics.Count == 0 ? methods.ToArray() : Array.Empty<HotfixActorMethodInfo>(),
                diagnostics.ToArray());
        }

        private static bool IsPubliclyExposable(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol arrayType)
            {
                return IsPubliclyExposable(arrayType.ElementType);
            }

            if (type is INamedTypeSymbol namedType)
            {
                if (namedType.DeclaredAccessibility != Accessibility.Public)
                {
                    return false;
                }

                if (namedType.ContainingType is not null && !IsPubliclyExposable(namedType.ContainingType))
                {
                    return false;
                }

                foreach (var typeArgument in namedType.TypeArguments)
                {
                    if (!IsPubliclyExposable(typeArgument))
                    {
                        return false;
                    }
                }

                return true;
            }

            return type.DeclaredAccessibility == Accessibility.Public;
        }

        private static bool TryCreateBehaviorActorMethod(
            HotfixBehaviorInfo behavior,
            IMethodSymbol method,
            string hotfixAssemblyName,
            List<HotfixGeneratorDiagnosticInfo> diagnostics,
            out HotfixActorMethodInfo? methodInfo)
        {
            methodInfo = null;
            var location = method.Locations.FirstOrDefault() ?? behavior.Behavior.Locations.FirstOrDefault();
            if (!IsSupportedBehaviorActorMethodShape(behavior.Actor, method, out var resultType))
            {
                diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                    HotfixGeneratorDiagnostics.HotfixBehaviorActorApiMethodShape,
                    location,
                    method.Name,
                    behavior.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return false;
            }

            var requestType = method.Parameters[1].Type;
            if (ContainsTypeFromAssembly(requestType, hotfixAssemblyName))
            {
                diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                    HotfixGeneratorDiagnostics.HotfixBehaviorActorApiTypeBoundary,
                    location,
                    method.Name,
                    "request",
                    requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return false;
            }

            if (resultType != null && ContainsTypeFromAssembly(resultType, hotfixAssemblyName))
            {
                diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                    HotfixGeneratorDiagnostics.HotfixBehaviorActorApiTypeBoundary,
                    location,
                    method.Name,
                    "return result",
                    resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return false;
            }

            var methodKey = CreateBehaviorActorMethodKey(behavior.Actor, method.Name, requestType, resultType);
            methodInfo = HotfixActorMethodInfo.Create(
                method,
                requestType,
                resultType,
                method.Parameters.Length == 3,
                methodKey);
            return true;
        }

        private static bool IsSupportedBehaviorActorMethodShape(
            INamedTypeSymbol actor,
            IMethodSymbol method,
            out ITypeSymbol? resultType)
        {
            resultType = null;
            if (method.TypeParameters.Length > 0 ||
                method.IsStatic ||
                method.IsExtensionMethod ||
                !IsValueTask(method.ReturnType, out resultType) ||
                (resultType != null && (resultType.IsRefLikeType || ContainsTypeParameter(resultType))))
            {
                return false;
            }

            if (method.Parameters.Length != 2 && method.Parameters.Length != 3)
            {
                return false;
            }

            var receiver = method.Parameters[0];
            if (receiver.RefKind != RefKind.None ||
                !SymbolEqualityComparer.Default.Equals(receiver.Type, actor))
            {
                return false;
            }

            if (!IsSupportedRequestParameter(method.Parameters[1]))
            {
                return false;
            }

            return method.Parameters.Length == 2 ||
                (method.Parameters[2].RefKind == RefKind.None && IsCancellationToken(method.Parameters[2].Type));
        }

        private static ITypeSymbol? GetActorKeyType(INamedTypeSymbol symbol)
        {
            for (var current = symbol.BaseType; current != null; current = current.BaseType)
            {
                if (current.Arity == 1 &&
                    current.Name == "Actor" &&
                    current.ContainingNamespace.ToDisplayString() == "Lakona.Game.Server.Actors")
                {
                    return current.TypeArguments[0];
                }
            }

            return null;
        }

        private static bool IsSupportedRequestParameter(IParameterSymbol parameter)
        {
            return parameter.RefKind == RefKind.None &&
                parameter.Type.TypeKind != TypeKind.Pointer &&
                !parameter.Type.IsRefLikeType &&
                !ContainsTypeParameter(parameter.Type) &&
                !IsCancellationToken(parameter.Type);
        }

        private static bool ContainsTypeParameter(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.TypeParameter)
            {
                return true;
            }

            if (type is IArrayTypeSymbol arrayType)
            {
                return ContainsTypeParameter(arrayType.ElementType);
            }

            if (type is IPointerTypeSymbol pointerType)
            {
                return ContainsTypeParameter(pointerType.PointedAtType);
            }

            return type is INamedTypeSymbol namedType &&
                namedType.TypeArguments.Any(ContainsTypeParameter);
        }

        private static bool ContainsTypeFromAssembly(ITypeSymbol type, string assemblyName)
        {
            if (string.Equals(type.ContainingAssembly?.Identity.Name, assemblyName, System.StringComparison.Ordinal))
            {
                return true;
            }

            if (type is IArrayTypeSymbol arrayType)
            {
                return ContainsTypeFromAssembly(arrayType.ElementType, assemblyName);
            }

            if (type is IPointerTypeSymbol pointerType)
            {
                return ContainsTypeFromAssembly(pointerType.PointedAtType, assemblyName);
            }

            return type is INamedTypeSymbol namedType &&
                namedType.TypeArguments.Any(argument => ContainsTypeFromAssembly(argument, assemblyName));
        }

        private static string CreateBehaviorActorMethodKey(
            INamedTypeSymbol actorType,
            string methodName,
            ITypeSymbol requestType,
            ITypeSymbol? resultType)
        {
            return "actor:" +
                GetRuntimeTypeIdentity(actorType) +
                "|method:" +
                methodName +
                "|request:" +
                GetRuntimeTypeIdentity(requestType) +
                "|result:" +
                (resultType == null ? "void" : GetRuntimeTypeIdentity(resultType));
        }

        private static bool IsValueTask(ITypeSymbol type, out ITypeSymbol? resultType)
        {
            resultType = null;
            if (type is INamedTypeSymbol namedType &&
                namedType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
                namedType.Name == "ValueTask")
            {
                if (namedType.Arity == 0)
                {
                    return true;
                }

                if (namedType.Arity == 1)
                {
                    resultType = namedType.TypeArguments[0];
                    return true;
                }
            }

            return false;
        }

        private static bool IsCancellationToken(ITypeSymbol type)
        {
            return type.Name == "CancellationToken" &&
                type.ContainingNamespace.ToDisplayString() == "System.Threading";
        }

        private static string SanitizeIdentifierPart(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in value)
            {
                if (builder.Length == 0)
                {
                    builder.Append(SyntaxFacts.IsIdentifierStartCharacter(character) ? character : '_');
                    continue;
                }

                builder.Append(SyntaxFacts.IsIdentifierPartCharacter(character) ? character : '_');
            }

            return builder.Length == 0 ? "field" : builder.ToString();
        }

        internal static string DisplayReturnType(HotfixActorMethodInfo method)
        {
            if (method.ResultType == null)
            {
                return "global::System.Threading.Tasks.ValueTask";
            }

            return "global::System.Threading.Tasks.ValueTask<" +
                method.ResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ">";
        }

        internal static ulong GetRemoteMethodId(HotfixActorMethodInfo method)
        {
            return CreateMethodId(method.MethodKey);
        }

        private static string GetActorPrefix(string actorName)
        {
            return actorName.EndsWith("Actor", System.StringComparison.Ordinal) && actorName.Length > "Actor".Length
                ? actorName.Substring(0, actorName.Length - "Actor".Length)
                : actorName;
        }

        internal static string ResolveActorName(INamedTypeSymbol actor)
        {
            foreach (var attribute in actor.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() ==
                        "Lakona.Game.Server.Actors.ActorNameAttribute" &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is string explicitName &&
                    !string.IsNullOrWhiteSpace(explicitName))
                {
                    return explicitName;
                }
            }

            return LowerFirst(GetActorPrefix(actor.Name));
        }

        internal static string CreateActorApiMethodKeyConstantName(HotfixActorMethodInfo method)
        {
            var builder = new StringBuilder();
            builder.Append(SanitizeIdentifierPart(method.Name));
            builder.Append('_');
            builder.Append(SanitizeIdentifierPart(GetRuntimeTypeFullName(method.RequestType)));
            builder.Append("_MethodKey");
            return builder.ToString();
        }

        internal static string CreateActorApiMethodIdConstantName(HotfixActorMethodInfo method)
        {
            var builder = new StringBuilder();
            builder.Append(SanitizeIdentifierPart(method.Name));
            builder.Append('_');
            builder.Append(SanitizeIdentifierPart(GetRuntimeTypeFullName(method.RequestType)));
            builder.Append("_MethodId");
            return builder.ToString();
        }

        private static ulong CreateMethodId(string methodKey)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            unchecked
            {
                var hash = offsetBasis;
                foreach (var value in Encoding.UTF8.GetBytes(methodKey))
                {
                    hash ^= value;
                    hash *= prime;
                }

                return hash;
            }
        }

        private static IEnumerable<string> EnumerateGeneratedActorWrapperSignatures(HotfixActorMethodInfo method)
        {
            yield return CreateGeneratedActorWrapperSignature(method.Name, method.RequestType);
            if (method.ResultType == null)
            {
                yield return CreateGeneratedActorWrapperSignature("Try" + method.Name, method.RequestType);
            }
        }

        private static string CreateGeneratedActorWrapperSignature(string methodName, ITypeSymbol requestType)
        {
            return methodName + "(" + GetRuntimeTypeIdentity(requestType) + ")";
        }

        internal static string GetAccessibility(INamedTypeSymbol symbol)
        {
            switch (symbol.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return "public";
                case Accessibility.Internal:
                    return "internal";
                case Accessibility.Private:
                    return "private";
                case Accessibility.Protected:
                    return "protected";
                case Accessibility.ProtectedOrInternal:
                    return "protected internal";
                case Accessibility.ProtectedAndInternal:
                    return "private protected";
                default:
                    return "internal";
            }
        }

        internal static string LowerFirst(string value)
        {
            if (value.Length == 0)
            {
                return value;
            }

            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        internal static string Indent(int indentLevel)
        {
            return new string(' ', indentLevel * 4);
        }

        private static ContainingTypeInfo[] CreateContainingTypes(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration)
        {
            var containingDeclarations = new List<TypeDeclarationSyntax>();
            for (var current = declaration.Parent; current != null; current = current.Parent)
            {
                if (current is TypeDeclarationSyntax containingDeclaration)
                {
                    containingDeclarations.Add(containingDeclaration);
                }
            }

            containingDeclarations.Reverse();

            var containingSymbols = new List<INamedTypeSymbol>();
            for (var current = symbol.ContainingType; current != null; current = current.ContainingType)
            {
                containingSymbols.Add(current);
            }

            containingSymbols.Reverse();

            return containingDeclarations
                .Zip(containingSymbols, (typeDeclaration, typeSymbol) => new ContainingTypeInfo(typeSymbol, typeDeclaration))
                .ToArray();
        }

        internal sealed class HotfixBehaviorInfo
        {
            public HotfixBehaviorInfo(INamedTypeSymbol behavior, INamedTypeSymbol actor, TypeDeclarationSyntax declaration, ContainingTypeInfo[] containingTypes)
            {
                Behavior = behavior;
                Actor = actor;
                Declaration = declaration;
                ContainingTypes = containingTypes;
            }

            public INamedTypeSymbol Behavior { get; }

            public INamedTypeSymbol Actor { get; }

            public TypeDeclarationSyntax Declaration { get; }

            public ContainingTypeInfo[] ContainingTypes { get; }
        }

        internal sealed class ContainingTypeInfo
        {
            public ContainingTypeInfo(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration)
            {
                Symbol = symbol;
                Declaration = declaration;
            }

            public INamedTypeSymbol Symbol { get; }

            public TypeDeclarationSyntax Declaration { get; }
        }

        internal sealed class HotfixActorGenerationInput
        {
            public HotfixActorGenerationInput(
                string assemblyName,
                HotfixBehaviorInfo[] behaviors,
                StartupRegistrationInfo[] startupRegistrations)
            {
                AssemblyName = assemblyName;
                Behaviors = behaviors;
                StartupRegistrations = startupRegistrations;
            }

            public string AssemblyName { get; }

            public HotfixBehaviorInfo[] Behaviors { get; }

            public StartupRegistrationInfo[] StartupRegistrations { get; }
        }

        internal sealed class StartupRegistrationInfo
        {
            public StartupRegistrationInfo(INamedTypeSymbol actor, ITypeSymbol keyType)
            {
                Actor = actor;
                KeyType = keyType;
            }

            public INamedTypeSymbol Actor { get; }

            public ITypeSymbol KeyType { get; }
        }

        internal sealed class HotfixActorApiInfo
        {
            public HotfixActorApiInfo(
                INamedTypeSymbol behavior,
                INamedTypeSymbol actor,
                ITypeSymbol keyType,
                ITypeSymbol? startupKeyType,
                string apiAccessibility,
                HotfixActorMethodInfo[] methods,
                HotfixGeneratorDiagnosticInfo[] diagnostics)
            {
                Behavior = behavior;
                Actor = actor;
                KeyType = keyType;
                StartupKeyType = startupKeyType;
                ApiAccessibility = apiAccessibility;
                Methods = methods;
                Diagnostics = diagnostics;
            }

            public INamedTypeSymbol Behavior { get; }

            public INamedTypeSymbol Actor { get; }

            public ITypeSymbol KeyType { get; }

            public ITypeSymbol? StartupKeyType { get; }

            public string ApiAccessibility { get; }

            public HotfixActorMethodInfo[] Methods { get; }

            public HotfixGeneratorDiagnosticInfo[] Diagnostics { get; }

            public bool IsSupported => Diagnostics.Length == 0 && Methods.Length > 0;
        }

        internal sealed class HotfixGeneratorDiagnosticInfo
        {
            public HotfixGeneratorDiagnosticInfo(
                DiagnosticDescriptor descriptor,
                Location? location,
                params object[] arguments)
            {
                Descriptor = descriptor;
                Location = location;
                Arguments = arguments;
            }

            public DiagnosticDescriptor Descriptor { get; }

            public Location? Location { get; }

            public object[] Arguments { get; }
        }

        internal sealed class HotfixActorMethodInfo
        {
            private HotfixActorMethodInfo(
                string name,
                ITypeSymbol requestType,
                ITypeSymbol? resultType,
                bool hasCancellationToken,
                Location? location,
                string methodKey)
            {
                Name = name;
                RequestType = requestType;
                ResultType = resultType;
                HasCancellationToken = hasCancellationToken;
                Location = location;
                MethodKey = methodKey;
            }

            public string Name { get; }

            public ITypeSymbol RequestType { get; }

            public ITypeSymbol? ResultType { get; }

            public bool HasCancellationToken { get; }

            public Location? Location { get; }

            public string MethodKey { get; }

            public static HotfixActorMethodInfo Create(
                IMethodSymbol method,
                ITypeSymbol requestType,
                ITypeSymbol? resultType,
                bool hasCancellationToken,
                string methodKey)
            {
                return new HotfixActorMethodInfo(
                    method.Name,
                    requestType,
                    resultType,
                    hasCancellationToken,
                    method.Locations.FirstOrDefault(),
                    methodKey);
            }
        }
    }
}
