using System.Diagnostics;
using System.IO.Pipes;

namespace Lakona.Hub;

internal sealed class HubSingleInstance : IDisposable
{
    private const string WindowsMutexName = @"Local\Lakona.Hub.SingleInstance";
    private const string DefaultMutexName = "Lakona.Hub.SingleInstance";
    private const string DefaultPipeName = "Lakona.Hub.Activation";
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(2);
    private readonly Mutex? mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource listenerCancellation = new();
    private Task? listenerTask;
    private Action? activationHandler;
    private int disposed;

    private HubSingleInstance(Mutex? mutex, string pipeName, bool isPrimary)
    {
        this.mutex = mutex;
        this.pipeName = pipeName;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    internal static HubSingleInstance Acquire() => Acquire(GetMutexName(), DefaultPipeName);

    internal static HubSingleInstance Acquire(string mutexName, string pipeName)
    {
        try
        {
            var instanceMutex = new Mutex(
                initiallyOwned: true,
                mutexName,
                new NamedWaitHandleOptions { CurrentUserOnly = true },
                out var createdNew);
            if (createdNew)
            {
                return new HubSingleInstance(instanceMutex, pipeName, isPrimary: true);
            }

            instanceMutex.Dispose();
            return new HubSingleInstance(null, pipeName, isPrimary: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Lakona Hub could not create its single-instance lock: {exception.Message}");
            return new HubSingleInstance(null, pipeName, isPrimary: true);
        }
    }

    internal bool NotifyPrimary() => NotifyPrimary(pipeName);

    internal static bool NotifyPrimary(string pipeName)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(ActivationTimeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                client.Connect(100);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    internal void StartListening(Action activatePrimaryWindow)
    {
        ArgumentNullException.ThrowIfNull(activatePrimaryWindow);
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (!IsPrimary || mutex is null)
        {
            return;
        }

        if (listenerTask is not null)
        {
            throw new InvalidOperationException("The Hub single-instance listener has already started.");
        }

        activationHandler = activatePrimaryWindow;
        listenerTask = Task.Run(ListenAsync);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        listenerCancellation.Cancel();
        if (listenerTask is not null)
        {
            try
            {
                listenerTask.GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Shutdown owns cancellation; a pipe that is already closing is harmless.
            }
        }

        listenerCancellation.Dispose();
        if (mutex is not null)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process is already leaving or the OS has released the handle.
            }

            mutex.Dispose();
        }
    }

    private async Task ListenAsync()
    {
        while (!listenerCancellation.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(listenerCancellation.Token);
                activationHandler?.Invoke();
            }
            catch (OperationCanceledException) when (listenerCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Lakona Hub single-instance activation listener failed: {exception.Message}");
                try
                {
                    await Task.Delay(50, listenerCancellation.Token);
                }
                catch (OperationCanceledException) when (listenerCancellation.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private static string GetMutexName() => OperatingSystem.IsWindows() ? WindowsMutexName : DefaultMutexName;
}
