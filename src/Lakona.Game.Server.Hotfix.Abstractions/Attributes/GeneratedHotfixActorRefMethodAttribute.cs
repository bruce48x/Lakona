using System;
using System.ComponentModel;

namespace Lakona.Game.Server.Hotfix.Abstractions;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedHotfixActorRefMethodAttribute : Attribute
{
}
