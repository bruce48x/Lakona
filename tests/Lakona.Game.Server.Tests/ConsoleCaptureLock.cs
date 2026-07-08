namespace Lakona.Game.Server.Tests;

internal static class ConsoleCaptureLock
{
    public static readonly object Gate = new();
}
