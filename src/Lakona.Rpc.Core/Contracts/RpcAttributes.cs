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
    ///     Marks an interface as the server-to-client notification contract for a specific RPC service.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class RpcNotificationContractAttribute : Attribute
    {
        public RpcNotificationContractAttribute(Type serviceType)
        {
            ServiceType = serviceType;
        }

        public Type ServiceType { get; }
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
}
