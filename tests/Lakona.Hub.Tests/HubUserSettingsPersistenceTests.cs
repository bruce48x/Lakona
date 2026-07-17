using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubUserSettingsPersistenceTests
{
    [Fact]
    public async Task ScheduleSave_CoalescesRapidChangesIntoOneWrite()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        using var persistence = new HubUserSettingsPersistence(
            Settings,
            _ => writes++,
            TimeSpan.FromSeconds(1),
            (_, cancellationToken) => release.Task.WaitAsync(cancellationToken));

        var first = persistence.ScheduleSave();
        var second = persistence.ScheduleSave();
        var third = persistence.ScheduleSave();
        release.SetResult();
        await Task.WhenAll(first, second, third);

        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task ScheduleSave_ReportsWriteFailures()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? observed = null;
        using var persistence = new HubUserSettingsPersistence(
            Settings,
            _ => throw new IOException("settings unavailable"),
            TimeSpan.FromSeconds(1),
            (_, cancellationToken) => release.Task.WaitAsync(cancellationToken));
        persistence.SaveFailed += exception => observed = exception;

        var save = persistence.ScheduleSave();
        release.SetResult();
        await save;

        Assert.IsType<IOException>(observed);
    }

    private static HubUserSettings Settings() =>
        new(HubLanguage.English, [], [], null, "Projects", null, null);
}
