using System.Reflection;
using System.Reflection.Emit;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixBehaviorScannerTests
{
    [Fact]
    public void Scan_discovers_hotfix_behavior_methods()
    {
        var assembly = CreateAssembly(nameof(Scan_discovers_hotfix_behavior_methods), module =>
        {
            CreateHotfixBehaviorType(module, "ChatRoomBehavior", typeof(ChatRoomActor), methodName: "JoinAsync");
        });

        var result = HotfixBehaviorScanner.Scan(assembly);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Assert.Single(result.Methods);
        Assert.Equal(typeof(ChatRoomActor).FullName, method.Key.StateTypeName);
        Assert.Equal("JoinAsync", method.Key.MethodName);
        Assert.Equal(typeof(int).FullName, method.Key.ReturnTypeName);
        Assert.Equal([typeof(int).FullName!], method.Key.ParameterTypeNames);
    }

    [Fact]
    public void Scan_reports_behavior_name_for_non_static_behavior_type()
    {
        var assembly = CreateAssembly(nameof(Scan_reports_behavior_name_for_non_static_behavior_type), module =>
        {
            DefineBehaviorType(
                    module,
                    "InvalidChatRoomBehavior",
                    typeof(ChatRoomActor),
                    TypeAttributes.Public | TypeAttributes.Class)
                .CreateType();
        });

        var result = HotfixBehaviorScanner.Scan(assembly);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Hotfix behavior", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_rejects_duplicate_method_keys()
    {
        var assembly = CreateAssembly(nameof(Scan_rejects_duplicate_method_keys), module =>
        {
            CreateHotfixBehaviorType(module, "DuplicateStateBehaviorA", typeof(DuplicateState), methodName: "Add");
            CreateHotfixBehaviorType(module, "DuplicateStateBehaviorB", typeof(DuplicateState), methodName: "Add");
        });

        var result = HotfixBehaviorScanner.Scan(assembly);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Duplicate hotfix method key", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_rejects_generic_extension_methods()
    {
        var assembly = CreateAssembly(nameof(Scan_rejects_generic_extension_methods), module =>
        {
            CreateGenericHotfixBehaviorType(module, typeof(GenericState));
        });

        var result = HotfixBehaviorScanner.Scan(assembly);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("must not be generic", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_rejects_out_parameter_extension_methods()
    {
        var assembly = CreateAssembly(nameof(Scan_rejects_out_parameter_extension_methods), module =>
        {
            CreateOutParameterHotfixBehaviorType(module, typeof(OutParameterState));
        });

        var result = HotfixBehaviorScanner.Scan(assembly);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("must not use by-ref, out, or pointer parameter types", StringComparison.Ordinal));
    }

    [Fact]
    public void Dispatch_table_rejects_null_methods()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new HotfixDispatchTable(1, null!));

        Assert.Equal("methods", exception.ParamName);
    }

    [Fact]
    public void Dispatch_table_rejects_null_bindings()
    {
        var exception = Assert.Throws<ArgumentException>(() => new HotfixDispatchTable(1, [null!]));

        Assert.Equal("methods", exception.ParamName);
    }

    private static Assembly CreateAssembly(string name, Action<ModuleBuilder> build)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"{name}_{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule($"{name}Module");

        build(module);

        return assembly;
    }

    private static Type CreateHotfixBehaviorType(ModuleBuilder module, string typeName, Type stateType, string methodName)
    {
        var behaviorType = DefineBehaviorType(module, typeName, stateType);
        var method = DefineExtensionMethod(behaviorType, methodName, typeof(int), [stateType, typeof(int)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);

        return behaviorType.CreateType();
    }

    private static Type CreateGenericHotfixBehaviorType(ModuleBuilder module, Type stateType)
    {
        var behaviorType = DefineBehaviorType(module, "GenericStateBehavior", stateType);
        var method = behaviorType.DefineMethod(
            "Generic",
            MethodAttributes.Public | MethodAttributes.Static);
        var genericParameter = method.DefineGenericParameters("T")[0];
        method.SetReturnType(genericParameter);
        method.SetParameters(stateType, genericParameter);
        AddExtensionAttribute(method);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);

        return behaviorType.CreateType();
    }

    private static Type CreateOutParameterHotfixBehaviorType(ModuleBuilder module, Type stateType)
    {
        var behaviorType = DefineBehaviorType(module, "OutParameterStateBehavior", stateType);
        var method = DefineExtensionMethod(behaviorType, "TryRead", typeof(bool), [stateType, typeof(int).MakeByRefType()]);
        method.DefineParameter(2, ParameterAttributes.Out, "value");

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        return behaviorType.CreateType();
    }

    private static TypeBuilder DefineBehaviorType(
        ModuleBuilder module,
        string typeName,
        Type stateType,
        TypeAttributes attributes = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class)
    {
        var behaviorType = module.DefineType(
            typeName,
            attributes);

        var attributeConstructor = typeof(HotfixBehaviorOfAttribute).GetConstructor([typeof(Type)])!;
        behaviorType.SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, [stateType]));

        return behaviorType;
    }

    private static MethodBuilder DefineExtensionMethod(TypeBuilder behaviorType, string name, Type returnType, Type[] parameterTypes)
    {
        var method = behaviorType.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            parameterTypes);
        AddExtensionAttribute(method);

        return method;
    }

    private static void AddExtensionAttribute(MethodBuilder method)
    {
        var attributeConstructor = typeof(System.Runtime.CompilerServices.ExtensionAttribute).GetConstructor(Type.EmptyTypes)!;
        method.SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, []));
    }

    public sealed class ChatRoomActor
    {
    }

    public sealed class DuplicateState
    {
    }

    public sealed class GenericState
    {
    }

    public sealed class OutParameterState
    {
    }
}
