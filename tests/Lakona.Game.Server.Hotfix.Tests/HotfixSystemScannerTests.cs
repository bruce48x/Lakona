using System.Reflection;
using System.Reflection.Emit;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixSystemScannerTests
{
    [Fact]
    public void Scan_discovers_public_static_extension_methods()
    {
        var assembly = CreateAssembly(nameof(Scan_discovers_public_static_extension_methods), module =>
        {
            CreateHotfixSystemType(module, "ValidStateSystem", typeof(ValidState), methodName: "Add");
        });

        var result = HotfixSystemScanner.Scan(assembly);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Assert.Single(result.Methods);
        Assert.Equal(typeof(ValidState).FullName, method.Key.StateTypeName);
        Assert.Equal("Add", method.Key.MethodName);
        Assert.Equal(typeof(int).FullName, method.Key.ReturnTypeName);
        Assert.Equal([typeof(int).FullName!], method.Key.ParameterTypeNames);
    }

    [Fact]
    public void Scan_rejects_duplicate_method_keys()
    {
        var assembly = CreateAssembly(nameof(Scan_rejects_duplicate_method_keys), module =>
        {
            CreateHotfixSystemType(module, "DuplicateStateSystemA", typeof(DuplicateState), methodName: "Add");
            CreateHotfixSystemType(module, "DuplicateStateSystemB", typeof(DuplicateState), methodName: "Add");
        });

        var result = HotfixSystemScanner.Scan(assembly);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Duplicate hotfix method key", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_rejects_generic_extension_methods()
    {
        var assembly = CreateAssembly(nameof(Scan_rejects_generic_extension_methods), module =>
        {
            CreateGenericHotfixSystemType(module, typeof(GenericState));
        });

        var result = HotfixSystemScanner.Scan(assembly);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("must not be generic", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_rejects_out_parameter_extension_methods()
    {
        var assembly = CreateAssembly(nameof(Scan_rejects_out_parameter_extension_methods), module =>
        {
            CreateOutParameterHotfixSystemType(module, typeof(OutParameterState));
        });

        var result = HotfixSystemScanner.Scan(assembly);

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

    private static Type CreateHotfixSystemType(ModuleBuilder module, string typeName, Type stateType, string methodName)
    {
        var systemType = DefineSystemType(module, typeName, stateType);
        var method = DefineExtensionMethod(systemType, methodName, typeof(int), [stateType, typeof(int)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);

        return systemType.CreateType();
    }

    private static Type CreateGenericHotfixSystemType(ModuleBuilder module, Type stateType)
    {
        var systemType = DefineSystemType(module, "GenericStateSystem", stateType);
        var method = systemType.DefineMethod(
            "Generic",
            MethodAttributes.Public | MethodAttributes.Static);
        var genericParameter = method.DefineGenericParameters("T")[0];
        method.SetReturnType(genericParameter);
        method.SetParameters(stateType, genericParameter);
        AddExtensionAttribute(method);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);

        return systemType.CreateType();
    }

    private static Type CreateOutParameterHotfixSystemType(ModuleBuilder module, Type stateType)
    {
        var systemType = DefineSystemType(module, "OutParameterStateSystem", stateType);
        var method = DefineExtensionMethod(systemType, "TryRead", typeof(bool), [stateType, typeof(int).MakeByRefType()]);
        method.DefineParameter(2, ParameterAttributes.Out, "value");

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        return systemType.CreateType();
    }

    private static TypeBuilder DefineSystemType(ModuleBuilder module, string typeName, Type stateType)
    {
        var systemType = module.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class);

        var attributeConstructor = typeof(HotfixSystemOfAttribute).GetConstructor([typeof(Type)])!;
        systemType.SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, [stateType]));

        return systemType;
    }

    private static MethodBuilder DefineExtensionMethod(TypeBuilder systemType, string name, Type returnType, Type[] parameterTypes)
    {
        var method = systemType.DefineMethod(
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

    public sealed class ValidState
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
