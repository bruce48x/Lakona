namespace Lakona.Game.Server.Hotfix.Abstractions;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class FeatureCommandAttribute : Attribute
{
    public FeatureCommandAttribute(int id)
    {
        Id = id;
    }

    public int Id { get; }
}
