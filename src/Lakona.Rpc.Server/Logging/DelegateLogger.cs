using Microsoft.Extensions.Logging;

namespace Lakona.Rpc.Server;

internal sealed class DelegateLogger : ILogger
{
    private readonly Action<string> _write;

    public DelegateLogger(Action<string> write)
    {
        _write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (exception is not null)
            message = $"{message}{Environment.NewLine}{exception}";

        _write(message);
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
