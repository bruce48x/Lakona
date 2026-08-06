using System;

namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Marks an interface as an RPC service. ServiceId must be stable across versions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class RpcServiceAttribute : Attribute
    {
        public RpcServiceAttribute(int serviceId)
        {
            ServiceId = serviceId;
        }

        public int ServiceId { get; }
        public string? ApiGroup { get; set; }
        public string? ApiName { get; set; }
        public Type? NotificationContract { get; set; }
    }

    /// <summary>
    ///     Marks an interface as a server-to-client notification contract. This
    ///     parameterless marker distinguishes an intentional RPC notification
    ///     contract from an arbitrary interface. The owning RPC service is
    ///     declared by that service's <see cref="RpcServiceAttribute.NotificationContract"/>
    ///     property, which is the single association authority between a service
    ///     and its notification contract.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
    public sealed class RpcNotificationContractAttribute : Attribute
    {
    }

    /// <summary>
    ///     Marks an interface method as an RPC method. MethodId must be stable within a service.
    ///     Lakona.Rpc source generation requires exactly one request DTO parameter and generates payload packing/unpacking for it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class RpcMethodAttribute : Attribute
    {
        public RpcMethodAttribute(int methodId)
        {
            MethodId = methodId;
        }

        public int MethodId { get; }
    }

    /// <summary>
    ///     Marks an interface method as a server-to-client notification. MethodId must be stable within a notification contract.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class RpcNotificationAttribute : Attribute
    {
        public RpcNotificationAttribute(int methodId)
        {
            MethodId = methodId;
        }

        public int MethodId { get; }
    }

    /// <summary>
    ///     Marks the current assembly as the client assembly that should receive generated RPC client glue.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class LakonaRpcGenerateClientAttribute : Attribute
    {
        public LakonaRpcGenerateClientAttribute()
        {
        }

        public LakonaRpcGenerateClientAttribute(string generatedNamespace)
        {
            GeneratedNamespace = generatedNamespace;
        }

        public string? GeneratedNamespace { get; }
    }

    /// <summary>
    ///     Marks the current assembly as the game client assembly that should receive the generated Lakona game client wrapper.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class LakonaGameGenerateClientAttribute : Attribute
    {
        public LakonaGameGenerateClientAttribute()
        {
        }
    }
}
