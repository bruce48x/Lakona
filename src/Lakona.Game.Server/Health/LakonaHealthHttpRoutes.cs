using Lakona.Game.Server.Guardrails;

namespace Lakona.Game.Server.Health;

public static class LakonaHealthHttpRoutes
{
    public static ILakonaHealthHttpRoute Live()
    {
        return new LiveRoute();
    }

    public static ILakonaHealthHttpRoute Ready(LakonaGameReadinessEvaluator evaluator)
    {
        return new ReadyRoute(evaluator);
    }

    public sealed class LiveRoute : ILakonaHealthHttpRoute
    {
        public string Method => "GET";

        public string Path => "/_lakona/health/live";

        public ValueTask<LakonaHealthHttpResponse> HandleAsync(
            LakonaHealthHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<LakonaHealthHttpResponse>(
                LakonaHealthHttpResponse.Json(new { status = "ok" }));
        }
    }

    public sealed class ReadyRoute : ILakonaHealthHttpRoute
    {
        private readonly LakonaGameReadinessEvaluator _evaluator;

        public ReadyRoute(LakonaGameReadinessEvaluator evaluator)
        {
            _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        }

        public string Method => "GET";

        public string Path => "/_lakona/health/ready";

        public ValueTask<LakonaHealthHttpResponse> HandleAsync(
            LakonaHealthHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = _evaluator.Evaluate();
            return new ValueTask<LakonaHealthHttpResponse>(
                LakonaHealthHttpResponse.Json(
                    new
                    {
                        status = snapshot.Succeeded ? "ready" : "not_ready",
                        succeeded = snapshot.Succeeded,
                        diagnostics = snapshot.Diagnostics.Select(static diagnostic => new
                        {
                            code = diagnostic.Code,
                            severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                            message = diagnostic.Message,
                            repair = diagnostic.Repair
                        })
                    },
                    snapshot.Succeeded ? 200 : 503));
        }
    }
}
