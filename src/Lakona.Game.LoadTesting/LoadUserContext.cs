using System.Diagnostics;
using Lakona.Game.LoadTesting.Internal;

namespace Lakona.Game.LoadTesting;

public sealed class LoadUserContext
{
    private readonly LoadRunRecorder recorder;

    internal LoadUserContext(int userIndex, string userName)
        : this(userIndex, userName, new LoadRunRecorder("default", 1))
    {
    }

    internal LoadUserContext(int userIndex, string userName, LoadRunRecorder recorder)
    {
        UserIndex = userIndex;
        UserName = userName;
        this.recorder = recorder;
    }

    public int UserIndex { get; }

    public string UserName { get; }

    public async ValueTask MeasureAsync(
        string operationName,
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(action);

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
            recorder.RecordSucceededOperation(operationName, Stopwatch.GetElapsedTime(startedAt));
        }
        catch (OperationCanceledException ex)
        {
            recorder.RecordCanceledOperation(operationName, Stopwatch.GetElapsedTime(startedAt), ex);
            throw;
        }
        catch (Exception ex)
        {
            recorder.RecordFailedOperation(operationName, Stopwatch.GetElapsedTime(startedAt), ex);
            throw;
        }
    }
}
