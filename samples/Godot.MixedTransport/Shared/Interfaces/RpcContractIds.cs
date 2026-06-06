namespace Shared.Interfaces;

public static class RpcContractIds
{
    public static class Services
    {
        public const int Auth = 1;
        public const int Battle = 2;
    }

    public static class AuthServiceMethods
    {
        public const int LoginAsync = 1;
    }

    public static class BattleServiceMethods
    {
        public const int JoinAsync = 1;
        public const int UpdateInputAsync = 2;
    }

    public static class BattleNotifications
    {
        public const int OnSnapshot = 1;
    }
}
