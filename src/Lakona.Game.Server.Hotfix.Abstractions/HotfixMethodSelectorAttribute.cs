using System.ComponentModel;

namespace Lakona.Game.Server.Hotfix.Abstractions;

[AttributeUsage(AttributeTargets.Parameter)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HotfixMethodSelectorAttribute : Attribute;
