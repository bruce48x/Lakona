using System.Collections.Generic;

namespace Lakona.Rpc.Analyzers;

public sealed partial class LakonaRpcSourceGenerator
{
    private interface IRpcMethodContract
    {
        string Name { get; }
        int MethodId { get; }
    }

    private sealed class RpcServiceModel
    {
        public RpcServiceModel(
            string interfaceName,
            string fullName,
            string metadataName,
            int serviceId,
            List<RpcMethodModel> methods,
            string? notificationContractInterfaceName,
            string? notificationContractFullName,
            string apiGroupName,
            string apiName)
        {
            InterfaceName = interfaceName;
            FullName = fullName;
            MetadataName = metadataName;
            ServiceId = serviceId;
            Methods = methods;
            NotificationContractInterfaceName = notificationContractInterfaceName;
            NotificationContractFullName = notificationContractFullName;
            ApiGroupName = apiGroupName;
            ApiName = apiName;
        }

        public string InterfaceName { get; }
        public string FullName { get; }
        public string MetadataName { get; }
        public int ServiceId { get; }
        public List<RpcMethodModel> Methods { get; }
        public string? NotificationContractInterfaceName { get; }
        public string? NotificationContractFullName { get; }
        public string? NotificationContractInterfaceFullName => NotificationContractFullName;
        public string ApiGroupName { get; }
        public string ApiName { get; }
        public List<RpcNotificationMethodModel> NotificationMethods { get; set; } = new();
        public bool HasNotificationContract => NotificationContractFullName is not null && NotificationMethods.Count > 0;
    }

    private sealed class NotificationContractModel
    {
        public NotificationContractModel(string name, string fullName, List<RpcNotificationMethodModel> methods)
        {
            Name = name;
            FullName = fullName;
            Methods = methods;
        }

        public string Name { get; }
        public string FullName { get; }
        public List<RpcNotificationMethodModel> Methods { get; }
    }

    private sealed class RpcMethodModel : IRpcMethodContract
    {
        public RpcMethodModel(string name, int methodId, List<RpcParameterModel> parameters, string? returnTypeName, bool isVoid)
        {
            Name = name;
            MethodId = methodId;
            Parameters = parameters;
            ReturnTypeName = returnTypeName;
            IsVoid = isVoid;
        }

        public string Name { get; }
        public int MethodId { get; }
        public List<RpcParameterModel> Parameters { get; }
        public string? ReturnTypeName { get; }
        public bool IsVoid { get; }
        public string PayloadType => Parameters[0].TypeName;
        public string PayloadValue => Parameters[0].Name;
    }

    private sealed class RpcNotificationMethodModel : IRpcMethodContract
    {
        public RpcNotificationMethodModel(string name, int methodId, List<RpcParameterModel> parameters, bool returnsValueTask)
        {
            Name = name;
            MethodId = methodId;
            Parameters = parameters;
            ReturnsValueTask = returnsValueTask;
        }

        public string Name { get; }
        public int MethodId { get; }
        public List<RpcParameterModel> Parameters { get; }
        public bool ReturnsValueTask { get; }
        public bool AcceptsCancellationToken => Parameters.Count == 2 && Parameters[1].IsCancellationToken;
        public string PayloadType => Parameters[0].TypeName;
        public string PayloadValue => Parameters[0].Name;
    }

    private sealed class RpcParameterModel
    {
        public RpcParameterModel(
            string typeName,
            string name,
            bool isCancellationToken)
        {
            TypeName = typeName;
            Name = name;
            IsCancellationToken = isCancellationToken;
        }

        public string TypeName { get; }
        public string Name { get; }
        public bool IsCancellationToken { get; }
    }

    private sealed class FacadeGroupModel
    {
        public FacadeGroupModel(string groupName, List<RpcServiceModel> services)
        {
            GroupName = groupName;
            Services = services;
        }

        public string GroupName { get; }
        public List<RpcServiceModel> Services { get; }
    }
}
