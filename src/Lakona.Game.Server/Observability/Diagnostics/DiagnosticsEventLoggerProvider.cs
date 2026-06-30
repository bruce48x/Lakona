using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class DiagnosticsEventLoggerProvider : ILoggerProvider
{
    private readonly IDiagnosticsEventSink _sink;
    private readonly LogLevel _minimumLevel;

    public DiagnosticsEventLoggerProvider(IDiagnosticsEventSink sink, LogLevel minimumLevel)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _minimumLevel = minimumLevel;
    }

    public DiagnosticsEventLoggerProvider(IDiagnosticsEventSink sink, LakonaObservabilityOptions options)
        : this(
            sink,
            (options ?? throw new ArgumentNullException(nameof(options))).Diagnostics.EventBuffer.MinimumLevel)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new DiagnosticsEventLogger(_sink, _minimumLevel, categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class DiagnosticsEventLogger : ILogger
    {
        private readonly IDiagnosticsEventSink _sink;
        private readonly LogLevel _minimumLevel;
        private readonly string _categoryName;

        public DiagnosticsEventLogger(
            IDiagnosticsEventSink sink,
            LogLevel minimumLevel,
            string categoryName)
        {
            _sink = sink;
            _minimumLevel = minimumLevel;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _minimumLevel
                && logLevel != LogLevel.None
                && _categoryName.StartsWith("Lakona.", StringComparison.Ordinal);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var dimensions = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["event_id"] = eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };

            if (exception is not null)
            {
                dimensions["exception_type"] = exception.GetType().Name;
            }

            var activity = Activity.Current;
            _sink.Publish(new DiagnosticsEvent(
                DateTimeOffset.UtcNow,
                logLevel,
                _categoryName,
                "framework.log",
                "Lakona framework log captured.",
                activity?.TraceId.ToString(),
                activity?.SpanId.ToString(),
                dimensions));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
