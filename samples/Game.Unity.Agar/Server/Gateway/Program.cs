using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Gateway.Features;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Loading;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddLakonaGame(builder.Configuration, features =>
{
    features.Feature<GatewayCoreFeature>("gateway-core");
    features
        .Feature<GatewayBusinessFeature>("gateway-business")
        .After("gateway-core")
        .RequiresFeature("gateway-core")
        .RequiresTransport("websocket")
        .RequiresTransport("kcp");
});

var hotfixDirectory = Path.Combine(AppContext.BaseDirectory, "hotfix");
builder.Services.AddLakonaGameHotfix(
    new CurrentDirectoryHotfixAssemblySource(hotfixDirectory, "Agar.Sample.Hotfix.dll"),
    sharedAssemblyNames: ["Agar.Sample.Hotfix"]);

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var hotfix = scope.ServiceProvider.GetRequiredService<IHotfixManager>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Gateway.Hotfix");
    var result = await hotfix.ReloadAsync();
    if (result.Succeeded)
    {
        logger.LogInformation(
            "Initial hotfix load succeeded from {HotfixPath} with {MethodCount} method(s).",
            result.Current.SourcePath,
            result.Current.Methods.Count);
    }
    else
    {
        logger.LogWarning(
            "Initial hotfix load failed for {HotfixPath}: {ErrorMessage}",
            result.RequestedPath,
            result.ErrorMessage);
        foreach (var diagnostic in result.Diagnostics)
        {
            logger.LogWarning("Hotfix diagnostic: {Diagnostic}", diagnostic);
        }
    }
}

await host.RunAsync();
