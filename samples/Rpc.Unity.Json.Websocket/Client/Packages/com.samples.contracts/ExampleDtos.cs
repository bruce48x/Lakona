namespace Game.Rpc.Contracts
{
    public class LoginRequest
    {
        public string Account { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class LoginReply
    {
        public int Code { get; set; }
        public string Token { get; set; } = "";
    }

    public class StepRequest
    {
    }

    public class StepReply
    {
        public int Step { get; set; }
    }

    public class PlayerNotify
    {
        public string Message { get; set; } = "";
    }
}
