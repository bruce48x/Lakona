---
title: Use Lakona Observability
description: Learn how to use Lakona logs, readiness checks, and local admin diagnostics to debug a game server.
date: 2026-06-30T00:00:00+08:00
---

Observability is the part of Lakona that helps you answer simple operational
questions:

- Did the server load the configuration I expected?
- Why did startup fail?
- Are actors, sessions, and hotfix state healthy?
- What happened shortly before the error?

Lakona uses standard .NET diagnostics for the raw signals: `ILogger`, `Meter`,
and `ActivitySource`. On top of that, the game server package adds a small
local diagnostics surface that is easy to use while you are developing or
debugging a server.

<div class="article-hero-panel">
  <div>
    <p class="article-kicker">Debug loop</p>
    <p class="article-hero-copy">Start with the readiness check, keep framework logs readable, then open the loopback diagnostics endpoints when you need current runtime state.</p>
  </div>
  <div class="mini-tree" aria-label="Observability tools">
    <span>readiness-check</span>
    <span class="indent">configuration and startup guardrails</span>
    <span>console logs</span>
    <span class="indent">framework events while the server runs</span>
    <span>local admin diagnostics</span>
    <span class="indent">process, hotfix, actor, session, and event snapshots</span>
  </div>
</div>

## First Check Startup State

After building the generated server solution, run the readiness check:

<div class="command-card">
  <div class="command-label">Validate runtime configuration</div>
  <pre><code>dotnet run --project "Server/App/Server.App.csproj" -- --readiness-check</code></pre>
</div>

Use this before starting the real server. It validates the resolved runtime
state and reports guardrail errors before the server opens client-facing
listeners.

For machine-readable output, add `--json`:

<div class="command-card">
  <div class="command-label">JSON readiness output</div>
  <pre><code>dotnet run --project "Server/App/Server.App.csproj" -- --readiness-check --json</code></pre>
</div>

The check is the fastest way to catch mistakes such as unsafe local admin
binding, invalid log levels, invalid metrics paths, missing hotfix assemblies,
or exporter settings that were enabled without the required integration.

## Make Logs Useful

Lakona logging is configured under `Lakona:Observability:Logging`. By default,
framework logging is enabled, the minimum level is `Information`, and console
logging is enabled.

Use `Warning` for a quiet server. Use `Information` when you are trying to see
normal framework decisions. Use `Debug` only for a short debugging session.

```json
{
  "Lakona": {
    "Observability": {
      "Logging": {
        "Enabled": true,
        "MinimumLevel": "Information",
        "Categories": {
          "Lakona.Rpc": "Warning",
          "Lakona.Rpc.Transport": "Information",
          "Lakona.Game.Server": "Information",
          "Lakona.Game.Session": "Information",
          "Lakona.Game.Actor": "Warning",
          "Lakona.Game.Cluster": "Information",
          "Lakona.Game.Hotfix": "Information",
          "Lakona.Game.Observability": "Information"
        },
        "Console": {
          "Enabled": true,
          "Format": "Compact",
          "IncludeScopes": false
        }
      }
    }
  }
}
```

If logs are too noisy, raise the global `MinimumLevel` first. If you only need
one subsystem, keep the global level higher and lower one category, such as
`Lakona.Game.Hotfix` or `Lakona.Game.Session`.

## Open Local Diagnostics

In the `Development` profile, Lakona enables the local admin host by default on
`127.0.0.1:20090`. Start the server:

<div class="command-card">
  <div class="command-label">Run the server</div>
  <pre><code>dotnet run --project "Server/App/Server.App.csproj" --no-build</code></pre>
</div>

Then open the summary endpoint in a browser:

<div class="command-card">
  <div class="command-label">Open diagnostics summary</div>
  <pre><code>http://127.0.0.1:20090/_lakona/diagnostics/summary</code></pre>
</div>

Or fetch it from a terminal:

<div class="command-card">
  <div class="command-label">Fetch diagnostics summary</div>
  <pre><code>curl http://127.0.0.1:20090/_lakona/diagnostics/summary</code></pre>
</div>

