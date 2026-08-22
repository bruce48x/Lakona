---
title: See Lakona in Action with Agar
description: Run a real Unity multiplayer game locally, then create a complete nine-node development cluster with Lakona.
date: 2026-08-22T01:00:00+08:00
---

Agar is a small, playable Unity game that shows what a Lakona project looks
like after it grows beyond a hello-world example. You can log in as a guest,
enter matchmaking, play a realtime battle, and inspect the server code behind
it.

This article follows the same path as the project: start small, split the
server roles, create the complete cluster, then deploy and observe the game.

<div class="article-hero-panel">
  <div>
    <p class="article-kicker">One game, two environments</p>
    <p class="article-hero-copy">Play Agar on your workstation first, then use the same project to explore a distributed cluster and its observability stack.</p>
  </div>
  <div class="mini-tree" aria-label="Agar development path">
    <span>Agar</span>
    <span class="indent">Unity client <em>play the game</em></span>
    <span class="indent">server-ctl.ps1 <em>local Docker run</em></span>
    <span class="indent">manage.ps1 <em>nine-node cluster</em></span>
  </div>
</div>

<div class="journey-steps" aria-label="Agar development steps">
  <div class="journey-step">
    <span class="step-number">1</span>
    <h3>Start</h3>
    <p>Bring up a local server.</p>
  </div>
  <div class="journey-step">
    <span class="step-number">2</span>
    <h3>Play</h3>
    <p>Connect the Unity client.</p>
  </div>
  <div class="journey-step">
    <span class="step-number">3</span>
    <h3>Cluster</h3>
    <p>Create the nine-node environment.</p>
  </div>
  <div class="journey-step">
    <span class="step-number">4</span>
    <h3>Operate</h3>
    <p>Deploy and observe the game.</p>
  </div>
</div>

## Before you start

For the local game, prepare:

