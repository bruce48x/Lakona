namespace Game.Rpc.Contracts
{
    public static class RpcContractIds
    {
        public static class Services
        {
            public const int Player = 1;
        }

        public static class PlayerServiceMethods
        {
            public const int LoginAsync = 1;
            public const int IncrStep = 2;
        }

        public static class PlayerNotifications
        {
            public const int OnNotify = 1;
        }
    }
}
