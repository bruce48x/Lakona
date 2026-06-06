using MemoryPack;

namespace Game.Rpc.Contracts
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LoginRequest
    {
        [MemoryPackOrder(0)]
        public string Account { get; set; } = "";

        [MemoryPackOrder(1)]
        public string Password { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LoginReply
    {
        [MemoryPackOrder(0)]
        public int Code { get; set; }

        [MemoryPackOrder(1)]
        public string Token { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class StepRequest
    {
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class StepReply
    {
        [MemoryPackOrder(0)]
        public int Step { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class RevisionRequest
    {
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class RevisionReply
    {
        [MemoryPackOrder(0)]
        public int Revision { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ProgressRequest
    {
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ProgressReply
    {
        [MemoryPackOrder(0)]
        public int Progress { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PlayerNotify
    {
        [MemoryPackOrder(0)]
        public string Message { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class InventoryNotify
    {
        [MemoryPackOrder(0)]
        public string Message { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class QuestNotify
    {
        [MemoryPackOrder(0)]
        public string Message { get; set; } = "";
    }
}
