namespace Shared.Contracts
{
    public static class RpcContractIds
    {
        public static class Services
        {
            public const int Login = 1;
            public const int Chat = 2;
        }

        public static class LoginServiceMethods
        {
            public const int LoginAsync = 1;
        }

        public static class LoginNotifications
        {
            public const int UserJoined = 1;
            public const int UserLeft = 2;
        }

        public static class ChatServiceMethods
        {
            public const int BindAsync = 1;
            public const int SendAsync = 2;
        }

        public static class ChatNotifications
        {
            public const int MessageReceived = 1;
        }
    }
}
