using System.Reflection;
using System.Reflection.Emit;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Rpc.Core;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public void Scanner_requires_one_hotfix_service_for_declared_rpc_contract()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(MissingHotfixContract).Assembly,
            [typeof(UnrelatedHotfixService)],
            requiredServiceContracts: [typeof(MissingHotfixContract)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains("MissingHotfixContract", StringComparison.Ordinal) &&
            diagnostic.Contains("exactly one", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_discovers_hotfix_lifecycle_methods()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(TestLifecycleContract).Assembly,
            [typeof(TestLifecycleImplementation)],
            requiredServiceContracts: [typeof(TestLifecycleContract)]);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var binding = Assert.Single(scan.Services);
        Assert.Equal(typeof(TestLifecycleContract), binding.ContractType);
    }

    [Fact]
    public void Scanner_rejects_lifecycle_methods_that_use_service_call_wrapper()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(TestLifecycleContract).Assembly,
            [typeof(TestLifecycleWithServiceCallImplementation)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(TestLifecycleWithServiceCallImplementation), StringComparison.Ordinal) &&
            diagnostic.Contains("HotfixLifecycleCall<TRequest>", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_service_methods_that_use_lifecycle_call_wrapper()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(TestServiceContract).Assembly,
            [typeof(TestServiceWithLifecycleCallImplementation)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(TestServiceWithLifecycleCallImplementation), StringComparison.Ordinal) &&
            diagnostic.Contains("HotfixServiceCall<TRequest>", StringComparison.Ordinal) &&
            diagnostic.Contains("HotfixServiceCall<TRequest, TCallback>", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_reports_required_lifecycle_contracts_with_lifecycle_diagnostic_wording()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(TestLifecycleContract).Assembly,
            [],
            requiredServiceContracts: [typeof(TestLifecycleContract)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(TestLifecycleContract), StringComparison.Ordinal) &&
            diagnostic.Contains("[HotfixService] or [HotfixLifecycle]", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_duplicate_hotfix_services_for_declared_rpc_contract()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(DuplicateHotfixContract).Assembly,
            [typeof(DuplicateHotfixServiceA), typeof(DuplicateHotfixServiceB)],
            requiredServiceContracts: [typeof(DuplicateHotfixContract)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains("DuplicateHotfixContract", StringComparison.Ordinal) &&
            diagnostic.Contains("2", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_accepts_non_static_service_with_constructor_dependencies()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(ConstructorDependencyServiceContract).Assembly,
            [typeof(ConstructorDependencyService)]);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var binding = Assert.Single(scan.Services);
        Assert.Equal(typeof(ConstructorDependencyService), binding.ServiceType);
    }

    [Fact]
    public void Scanner_rejects_non_static_service_with_raw_dto_parameter()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(ConstructorDependencyServiceContract).Assembly,
            [typeof(InstanceRawDtoService)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(InstanceRawDtoService), StringComparison.Ordinal) &&
            diagnostic.Contains("instance dispatch", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Contains("HotfixServiceCall", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_accepts_static_service_with_raw_dto_parameter()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(ConstructorDependencyServiceContract).Assembly,
            [typeof(StaticRawDtoService)]);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var binding = Assert.Single(scan.Services);
        Assert.Equal(typeof(StaticRawDtoService), binding.ServiceType);
    }

    [Fact]
    public void Scanner_rejects_open_generic_hotfix_service_type()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(GenericHotfixService<>).Assembly,
            [typeof(GenericHotfixService<>)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(GenericHotfixService<object>), StringComparison.Ordinal) ||
            diagnostic.Contains("open generic", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_service_with_multiple_unmarked_public_constructors()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(MultipleConstructorService).Assembly,
            [typeof(MultipleConstructorService)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains("multiple public constructors", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_accepts_service_with_activator_utilities_constructor_marker()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(MarkedConstructorService).Assembly,
            [typeof(MarkedConstructorService)]);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
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

    [RpcService(201)]
    public interface MissingHotfixContract
    {
        [RpcMethod(1)]
        ValueTask PingAsync(MissingHotfixRequest request);
    }

    public sealed class MissingHotfixRequest
    {
    }

    public interface TestLifecycleContract
    {
        [RpcMethod(301)]
        ValueTask ExpiredAsync(TestLifecycleRequest request);
    }

    public sealed class TestLifecycleRequest
    {
    }

    public interface TestServiceContract
    {
        [RpcMethod(302)]
        ValueTask PingAsync(TestServiceRequest request);
    }

    public sealed class TestServiceRequest
    {
    }

    [HotfixLifecycle(typeof(TestLifecycleContract))]
    public sealed class TestLifecycleImplementation
    {
        public static ValueTask ExpiredAsync(HotfixLifecycleCall<TestLifecycleRequest> call)
        {
            return default;
        }
    }

    [HotfixLifecycle(typeof(TestLifecycleContract))]
    public sealed class TestLifecycleWithServiceCallImplementation
    {
        public static ValueTask ExpiredAsync(HotfixServiceCall<TestLifecycleRequest> call)
        {
            return default;
        }
    }

    [HotfixService(typeof(TestServiceContract))]
    public sealed class TestServiceWithLifecycleCallImplementation
    {
        public static ValueTask PingAsync(HotfixLifecycleCall<TestServiceRequest> call)
        {
            return default;
        }
    }

    public sealed class UnrelatedHotfixService
    {
    }

    [RpcService(202)]
    public interface DuplicateHotfixContract
    {
        [RpcMethod(1)]
        ValueTask PingAsync(DuplicateHotfixRequest request);
    }

    public sealed class DuplicateHotfixRequest
    {
    }

    [HotfixService(typeof(DuplicateHotfixContract))]
    public sealed class DuplicateHotfixServiceA
    {
        public static ValueTask PingAsync(HotfixServiceCall<DuplicateHotfixRequest> call)
        {
            return default;
        }
    }

    [HotfixService(typeof(DuplicateHotfixContract))]
    public sealed class DuplicateHotfixServiceB
    {
        public static ValueTask PingAsync(HotfixServiceCall<DuplicateHotfixRequest> call)
        {
            return default;
        }
    }

    [RpcService(303)]
    public interface ConstructorDependencyServiceContract
    {
        [RpcMethod(1)]
        ValueTask PingAsync(ConstructorDependencyRequest request);
    }

    public sealed class ConstructorDependencyRequest
    {
    }

    public sealed class ConstructorDependency
    {
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class ConstructorDependencyService
    {
        public ConstructorDependencyService(ConstructorDependency dependency)
        {
        }

        public ValueTask PingAsync(HotfixServiceCall<ConstructorDependencyRequest> call)
        {
            return default;
        }
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class InstanceRawDtoService
    {
        public ValueTask PingAsync(ConstructorDependencyRequest request)
        {
            return default;
        }
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class StaticRawDtoService
    {
        public static ValueTask PingAsync(ConstructorDependencyRequest request)
        {
            return default;
        }
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class GenericHotfixService<T>
    {
        public ValueTask PingAsync(HotfixServiceCall<ConstructorDependencyRequest> call)
        {
            return default;
        }
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class MultipleConstructorService
    {
        public MultipleConstructorService(ConstructorDependency dependency)
        {
        }

        public MultipleConstructorService(string value)
        {
        }

        public ValueTask PingAsync(HotfixServiceCall<ConstructorDependencyRequest> call)
        {
            return default;
        }
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class MarkedConstructorService
    {
        public MarkedConstructorService(string value)
        {
        }

        [ActivatorUtilitiesConstructor]
        public MarkedConstructorService(ConstructorDependency dependency)
        {
        }

        public ValueTask PingAsync(HotfixServiceCall<ConstructorDependencyRequest> call)
        {
            return default;
        }
    }
}
