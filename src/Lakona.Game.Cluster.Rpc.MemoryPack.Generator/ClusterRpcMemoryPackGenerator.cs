using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Lakona.Game.Cluster.Rpc.MemoryPack.Generator;

[Generator]
public sealed class ClusterRpcMemoryPackGenerator : IIncrementalGenerator
{
    private const string SchemaFileName = "cluster-rpc-memorypack.schema.json";
    private const string GeneratedFileName = "ClusterRpcMemoryPackFormatters.g.cs";

    private static readonly DiagnosticDescriptor MissingSchema = new(
        "LKGMP001",
        "Cluster RPC MemoryPack schema is missing",
        "AdditionalFiles must include '{0}'",
        "Lakona.Game.Cluster.Rpc.MemoryPack",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingType = new(
        "LKGMP002",
        "Cluster RPC MemoryPack schema type is missing",
        "Schema type '{0}' could not be resolved as '{1}'",
        "Lakona.Game.Cluster.Rpc.MemoryPack",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingProperty = new(
        "LKGMP003",
        "Cluster RPC MemoryPack schema property is missing",
        "Schema property '{0}' on type '{1}' is missing or is not a public instance readable property",
        "Lakona.Game.Cluster.Rpc.MemoryPack",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var schemaFiles = context.AdditionalTextsProvider
            .Where(static file => string.Equals(Path.GetFileName(file.Path), SchemaFileName, StringComparison.Ordinal))
            .Collect();

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(schemaFiles),
            static (sourceProductionContext, input) => Execute(sourceProductionContext, input.Left, input.Right));
    }

    private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<AdditionalText> schemaFiles)
    {
        if (schemaFiles.Length == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingSchema, Location.None, SchemaFileName));
            return;
        }

        var schemaFile = schemaFiles[0];
        var schema = ReadSchema(schemaFile, context.CancellationToken);
        if (schema is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingSchema, Location.None, SchemaFileName));
            return;
        }

        var resolvedTypes = ResolveSchemaTypes(context, compilation, schema);
        if (resolvedTypes.IsDefaultOrEmpty)
        {
            return;
        }

        var source = GenerateSource(schema, resolvedTypes);
        context.AddSource(GeneratedFileName, SourceText.From(source, Encoding.UTF8));
    }

    private static SchemaModel? ReadSchema(AdditionalText schemaFile, CancellationToken cancellationToken)
    {
        var text = schemaFile.GetText(cancellationToken)?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var document = JsonDocument.Parse(text!);
        var root = document.RootElement;
        var schemaVersion = root.GetProperty("schemaVersion").GetInt32();
        var dtoNamespace = root.GetProperty("dtoNamespace").GetString();
        var formatterNamespace = root.GetProperty("formatterNamespace").GetString();
        var registrationClass = root.GetProperty("registrationClass").GetString();

        if (schemaVersion < 1 ||
            string.IsNullOrWhiteSpace(dtoNamespace) ||
            string.IsNullOrWhiteSpace(formatterNamespace) ||
            string.IsNullOrWhiteSpace(registrationClass))
        {
            return null;
        }

        var types = ImmutableArray.CreateBuilder<SchemaType>();
        foreach (var typeElement in root.GetProperty("types").EnumerateArray())
        {
            var name = typeElement.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var properties = ImmutableArray.CreateBuilder<string>();
            foreach (var propertyElement in typeElement.GetProperty("properties").EnumerateArray())
            {
                var property = propertyElement.GetString();
                if (string.IsNullOrWhiteSpace(property))
                {
                    return null;
                }

                properties.Add(property!);
            }

            types.Add(new SchemaType(name!, properties.ToImmutable()));
        }

        return new SchemaModel(dtoNamespace!, formatterNamespace!, registrationClass!, types.ToImmutable());
    }

    private static ImmutableArray<ResolvedType> ResolveSchemaTypes(
        SourceProductionContext context,
        Compilation compilation,
        SchemaModel schema)
    {
        var resolvedTypes = ImmutableArray.CreateBuilder<ResolvedType>();
        var hasDiagnostics = false;

        foreach (var schemaType in schema.Types)
        {
            var metadataName = schema.DtoNamespace + "." + schemaType.Name;
            var type = compilation.GetTypeByMetadataName(metadataName);
            if (type is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingType, Location.None, schemaType.Name, metadataName));
                hasDiagnostics = true;
                continue;
            }

            var properties = ImmutableArray.CreateBuilder<ResolvedProperty>();
            foreach (var propertyName in schemaType.Properties)
            {
                var property = type.GetMembers(propertyName)
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault(static property =>
                        property.DeclaredAccessibility == Accessibility.Public &&
                        !property.IsStatic &&
                        property.GetMethod is { DeclaredAccessibility: Accessibility.Public });

                if (property is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingProperty,
                        type.Locations.FirstOrDefault(),
                        propertyName,
                        type.ToDisplayString(TypeDisplayFormat)));
                    hasDiagnostics = true;
                    continue;
                }

                properties.Add(new ResolvedProperty(property));
            }

            resolvedTypes.Add(new ResolvedType(type, schemaType, properties.ToImmutable()));
        }

        return hasDiagnostics ? ImmutableArray<ResolvedType>.Empty : resolvedTypes.ToImmutable();
    }

    private static string GenerateSource(SchemaModel schema, ImmutableArray<ResolvedType> resolvedTypes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("#pragma warning disable CS8600");
        builder.AppendLine("#pragma warning disable CS8601");
        builder.AppendLine("#pragma warning disable CS8602");
        builder.AppendLine("#pragma warning disable CS8604");
        builder.AppendLine();
        builder.Append("namespace ").Append(schema.FormatterNamespace).AppendLine(";");
        builder.AppendLine();
        builder.Append("public static class ").Append(schema.RegistrationClass).AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    private static int s_registered;");
        builder.AppendLine();
        builder.AppendLine("    public static void Register()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (global::System.Threading.Interlocked.Exchange(ref s_registered, 1) == 1)");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine();

        foreach (var type in resolvedTypes)
        {
            builder
                .Append("        global::MemoryPack.MemoryPackFormatterProvider.Register<")
                .Append(TypeName(type.Symbol))
                .Append(">(new ")
                .Append(FormatterClassName(type.Symbol))
                .AppendLine("());");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();

        foreach (var type in resolvedTypes)
        {
            AppendFormatter(builder, type);
        }

        return builder.ToString();
    }

    private static void AppendFormatter(StringBuilder builder, ResolvedType type)
    {
        var typeName = TypeName(type.Symbol);
        var formatterName = FormatterClassName(type.Symbol);
        var properties = type.Properties;
        var encodings = properties.Select(static property => GetFieldEncoding(property.Symbol)).ToArray();
        var useDirectMetadata = encodings.All(static encoding => encoding.LengthExpression is not null);

        builder
            .Append("file sealed class ")
            .Append(formatterName)
            .Append(" : global::MemoryPack.MemoryPackFormatter<")
            .Append(typeName)
            .AppendLine(">");
        builder.AppendLine("{");
        builder
            .Append("    public override void Serialize<TBufferWriter>(ref global::MemoryPack.MemoryPackWriter<TBufferWriter> writer, scoped ref ")
            .Append(typeName)
            .AppendLine("? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (value is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            writer.WriteNullObjectHeader();");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine();

        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i].Symbol;
            builder
                .Append("        var __field")
                .Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(" = value.")
                .Append(EscapeIdentifier(property.Name))
                .AppendLine(";");
        }

        builder.AppendLine();

        if (useDirectMetadata)
        {
            builder
                .Append("        writer.WriteObjectHeader((byte)")
                .Append(properties.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine(");");

            for (var i = 0; i < properties.Length; i++)
            {
                builder
                    .Append("        writer.WriteVarInt(")
                    .Append(encodings[i].LengthExpression!("writer", "__field" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    .AppendLine(");");
            }

            builder.AppendLine();

            for (var i = 0; i < properties.Length; i++)
            {
                builder.Append("        ");
                AppendWritePayload(builder, encodings[i], "writer", "__field" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            builder.AppendLine("    }");
            builder.AppendLine();
            AppendDeserialize(builder, typeName, properties, encodings);
            builder.AppendLine("}");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("        var tempBuffer = global::MemoryPack.Internal.ReusableLinkedArrayBufferWriterPool.Rent();");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder
            .Append("            global::System.Span<int> offsets = stackalloc int[")
            .Append(properties.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .AppendLine("];");
        builder.AppendLine("            var tempWriter = new global::MemoryPack.MemoryPackWriter<global::MemoryPack.Internal.ReusableLinkedArrayBufferWriter>(ref tempBuffer, writer.OptionalState);");
        builder.AppendLine();

        for (var i = 0; i < properties.Length; i++)
        {
            builder.Append("            ");
            AppendWritePayload(builder, encodings[i], "tempWriter", "__field" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder
                .Append("            offsets[")
                .Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append("] = tempWriter.WrittenCount;")
                .AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("            tempWriter.Flush();");
        builder
            .Append("            writer.WriteObjectHeader((byte)")
            .Append(properties.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .AppendLine(");");
        builder
            .Append("            for (var i = 0; i < ")
            .Append(properties.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .AppendLine("; i++)");
        builder.AppendLine("            {");
        builder.AppendLine("                var delta = i == 0 ? offsets[i] : offsets[i] - offsets[i - 1];");
        builder.AppendLine("                writer.WriteVarInt(delta);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            tempBuffer.WriteToAndReset(ref writer);");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");
        builder.AppendLine("            global::MemoryPack.Internal.ReusableLinkedArrayBufferWriterPool.Return(tempBuffer);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();

        AppendDeserialize(builder, typeName, properties, encodings);
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendDeserialize(
        StringBuilder builder,
        string typeName,
        ImmutableArray<ResolvedProperty> properties,
        FieldEncoding[] encodings)
    {
        builder
            .Append("    public override void Deserialize(ref global::MemoryPack.MemoryPackReader reader, scoped ref ")
            .Append(typeName)
            .AppendLine("? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (!reader.TryReadObjectHeader(out var count))");
        builder.AppendLine("        {");
        builder.AppendLine("            value = null;");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var __isNew = value is null;");
        builder.AppendLine();
        builder
            .Append("        const byte memberCount = ")
            .Append(properties.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .AppendLine(";");
        builder.AppendLine("        if (count > memberCount)");
        builder.AppendLine("        {");
        builder
            .Append("            global::MemoryPack.MemoryPackSerializationException.ThrowInvalidPropertyCount(typeof(")
            .Append(typeName)
            .AppendLine("), memberCount, count);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        global::System.Span<int> deltas = stackalloc int[count];");
        builder.AppendLine("        for (var i = 0; i < count; i++)");
        builder.AppendLine("        {");
        builder.AppendLine("            deltas[i] = reader.ReadVarIntInt32();");
        builder.AppendLine("        }");
        builder.AppendLine();

        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i].Symbol;
            builder
                .Append("        ")
                .Append(TypeName(property.Type))
                .Append(" __field")
                .Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(" = __isNew ? ")
                .Append(DefaultInitializer(property))
                .Append(" : value.")
                .Append(EscapeIdentifier(property.Name))
                .AppendLine(";");
        }

        builder.AppendLine();
        builder.AppendLine("        switch (count)");
        builder.AppendLine("        {");
        for (var i = 0; i <= properties.Length; i++)
        {
            builder
                .Append("            case ")
                .Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine(":");
            for (var fieldIndex = 0; fieldIndex < i; fieldIndex++)
            {
                builder
                    .Append("                if (deltas[")
                    .Append(fieldIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .AppendLine("] != 0)");
                builder.AppendLine("                {");
                AppendReadPayload(
                    builder,
                    encodings[fieldIndex],
                    "reader",
                    "__field" + fieldIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "                    ");
                builder.AppendLine("                }");
            }

            builder.AppendLine("                break;");
        }

        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (__isNew)");
        builder.AppendLine("        {");
        builder
            .Append("            value = new ")
            .Append(typeName)
            .AppendLine("();");
        builder.AppendLine("        }");
        builder.AppendLine();

        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i].Symbol;
            builder
                .Append("        value.")
                .Append(EscapeIdentifier(property.Name))
                .Append(" = __field")
                .Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine(";");
        }

        builder.AppendLine("    }");
    }

    private static void AppendWritePayload(StringBuilder builder, FieldEncoding encoding, string writerName, string valueExpression)
    {
        builder
            .Append(encoding.WriteStatement(writerName, valueExpression))
            .AppendLine();
    }

    private static void AppendReadPayload(
        StringBuilder builder,
        FieldEncoding encoding,
        string readerName,
        string targetExpression,
        string indent)
    {
        if (encoding.ReadNewStatement == encoding.ReadExistingStatement)
        {
            builder
                .Append(indent)
                .Append(encoding.ReadExistingStatement(readerName, targetExpression))
                .AppendLine();
            return;
        }

        builder.Append(indent).AppendLine("if (__isNew)");
        builder.Append(indent).AppendLine("{");
        builder
            .Append(indent)
            .Append("    ")
            .Append(encoding.ReadNewStatement(readerName, targetExpression))
            .AppendLine();
        builder.Append(indent).AppendLine("}");
        builder.Append(indent).AppendLine("else");
        builder.Append(indent).AppendLine("{");
        builder
            .Append(indent)
            .Append("    ")
            .Append(encoding.ReadExistingStatement(readerName, targetExpression))
            .AppendLine();
        builder.Append(indent).AppendLine("}");
    }

    private static FieldEncoding GetFieldEncoding(IPropertySymbol property)
    {
        var type = property.Type;
        var typeName = TypeName(type);

        if (type.SpecialType == SpecialType.System_String)
        {
            return new FieldEncoding(
                static (writer, value) => writer + ".GetStringWriteLength(" + value + ")",
                static (writer, value) => writer + ".WriteString(" + value + ");",
                static (reader, target) => target + " = " + reader + ".ReadString();",
                static (reader, target) => target + " = " + reader + ".ReadString();");
        }

        if (IsByteArray(type))
        {
            return new FieldEncoding(
                static (writer, value) => writer + ".GetUnmanageArrayWriteLength<byte>(" + value + ")",
                static (writer, value) => writer + ".WriteUnmanagedArray(" + value + ");",
                static (reader, target) => target + " = " + reader + ".ReadUnmanagedArray<byte>();",
                static (reader, target) => reader + ".ReadUnmanagedArray(ref " + target + ");");
        }

        if (IsDirectUnmanaged(type, out var unmanagedTypeName))
        {
            return new FieldEncoding(
                (_, _) => "global::System.Runtime.CompilerServices.Unsafe.SizeOf<" + unmanagedTypeName + ">()",
                static (writer, value) => writer + ".WriteUnmanaged(" + value + ");",
                static (reader, target) => reader + ".ReadUnmanaged(out " + target + ");",
                static (reader, target) => reader + ".ReadUnmanaged(out " + target + ");");
        }

        if (IsNullablePrimitive(type))
        {
            return new FieldEncoding(
                null,
                static (writer, value) => writer + ".DangerousWriteUnmanaged(" + value + ");",
                static (reader, target) => reader + ".DangerousReadUnmanaged(out " + target + ");",
                static (reader, target) => reader + ".DangerousReadUnmanaged(out " + target + ");");
        }

        return new FieldEncoding(
            null,
            static (writer, value) => writer + ".WriteValue(" + value + ");",
            (reader, target) => target + " = " + reader + ".ReadValue<" + typeName + ">();",
            static (reader, target) => reader + ".ReadValue(ref " + target + ");");
    }

    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(TypeDisplayFormat);

    private static string FormatterClassName(INamedTypeSymbol type) => EscapeIdentifier(type.Name) + "Formatter";

    private static string EscapeIdentifier(string name) => "@" + name;

    private static string DefaultInitializer(IPropertySymbol property)
    {
        var type = property.Type;
        if (IsNullableValueType(type))
        {
            return "null";
        }

        if (type.IsReferenceType && property.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return "null";
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            return "string.Empty";
        }

        if (IsByteArray(type))
        {
            return "global::System.Array.Empty<byte>()";
        }

        if (TryGetListElementType(type, out var elementType))
        {
            return "new global::System.Collections.Generic.List<" + TypeName(elementType) + ">()";
        }

        if (IsDictionary(type))
        {
            return "null";
        }

        if (type.IsValueType)
        {
            return "default(" + TypeName(type) + ")";
        }

        return "null";
    }

    private static bool IsNullableValueType(ITypeSymbol type) =>
        type is INamedTypeSymbol namedType &&
        namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool IsNullablePrimitive(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType ||
            namedType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
        {
            return false;
        }

        return IsSupportedPrimitive(namedType.TypeArguments[0]);
    }

    private static bool IsDirectUnmanaged(ITypeSymbol type, out string unmanagedTypeName)
    {
        if (type.SpecialType == SpecialType.System_Int32 ||
            type.SpecialType == SpecialType.System_Int64 ||
            type.SpecialType == SpecialType.System_Boolean ||
            IsDateTimeOffset(type))
        {
            unmanagedTypeName = TypeName(type);
            return true;
        }

        unmanagedTypeName = string.Empty;
        return false;
    }

    private static bool IsSupportedPrimitive(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Int32 ||
        type.SpecialType == SpecialType.System_Int64 ||
        type.SpecialType == SpecialType.System_Boolean;

    private static bool IsDateTimeOffset(ITypeSymbol type) =>
        type is INamedTypeSymbol namedType &&
        namedType.Name == "DateTimeOffset" &&
        namedType.ContainingNamespace?.ToDisplayString() == "System";

    private static bool IsByteArray(ITypeSymbol type) =>
        type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte };

    private static bool TryGetListElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is INamedTypeSymbol namedType &&
            namedType.TypeArguments.Length == 1 &&
            IsSystemCollectionsGeneric(namedType.ContainingNamespace) &&
            (namedType.Name == "List" || namedType.Name == "IReadOnlyList"))
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        elementType = type;
        return false;
    }

    private static bool IsDictionary(ITypeSymbol type) =>
        type is INamedTypeSymbol namedType &&
        namedType.Name == "Dictionary" &&
        IsSystemCollectionsGeneric(namedType.ContainingNamespace);

    private static bool IsSystemCollectionsGeneric(INamespaceSymbol? ns) =>
        ns?.ToDisplayString() == "System.Collections.Generic";

    private sealed class SchemaModel
    {
        public SchemaModel(
            string dtoNamespace,
            string formatterNamespace,
            string registrationClass,
            ImmutableArray<SchemaType> types)
        {
            DtoNamespace = dtoNamespace;
            FormatterNamespace = formatterNamespace;
            RegistrationClass = registrationClass;
            Types = types;
        }

        public string DtoNamespace { get; }

        public string FormatterNamespace { get; }

        public string RegistrationClass { get; }

        public ImmutableArray<SchemaType> Types { get; }
    }

    private sealed class FieldEncoding
    {
        public FieldEncoding(
            Func<string, string, string>? lengthExpression,
            Func<string, string, string> writeStatement,
            Func<string, string, string> readNewStatement,
            Func<string, string, string> readExistingStatement)
        {
            LengthExpression = lengthExpression;
            WriteStatement = writeStatement;
            ReadNewStatement = readNewStatement;
            ReadExistingStatement = readExistingStatement;
        }

        public Func<string, string, string>? LengthExpression { get; }

        public Func<string, string, string> WriteStatement { get; }

        public Func<string, string, string> ReadNewStatement { get; }

        public Func<string, string, string> ReadExistingStatement { get; }
    }

    private sealed class SchemaType
    {
        public SchemaType(string name, ImmutableArray<string> properties)
        {
            Name = name;
            Properties = properties;
        }

        public string Name { get; }

        public ImmutableArray<string> Properties { get; }
    }

    private readonly struct ResolvedType
    {
        public ResolvedType(INamedTypeSymbol symbol, SchemaType schema, ImmutableArray<ResolvedProperty> properties)
        {
            Symbol = symbol;
            Schema = schema;
            Properties = properties;
        }

        public INamedTypeSymbol Symbol { get; }

        public SchemaType Schema { get; }

        public ImmutableArray<ResolvedProperty> Properties { get; }
    }

    private readonly struct ResolvedProperty
    {
        public ResolvedProperty(IPropertySymbol symbol)
        {
            Symbol = symbol;
        }

        public IPropertySymbol Symbol { get; }
    }
}
