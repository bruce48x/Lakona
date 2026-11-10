namespace Lakona.Testing.Http;

// The generator and runtime scanner consume the same source cases. A valid
// endpoint is kept alongside invalid declarations so zero-endpoint rejection
// cannot mask a missed method diagnostic.
public static class HttpEndpointDiscoveryCases
{
    public static IEnumerable<object?[]> Cases()
    {
        yield return ["baseline", "", true, null];
        yield return ["private helper", "private string Helper() => \"ok\";", true, null];
        yield return ["private static generic helper", "private static T Helper<T>(T value) => value;", true, null];
        yield return ["internal helper", "internal string Helper() => \"ok\";", true, null];
        yield return ["internal static helper", "internal static string Helper() => \"ok\";", true, null];
        yield return ["property", "public string Status => \"ok\";", true, null];
        yield return ["disposal", "public void Dispose() {} public ValueTask DisposeAsync() => default;", true, null];
        yield return ["explicit disposal", "void IDisposable.Dispose() {} ValueTask IAsyncDisposable.DisposeAsync() => default;", true, null];
        yield return ["public missing attribute", "public ValueTask<LakonaHttpResponse> Helper(LakonaHttpCall call) => default;", true, "LAKONA20043"];
        yield return ["disposal overload is not cleanup", "public void Dispose(int value) {}", true, "LAKONA20043"];
        foreach (var declaration in new[]
        {
            "private ValueTask<LakonaHttpResponse> Invalid(LakonaHttpCall call) => default;",
            "internal ValueTask<LakonaHttpResponse> Invalid(LakonaHttpCall call) => default;",
            "public static ValueTask<LakonaHttpResponse> Invalid(LakonaHttpCall call) => default;",
            "public ValueTask<LakonaHttpResponse> Invalid<T>(LakonaHttpCall call) => default;",
            "public ValueTask<string> Invalid(LakonaHttpCall call) => default;",
            "public ValueTask<LakonaHttpResponse> Invalid(string call) => default;",
            "public void Dispose() {}",
            "public ValueTask DisposeAsync() => default;",
            "void IDisposable.Dispose() {}",
            "ValueTask IAsyncDisposable.DisposeAsync() => default;"
        })
            yield return [declaration, "[LakonaHttpEndpoint(\"POST\", \"/invalid\")] " + declaration, true, "LAKONA20043"];
        yield return ["annotated indexer", "public ValueTask<LakonaHttpResponse> this[LakonaHttpCall call] { [LakonaHttpEndpoint(\"POST\", \"/invalid\")] get => default; }", true, "LAKONA20043"];
        yield return ["empty service", "", false, "LAKONA20042"];
        yield return ["helpers only", "private string Helper() => \"ok\"; internal static void Other() {}", false, "LAKONA20042"];
        yield return ["cleanup only", "public void Dispose() {} public ValueTask DisposeAsync() => default;", false, "LAKONA20042"];
        yield return ["invalid endpoint only", "[LakonaHttpEndpoint(\"POST\", \"/invalid\")] private ValueTask<LakonaHttpResponse> Invalid(LakonaHttpCall call) => default;", false, "LAKONA20043"];
    }

    public static string Source(string members, bool hasEndpoint) => $$"""
        using System;
        using System.Threading.Tasks;
        using Lakona.Game.Server.Http;
        [LakonaHttpService("discovery")]
        public sealed class HttpService {{(members.Contains("IDisposable.") || members.Contains("IAsyncDisposable.") ? ": IDisposable, IAsyncDisposable" : "")}}
        {
            {{(hasEndpoint ? """
                [LakonaHttpEndpoint("post", "/discovery")]
                public ValueTask<LakonaHttpResponse> Handle(LakonaHttpCall call) =>
                    new ValueTask<LakonaHttpResponse>(LakonaHttpResponse.Text(call.Request.TraceIdentifier));
                """ : "")}}
            {{members}}
            {{(members.Contains("IDisposable.") && !members.Contains("IAsyncDisposable.") ? "ValueTask IAsyncDisposable.DisposeAsync() => default;" : "")}}
            {{(members.Contains("IAsyncDisposable.") && !members.Contains("IDisposable.") ? "void IDisposable.Dispose() {}" : "")}}
        }
        """;
}
