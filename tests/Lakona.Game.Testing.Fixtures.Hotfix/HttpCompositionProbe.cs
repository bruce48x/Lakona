using Lakona.Game.Server.Http;

namespace Lakona.Game.Testing.Fixtures.Hotfix;

[LakonaHttpService("probe")]
public sealed class HttpCompositionProbe
{
    [LakonaHttpEndpoint("GET", "/probe")]
    public ValueTask<LakonaHttpResponse> Get(LakonaHttpCall call) =>
        new(LakonaHttpResponse.Text("ok"));
}

[LakonaHttpService("disabled")]
public sealed class DisabledHttpCompositionProbe
{
    public DisabledHttpCompositionProbe(CompositionProbe probe) =>
        throw new InvalidOperationException("Disabled HTTP module was activated.");

    [LakonaHttpEndpoint("GET", "/disabled")]
    public ValueTask<LakonaHttpResponse> Get(LakonaHttpCall call) =>
        new(LakonaHttpResponse.Text("unexpected"));
}
