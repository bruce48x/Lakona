# Testing

Tests must protect runtime contracts rather than mirror implementation details.

| Area | Required coverage when changed |
| --- | --- |
| Actor messaging | Dispatch, responses, timeout, response validation, dead letters |
| Actor mailbox | Ordering, non-concurrency, backpressure, stop drain, metrics |
| Actor lifecycle | Startup, graceful stop, rollback, disposal |
| Actor tooling | Generated extensions, clients, source shape, diagnostics |
| RPC runtime | Encoding, dispatch, cleanup, admission, protocol limits |
| Transports | Cancellation, disconnect, backpressure, framing, security |
| Serializers | Roundtrips, compatibility, failure behavior |
| Starter/tooling | CLI, dependency planning, layout, template output |
| Game sessions | Resume, cleanup, callbacks, token validation, reliable push |
| Cluster | Lookup, expiry, dispatch, stale registration, node restart |
| Hotfix | Dispatch, reload, unload, watching, accessors, fallback |
| Unity samples | EditMode or PlayMode coverage for runtime behavior and shape |

Unity tests use NUnit and Unity Test Framework. Use `[UnityTest]` with
`IEnumerator` for asynchronous Unity tests and alias assertions with
`using NUnitAssert = NUnit.Framework.Assert;`.

Source-scan tests that read `src/**` must be updated when source files move or
are renamed.

For solution runs that exceed local tool timeouts, execute test projects
sequentially:

```powershell
$projects = Get-ChildItem tests -Recurse -Filter '*.csproj' | Sort-Object FullName
foreach ($project in $projects) {
  dotnet test $project.FullName --no-build
  if ($LASTEXITCODE -ne 0) { throw "Tests failed: $($project.FullName)" }
}
```
