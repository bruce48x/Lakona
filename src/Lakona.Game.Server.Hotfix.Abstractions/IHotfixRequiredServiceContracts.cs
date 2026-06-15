namespace Lakona.Game.Server.Hotfix.Abstractions;

public interface IHotfixRequiredServiceContracts
{
    IReadOnlyList<Type> ServiceContracts { get; }
}