The summary response contains a `status`, a timestamp, and named sections. The
default server includes process, hotfix, actor, and session diagnostics.

## Know the Endpoints

Use these endpoints during local development:

| Endpoint | What it is for |
| --- | --- |
| `/_lakona/diagnostics/summary` | One combined snapshot for the server. Start here. |
| `/_lakona/diagnostics/events` | Recent warnings and errors captured by the diagnostics event buffer. |
| `/_lakona/diagnostics/actors` | Actor type counts and aggregate mailbox state. |
| `/_lakona/diagnostics/sessions` | Active, disconnected, terminated, and resumable session counts. |
| `/_lakona/diagnostics/netstat` | Reserved for transport and RPC network counters. It currently reports that the provider is not implemented yet. |

The process section includes the process id, uptime, working set bytes, and GC
heap bytes. The hotfix section shows whether hotfix is available, which version
is loaded, how many methods and features are in the dispatch table, and the
last reload status.

## Use Events for Recent Failures

The diagnostics event buffer keeps recent framework events in memory. It is not
a log file and it is not durable storage. It is meant to answer "what just
happened?" while the process is still running.

```json
{
  "Lakona": {
    "Observability": {
      "Diagnostics": {
        "EventBuffer": {
          "Enabled": true,
          "Capacity": 1024,
          "MinimumLevel": "Warning"
        }
      }
    }
  }
}
```

Raise the capacity if you are debugging a burst of failures. Lower
`MinimumLevel` temporarily if you need more detail, then set it back when you
are done.

## Keep Production Locked Down

Production and Compose profiles disable the local admin host by default.
That is the right default. If you need it during an incident, keep it bound to
loopback:

```json
{
  "Lakona": {
    "Profile": "Production",
    "Observability": {
      "LocalAdmin": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 20090,
        "RequireLoopback": true
      }
    }
  }
}
```

Do not expose the local admin host directly to the public network. If you need
to inspect a remote server, prefer an SSH tunnel:

<div class="command-card">
  <div class="command-label">Tunnel local admin from a server</div>
  <pre><code>ssh -L 20090:127.0.0.1:20090 user@server</code></pre>
</div>

Then open `http://127.0.0.1:20090/_lakona/diagnostics/summary` on your local
machine.

`Lakona:Observability:Diagnostics:DetailEnabled` should stay `false` unless
you intentionally need detailed runtime state in a trusted local environment.
Lakona guardrails reject unsafe combinations, such as detailed diagnostics on a
non-loopback local admin endpoint.

## File Logs, Metrics, and Traces

The current core package exposes the configuration shape and validation for
file logging, Prometheus, and trace export, but the actual exporter packages are
integration points.

That means:

- Console logging works in the core package.
- Actor metrics are emitted through the .NET `Meter` named `Lakona.Game.Actor`.
- Actor traces are emitted through the .NET `ActivitySource` named
  `Lakona.Game.Actor`.
- File logging requires a registered file logging integration before
  `Lakona:Observability:Logging:File:Enabled` can be `true`.
- Prometheus serving requires a registered Prometheus endpoint integration
  before `Lakona:Observability:Metrics:Prometheus:Enabled` can be `true`.
- Trace export requires a registered OpenTelemetry integration before
  `Lakona:Observability:Tracing:Export:Enabled` can be `true`.

If you enable one of those exporters without registering its integration,
startup validation fails with a clear guardrail diagnostic. That is intentional:
it is better to fail early than to make you think telemetry is being exported
when it is not.

## A Simple Debugging Routine

When something looks wrong, use this order:

1. Run `--readiness-check` and fix any guardrail error first.
2. Start the server with console logs at `Information`.
3. Open `/_lakona/diagnostics/summary`.
4. Check `/_lakona/diagnostics/events` for recent warnings and errors.
5. If the problem is actor-related, open `/_lakona/diagnostics/actors`.
6. If the problem is player connection state, open
   `/_lakona/diagnostics/sessions`.
7. If the problem only happens in production, use a loopback local admin host
   through an SSH tunnel instead of exposing the endpoint.

This gives you a short path from "the server is broken" to a concrete runtime
fact you can act on.
