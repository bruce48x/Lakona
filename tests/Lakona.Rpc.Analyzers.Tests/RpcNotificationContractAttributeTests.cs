using System;
using System.Linq;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Rpc.Analyzers.Tests;

public sealed class RpcNotificationContractAttributeTests
{
    [Fact]
    public void Marker_CompilesOnInterface_WithParameterlessConstructor()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation("""
            using Lakona.Rpc.Core;

            namespace Contracts
            {
                [RpcNotificationContract]
                public interface IExampleCallback
                {
                    [RpcNotification(1)]
                    void OnChanged(object update);
                }
            }
            """);

        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(compilation));
    }

    [Fact]
    public void Attribute_HasNoServiceTypeProperty()
    {
        Assert.Null(typeof(RpcNotificationContractAttribute).GetProperty("ServiceType"));
    }

    [Fact]
    public void Attribute_RemovedTypeConstructor_IsNotPartOfPublicContract()
    {
        Assert.Empty(
            typeof(RpcNotificationContractAttribute).GetConstructors()
                .Where(static constructor => constructor.GetParameters().Length == 1)
                .ToArray());
    }

    [Fact]
    public void Attribute_IsInterfaceOnlyParameterlessMarker()
    {
        var usage = typeof(RpcNotificationContractAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.Equal(AttributeTargets.Interface, usage.ValidOn);
        Assert.False(usage.Inherited);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void NotificationAttribute_RemainsMethodOnlyWithRequiredId()
    {
        var usage = typeof(RpcNotificationAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
        Assert.Equal(AttributeTargets.Method, usage.ValidOn);

        Assert.Single(
            typeof(RpcNotificationAttribute).GetConstructors(),
            static constructor => constructor.GetParameters().Length == 1 && constructor.GetParameters()[0].ParameterType == typeof(int));
    }

    [Fact]
    public void RemovedTypeConstructor_IsRejectedByTheCompiler()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation("""
            using Lakona.Rpc.Core;

            namespace Contracts
            {
                [RpcNotificationContract(typeof(IExampleCallback))]
                public interface IExampleCallback
                {
                    [RpcNotification(1)]
                    void OnChanged(object update);
                }
            }
            """);

        var errors = AnalyzerTestHelpers.ErrorDiagnostics(compilation);
        Assert.Contains(errors, static diagnostic => diagnostic.Id == "CS1729");
    }
}
