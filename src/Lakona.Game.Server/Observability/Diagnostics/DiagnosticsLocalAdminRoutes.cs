using System.Text.Json;
using Lakona.Game.Server.LocalAdmin;

namespace Lakona.Game.Server.Observability.Diagnostics;

public static class DiagnosticsLocalAdminRoutes
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static IReadOnlyList<ILakonaLocalAdminRoute> Create(
        LakonaDiagnosticsSnapshotService snapshots,
        IDiagnosticsEventSink eventSink)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(eventSink);

        return
        [
            new SummaryRoute(snapshots),
            new EventsRoute(eventSink),
            new NetstatRoute(),
            new ActorsRoute(snapshots),
            new SessionsRoute(snapshots)
        ];
    }

    public static ILakonaLocalAdminRoute Summary(LakonaDiagnosticsSnapshotService snapshots)
    {
        return new SummaryRoute(snapshots);
    }

    public static ILakonaLocalAdminRoute Events(IDiagnosticsEventSink eventSink)
    {
        return new EventsRoute(eventSink);
    }

    public static ILakonaLocalAdminRoute Netstat()
    {
        return new NetstatRoute();
    }

    public static ILakonaLocalAdminRoute Actors(LakonaDiagnosticsSnapshotService snapshots)
    {
        return new ActorsRoute(snapshots);
    }

    public static ILakonaLocalAdminRoute Sessions(LakonaDiagnosticsSnapshotService snapshots)
    {
        return new SessionsRoute(snapshots);
    }

    public sealed class SummaryRoute : ILakonaLocalAdminRoute
    {
        private readonly LakonaDiagnosticsSnapshotService _snapshots;

        public SummaryRoute(LakonaDiagnosticsSnapshotService snapshots)
        {
            _snapshots = snapshots;
        }

        public string Method => "GET";

        public string Path => "/_lakona/diagnostics/summary";

        public async ValueTask<LakonaLocalAdminResponse> HandleAsync(
            LakonaLocalAdminRequest request,
            CancellationToken cancellationToken = default)
        {
            var summary = await _snapshots.CaptureSummaryAsync(cancellationToken).ConfigureAwait(false);
            return LakonaLocalAdminResponse.Json(summary, options: JsonOptions);
        }
    }

    public sealed class EventsRoute : ILakonaLocalAdminRoute
    {
        private readonly IDiagnosticsEventSink _eventSink;

        public EventsRoute(IDiagnosticsEventSink eventSink)
        {
            _eventSink = eventSink;
        }

        public string Method => "GET";

        public string Path => "/_lakona/diagnostics/events";

        public ValueTask<LakonaLocalAdminResponse> HandleAsync(
            LakonaLocalAdminRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<LakonaLocalAdminResponse>(
                LakonaLocalAdminResponse.Json(new { events = _eventSink.Snapshot(100) }, options: JsonOptions));
        }
    }

    public sealed class NetstatRoute : ILakonaLocalAdminRoute
    {
        public string Method => "GET";

        public string Path => "/_lakona/diagnostics/netstat";

        public ValueTask<LakonaLocalAdminResponse> HandleAsync(
            LakonaLocalAdminRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<LakonaLocalAdminResponse>(
                LakonaLocalAdminResponse.Json(
                    new
                    {
                        status = "unavailable",
                        explanation = "Transport and RPC counters are deferred until the netstat diagnostics provider is implemented."
                    },
                    options: JsonOptions));
        }
    }

    public sealed class ActorsRoute : DiagnosticsSectionRoute
    {
        public ActorsRoute(LakonaDiagnosticsSnapshotService snapshots)
            : base(snapshots, "/_lakona/diagnostics/actors", "actors", EmptyActorsSnapshot.Instance)
        {
        }
    }

    public sealed class SessionsRoute : DiagnosticsSectionRoute
    {
        public SessionsRoute(LakonaDiagnosticsSnapshotService snapshots)
            : base(snapshots, "/_lakona/diagnostics/sessions", "sessions", EmptySessionsSnapshot.Instance)
        {
        }
    }

    public abstract class DiagnosticsSectionRoute : ILakonaLocalAdminRoute
    {
        private readonly LakonaDiagnosticsSnapshotService _snapshots;
        private readonly string _section;
        private readonly object _empty;

        public DiagnosticsSectionRoute(
            LakonaDiagnosticsSnapshotService snapshots,
            string path,
            string section,
            object empty)
        {
            _snapshots = snapshots;
            Path = path;
            _section = section;
            _empty = empty;
        }

        public string Method => "GET";

        public string Path { get; }

        public async ValueTask<LakonaLocalAdminResponse> HandleAsync(
            LakonaLocalAdminRequest request,
            CancellationToken cancellationToken = default)
        {
            var summary = await _snapshots.CaptureSummaryAsync(cancellationToken).ConfigureAwait(false);
            var error = summary.Errors.FirstOrDefault(error => StringComparer.Ordinal.Equals(error.Provider, _section));
            if (error is not null)
            {
                return LakonaLocalAdminResponse.Json(
                    new DiagnosticsSectionErrorResponse(
                        "partial",
                        error.Provider,
                        error.ErrorType,
                        error.Message),
                    options: JsonOptions);
            }

            var value = summary.Sections.TryGetValue(_section, out var section) ? section : _empty;
            return LakonaLocalAdminResponse.Json(value, options: JsonOptions);
        }
    }

    private sealed record DiagnosticsSectionErrorResponse(
        string Status,
        string Provider,
        string ErrorType,
        string Message);

    private sealed record EmptyActorsSnapshot(IReadOnlyList<object> ActorTypes)
    {
        public static EmptyActorsSnapshot Instance { get; } = new([]);
    }

    private sealed record EmptySessionsSnapshot(
        int TotalSessions,
        int ActiveSessions,
        int ActiveConnections,
        int DisconnectedSessions,
        int TerminatedSessions,
        int ResumableSessions)
    {
        public static EmptySessionsSnapshot Instance { get; } = new(0, 0, 0, 0, 0, 0);
    }
}
