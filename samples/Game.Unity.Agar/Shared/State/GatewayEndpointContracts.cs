using MemoryPack;

namespace Agar.Sample.State.Contracts
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class GatewayEndpointDescriptor
    {
        [MemoryPackOrder(0)]
        public string InstanceId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string Transport { get; set; } = "";

        [MemoryPackOrder(2)]
        public string Host { get; set; } = "";

        [MemoryPackOrder(3)]
        public int Port { get; set; }

        [MemoryPackOrder(4)]
        public string Path { get; set; } = "";
    }
}
