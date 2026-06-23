using System.Reflection;
using Lakona.Rpc.Core;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class RpcContractSplitTests
{
    [Fact]
    public void SharedContractsExposeSeparateLoginPlayerAndBattleServices()
    {
        AssertRpcService(typeof(ILoginService), 1, notificationContract: null);
        AssertRpcService(typeof(IPlayerService), 2, typeof(IControlCallback));
        AssertRpcService(typeof(IBattleService), 3, typeof(IBattleCallback));

        AssertRpcNotification(typeof(IControlCallback), typeof(IPlayerService));
        AssertRpcNotification(typeof(IBattleCallback), typeof(IBattleService));

        Assert.Equal(
            new[] { "LoginAsync" },
            RpcMethodNames(typeof(ILoginService)));
        Assert.Equal(
            new[] { "StartMatchmakingAsync", "CancelMatchmakingAsync", "GetLeaderboardAsync", "LogoutAsync" },
            RpcMethodNames(typeof(IPlayerService)));
        Assert.Equal(
            new[] { "AttachRealtimeAsync", "SubmitInputAsync" },
            RpcMethodNames(typeof(IBattleService)));
    }

    [Fact]
    public void SharedContractsDoNotExposeControlBindingOrOldPlayerCallback()
    {
        var sharedAssembly = typeof(ILoginService).Assembly;

        Assert.Null(sharedAssembly.GetType("Shared.Interfaces." + "IPlayer" + "Callback"));
        Assert.DoesNotContain("Bind" + "ControlAsync", File.ReadAllText(SharedContractPath()), StringComparison.Ordinal);
    }

    [Fact]
    public void AgarSampleDoesNotUseHandWrittenRpcServiceAliasBinders()
    {
        var binderPath = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "App",
            "Hosting",
            "AgarRpcService" + "Binders.cs");

        Assert.False(File.Exists(binderPath));
    }

    private static void AssertRpcService(Type serviceType, int serviceId, Type? notificationContract)
    {
        var attribute = serviceType.GetCustomAttribute<RpcServiceAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(serviceId, attribute.ServiceId);
        Assert.Equal(notificationContract, attribute.NotificationContract);
    }

    private static void AssertRpcNotification(Type callbackType, Type serviceType)
    {
        var attribute = callbackType.GetCustomAttribute<RpcNotificationContractAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(serviceType, attribute.ServiceType);
    }

    private static string[] RpcMethodNames(Type serviceType)
    {
        return serviceType
            .GetMethods()
            .Where(method => method.GetCustomAttribute<RpcMethodAttribute>() is not null)
            .OrderBy(method => method.GetCustomAttribute<RpcMethodAttribute>()!.MethodId)
            .Select(method => method.Name)
            .ToArray();
    }

    private static string SharedContractPath()
    {
        return Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Shared",
            "Interfaces",
            "IPlayerService.cs");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples", "Game.Unity.Agar")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }
}
