namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record ActorStartupDeclaration(
    string Name,
    Func<ActorStartupContext, ActorStartupPlan> CreatePlan);
