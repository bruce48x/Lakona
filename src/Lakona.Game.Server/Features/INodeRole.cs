namespace Lakona.Game.Server.Features;

public interface INodeRole
{
    string Name { get; }

    IFeature[] Features { get; }
}