- A checkout of the [Lakona repository](https://github.com/bruce48x/Lakona).
- Docker Desktop with the Docker engine running.
- PowerShell 7. <code>server-ctl.ps1</code> does not target Windows PowerShell 5.1.
- Unity 2022 LTS if you want to run the client.

The nine-node local cluster additionally uses Vagrant and VirtualBox. Reserve
at least **24 GiB of physical memory**; the VMs use about 17 GiB before
Windows, Docker, Unity, and other tools are counted.

## 1. Start Agar locally

Open a PowerShell 7 terminal at the sample folder:

<div class="command-card">
  <div class="command-label">Enter the Agar sample</div>
  <pre><code>cd path\to\Lakona\samples\Game.Unity.Agar</code></pre>
</div>

Start a single-node environment for the quickest first run:

<div class="command-card primary-command">
  <div class="command-label">Start the quick local environment</div>
  <pre><code>pwsh -NoProfile -File .\server-ctl.ps1 start -Topology single</code></pre>
</div>

The script builds the Agar images, starts PostgreSQL, Redis, and the game
server, then waits for the server to report ready. When it finishes, the
client-facing endpoints are:

| Endpoint | Address | Purpose |
| --- | --- | --- |
| Gateway | <code>ws://127.0.0.1:20000/ws</code> | Login, matchmaking, and normal RPC calls |
| Battle | <code>udp://127.0.0.1:20001</code> | Realtime battle traffic |
| Operations | <code>http://127.0.0.1:21000</code> | Local operations and readiness checks |

Once the single-node game works, start the default three-node topology:

<div class="command-card">
  <div class="command-label">Split the local server into three nodes</div>
  <pre><code>pwsh -NoProfile -File .\server-ctl.ps1 start</code></pre>
</div>

This starts <code>data-1</code>, <code>gateway-1</code>, and
<code>battle-1</code>, alongside PostgreSQL and Redis. The client still uses
the same gateway and battle ports, but the application responsibilities are
now separated across three Lakona nodes.

Inspect or stop the environment with the same script:

<div class="command-card">
  <div class="command-label">Inspect or stop the local environment</div>
  <pre><code>pwsh -NoProfile -File .\server-ctl.ps1 status
pwsh -NoProfile -File .\server-ctl.ps1 logs -NoFollow
pwsh -NoProfile -File .\server-ctl.ps1 stop</code></pre>
</div>

Starting one topology stops the other first, so switching between
<code>single</code> and the default three-node layout does not leave both
trying to use the same ports.

## 2. Play Agar in Unity

Leave the server running and open this folder in Unity 2022 LTS:

<code>samples/Game.Unity.Agar/Client</code>

Open <code>Assets/Scenes/Gameplay.unity</code>, press **Play**, and choose:

1. Choose **Multiplayer**.
2. Choose **Guest Login**.
3. Start matchmaking from the multiplayer lobby.
4. When the match starts, use **W/A/S/D** to move, eat food, and grow.

The client now exercises a complete multiplayer path: it connects to the
gateway, creates a temporary account, waits in the matchmaking queue, receives
match information, opens the realtime battle connection, and renders the game
state.

The mode menu also has a local single-player option. It is useful for looking
at the gameplay loop, but it does not use the server; multiplayer is the path
that demonstrates Lakona.

## 3. Create the complete cluster

The three-node Docker topology is enough for a fast local test. The separate
[lakona-agar-dev-cluster](https://github.com/bruce48x/lakona-agar-dev-cluster)
repository adds the machines around it: an Ansible controller, dedicated data
services, and a central observability stack.

This guide uses its local Vagrant platform. It creates nine Debian VMs on a
private network, so the layout can be recreated without manually configuring
nine machines.

Clone the cluster controller:

<div class="command-card">
  <div class="command-label">Get the cluster controller</div>
  <pre><code>git clone https://github.com/bruce48x/Lakona-agar-dev-cluster.git
cd .\Lakona-agar-dev-cluster</code></pre>
</div>

On Windows, create and initialize the cluster with PowerShell 7:

<div class="command-card primary-command">
  <div class="command-label">Create the nine-node environment</div>
  <pre><code>.\host\manage.ps1 up</code></pre>
</div>

On macOS, invoke the same script through PowerShell 7:

<div class="command-card">
  <div class="command-label">macOS equivalent</div>
  <pre><code>pwsh ./host/manage.ps1 up</code></pre>
</div>

The first run creates or starts the VMs, configures SSH, initializes Ansible,
and runs a health check. Running <code>up</code> again is safe; it brings the
environment back to the expected state.

The important boundary is this: <code>up</code> creates infrastructure, but it
does not deploy an Agar build. The cluster is ready for a package in the next
step.

The application nodes handle the game, the dedicated data nodes hold durable
and shared data, and <code>monitoring-1</code> receives telemetry for the whole
cluster. <code>ansible-1</code> is the control plane; it configures and operates
the other machines but does not carry game traffic.

<figure class="topology-figure">
  <img src="../../images/agar-cluster-topology.svg" alt="Agar topology showing the Unity client outside the nine-node cluster, with traffic entering gateway-1 and battle-1, application nodes using PostgreSQL and Redis, and OpenTelemetry flowing to monitoring-1 and Grafana.">
  <figcaption>The local Vagrant cluster separates game traffic, data, deployment, and observability.</figcaption>
</figure>

## 4. Deploy an Agar server package

Build a **full Linux x64 server package** with Lakona Hub or
<code>lakona-tool server pack</code>. For example:

<code>C:\packages\Server.Full-Agar1-20260822-120000Z-linux-x64.zip</code>

Give the package explicitly to the host controller:

<div class="command-card primary-command">
  <div class="command-label">Deploy the full Agar release</div>
  <pre><code>.\host\manage.ps1 start -ArtifactPath C:\packages\Server.Full-Agar1-20260822-120000Z-linux-x64.zip</code></pre>
</div>

The controller copies the archive to the application nodes, validates it,
starts the release, and checks readiness.

The local Vagrant network forwards the game ports to the host:

| Host port | Destination | Purpose |
| --- | --- | --- |
| TCP <code>20000</code> | <code>server-2:20000</code> | Gateway WebSocket |
| UDP <code>20001</code> | <code>server-3:20001</code> | Battle KCP |

Point the Unity client at <code>127.0.0.1</code> while this local cluster is
running. Grafana is available at
<code>http://192.168.56.21:3000</code>.

## 5. Follow the telemetry

The diagram above shows the telemetry path: each application node sends
metrics, traces, and logs to a node-local Collector, which forwards OTLP data
to <code>monitoring-1</code>. Prometheus, Tempo, and Loki store the three data
types; Grafana is the place to explore them together.

While you test the deployed game, use the host controller for the operational
view:

<div class="command-card">
  <div class="command-label">Inspect the running cluster</div>
  <pre><code>.\host\manage.ps1 vm-status
.\host\manage.ps1 status
.\host\manage.ps1 logs -Limit server-2 -Lines 50 -Boot</code></pre>
</div>

When the cluster is no longer needed, stop it without deleting its disks:

<div class="command-card">
  <div class="command-label">Pause the environment</div>
  <pre><code>.\host\manage.ps1 halt</code></pre>
</div>

When you want to remove the VMs and their PostgreSQL and observability data,
use <code>destroy</code>.

## What to inspect in the code

The sample keeps the client, shared rules, and server responsibilities easy to
find:

- <code>Client/Assets/Scripts/Gameplay</code> contains the Unity game flow and network session.
- <code>Shared/Gameplay</code> contains the gameplay rules and deterministic simulation shared with the client.
- <code>Server/App</code> contains the stable server host and actor state shells.
- <code>Server/Hotfix</code> contains the services, matchmaking loop, battle callbacks, and game behavior.
- <code>server-ctl.ps1</code> manages the local Docker environment.

Agar's progression is the point: **play locally, split the nodes, deploy the
same game, and inspect its behavior through telemetry**.

For the sample's full design and test commands, see the
[Game.Unity.Agar README](https://github.com/bruce48x/Lakona/tree/main/samples/Game.Unity.Agar).
For the cluster controller, host operations, and cloud adapter, see the
[lakona-agar-dev-cluster repository](https://github.com/bruce48x/Lakona-agar-dev-cluster).
