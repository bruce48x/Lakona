namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Base class for reloadable hotfix feature declarations.
/// </summary>
/// <remarks>
/// User code declares one class per hotfix feature, marks it with
/// <see cref="HotfixFeatureAttribute"/>, and optionally provides public static
/// <c>Configure</c>, <c>StartAsync</c>, and <c>StopAsync</c> methods with the
/// framework-supported signatures. Feature instances are not long-lived state;
/// persistent runtime handles should be stored in <see cref="HotfixFeatureState"/>.
/// </remarks>
public abstract class HotfixGameFeature
{
}
