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

    internal static bool NotifyPrimary(string pipeName) => NotifyPrimaryAsync(pipeName).GetAwaiter().GetResult();

    private static async Task<bool> NotifyPrimaryAsync(string pipeName)
    {
        using var timeout = new CancellationTokenSource(ActivationTimeout);
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(100, timeout.Token).ConfigureAwait(false);
                var acknowledgement = new byte[1];
                return await client.ReadAsync(acknowledgement, timeout.Token).ConfigureAwait(false) == 1
                    && acknowledgement[0] == 1;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
            {
                try
                {
                    await Task.Delay(50, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    return false;
                }
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
        listenerTask = ListenAsync();
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
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(listenerCancellation.Token).ConfigureAwait(false);
                activationHandler?.Invoke();
                await server.WriteAsync(new byte[] { 1 }, listenerCancellation.Token).ConfigureAwait(false);
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
                    await Task.Delay(50, listenerCancellation.Token).ConfigureAwait(false);
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
