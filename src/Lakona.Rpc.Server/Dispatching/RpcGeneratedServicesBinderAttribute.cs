namespace Lakona.Rpc.Server;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RpcGeneratedServicesBinderAttribute : Attribute
{
    public RpcGeneratedServicesBinderAttribute(Type binderType)
    {
        BinderType = binderType ?? throw new ArgumentNullException(nameof(binderType));
    }

    public Type BinderType { get; }
}
