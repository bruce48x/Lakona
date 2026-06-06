using Lakona.Game.Server.Hotfix.Dispatch;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixUnloadTests
{
    [Fact]
    public void Dispatch_table_version_changes_after_replace()
    {
        var first = new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>());
        var second = new HotfixDispatchTable(2, Array.Empty<HotfixMethodBinding>());

        HotfixDispatch.Replace(first);
        Assert.Equal(1, HotfixDispatch.Current.Version);

        HotfixDispatch.Replace(second);
        Assert.Equal(2, HotfixDispatch.Current.Version);
    }
}
