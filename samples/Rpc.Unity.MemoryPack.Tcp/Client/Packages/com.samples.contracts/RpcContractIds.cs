namespace Game.Rpc.Contracts
{
    public static class RpcContractIds
    {
        public static class Services
        {
            public const int Player = 1;
            public const int Inventory = 2;
            public const int Quest = 3;
        }

        public static class PlayerServiceMethods
        {
            public const int LoginAsync = 1;
            public const int IncrStep = 2;
        }

        public static class PlayerNotifications
        {
            public const int OnPlayerNotify = 1;
        }

        public static class InventoryServiceMethods
        {
            public const int GetRevisionAsync = 1;
            public const int IncrRevision = 2;
        }

        public static class InventoryNotifications
        {
            public const int OnInventoryNotify = 1;
        }

        public static class QuestServiceMethods
        {
            public const int GetProgressAsync = 1;
            public const int IncrProgress = 2;
        }

        public static class QuestNotifications
        {
            public const int OnQuestNotify = 1;
        }
    }
}
