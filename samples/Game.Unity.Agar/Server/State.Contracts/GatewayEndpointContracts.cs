namespace Agar.Sample.State.Contracts;

public sealed class GatewayEndpointDescriptor
{
    public string InstanceId { get; set; } = "";

    public string Transport { get; set; } = "";

    public string Host { get; set; } = "";

    public int Port { get; set; }

    public string Path { get; set; } = "";
}
