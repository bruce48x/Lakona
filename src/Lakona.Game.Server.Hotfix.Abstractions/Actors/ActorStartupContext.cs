namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record ActorStartupContext(
    string Name,
    IReadOnlyDictionary<string, string> Options);
