---
title: Create and Run a Lakona Project
description: Install Lakona.Tool, create a new Unity or Godot game server project, validate it, and run the generated server.
date: 2026-06-18T00:00:00+08:00
---

Lakona is designed to start as a complete local workspace, not a pile of
packages that you have to assemble by hand. One command can generate a server,
shared contracts, hotfixable game logic, and a client project for Unity, Godot,
or both engines over time.

<div class="article-hero-panel">
  <div>
    <p class="article-kicker">First runnable project</p>
    <p class="article-hero-copy">Create a Lakona workspace, build the server and hotfix code, validate the runtime configuration, then connect from Unity or Godot.</p>
  </div>
  <div class="mini-tree" aria-label="Generated project structure">
    <span>MyGame/</span>
    <span class="indent">Shared/ <em>contracts and DTOs</em></span>
    <span class="indent">Server/ <em>host and hotfix code</em></span>
    <span class="indent">Client/ <em>Unity or Godot project</em></span>
  </div>
</div>

<div class="journey-steps" aria-label="Getting started steps">
  <div class="journey-step">
    <span class="step-number">1</span>
    <h3>Install</h3>
    <p>.NET 10 SDK and Lakona.Tool.</p>
  </div>
  <div class="journey-step">
    <span class="step-number">2</span>
    <h3>Create</h3>
    <p>Generate a Unity or Godot project.</p>
  </div>
  <div class="journey-step">
    <span class="step-number">3</span>
    <h3>Build</h3>
    <p>Compile server and hotfix first.</p>
  </div>
  <div class="journey-step">
    <span class="step-number">4</span>
    <h3>Run</h3>
    <p>Start the server and open the client.</p>
  </div>
</div>

## Prerequisites

<div class="requirement-grid">
  <div class="requirement-card">
    <h3>.NET SDK 10.0+</h3>
    <p>Required for the generated game server. Older SDKs cannot build the default Lakona server projects.</p>
    <a href="https://dotnet.microsoft.com/en-us/download/dotnet/10.0" target="_blank" rel="noopener">Download from Microsoft</a>
  </div>
  <div class="requirement-card">
    <h3>Unity or Godot</h3>
    <p>Install Unity 2022 LTS for Unity projects, or Godot 4.x .NET for Godot projects.</p>
  </div>
</div>

## Install the CLI

Install `Lakona.Tool` as a global .NET tool:

<div class="command-card">
  <div class="command-label">Install Lakona.Tool</div>
  <pre><code>dotnet tool install -g Lakona.Tool</code></pre>
</div>

If you already installed it before, update it with:

<div class="command-card">
  <div class="command-label">Update an existing install</div>
  <pre><code>dotnet tool update -g Lakona.Tool</code></pre>
</div>

## Create a Project

Choose the client engine first. The server shape stays the same.

<div class="engine-grid">
  <div class="engine-card">
    <h3>Unity starter</h3>
    <p>Use this when the game client is a Unity 2022 LTS project.</p>
    <pre><code>lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack</code></pre>
  </div>
  <div class="engine-card">
    <h3>Godot starter</h3>
    <p>Use this when the game client is a Godot 4.x .NET project.</p>
    <pre><code>lakona-tool new --name MyGame --client-engine godot --transport kcp --serializer memorypack</code></pre>
  </div>
</div>

<div class="workspace-map">
  <div>
    <p class="article-kicker">Generated workspace</p>
    <p>Every starter keeps shared contracts, stable server code, hotfixable behavior, and the selected game client in one local workspace.</p>
  </div>
  <pre class="file-tree"><code>MyGame/
  Shared/        RPC contracts, DTOs, callbacks, shared protocol types
  Server/
    App/         Stable host, actors, lifecycle handlers, configuration
    Hotfix/      Replaceable gameplay behavior
  Client/        Unity or Godot client project</code></pre>
</div>

## Build the Server

Move into the generated project and build the server solution first:

<div class="command-card primary-command">
  <div class="command-label">First command inside the new project</div>
  <pre><code>cd MyGame
dotnet build "Server/Server.slnx"</code></pre>
</div>

This is the first command to run inside a new Lakona project. It ensures both
the stable server host and the hotfix project compile before you ask the server
to inspect its runtime configuration.

After changing `Shared/` or `Server/Hotfix/`, rebuild the hotfix project to
refresh the development hotfix output used by the running server:

<div class="command-card">
  <div class="command-label">Refresh local hotfix output</div>
  <pre><code>dotnet build "Server/Hotfix/Server.Hotfix.csproj"</code></pre>
</div>

## Start the Server

After the server solution and hotfix output build, start the server:

<div class="command-card">
  <div class="command-label">Start the local server</div>
  <pre><code>dotnet run --project "Server/App/Server.App.csproj" --no-build</code></pre>
</div>

The default endpoint listens on `127.0.0.1:20000`. WebSocket projects use
`ws://127.0.0.1:20000/ws`; TCP and KCP projects use their selected transport on
port `20000`.

## Check Readiness

With the server running, request the readiness endpoint from another terminal:

<div class="command-card">
  <div class="command-label">Inspect generated runtime state</div>
  <pre><code>curl http://127.0.0.1:20080/_lakona/health/ready</code></pre>
</div>

The readiness response contains JSON guardrail diagnostics when configuration,
hotfix output, endpoints, or observability settings are not ready. Liveness is
available at `http://127.0.0.1:20080/_lakona/health/live`.

## Run the Client

<div class="engine-grid">
  <div class="engine-card">
    <h3>Unity first launch</h3>
    <p>Open the generated <code>Client/</code> project in Unity 2022 LTS and let NuGetForUnity restore the packages from <code>Client/Assets/packages.config</code>.</p>
    <div class="notice-card">
      <strong>Package restore</strong>
      <p>If Unity is still compiling or restoring packages, wait for the restore to finish before running the generated login scene. Reopen the project only if Unity asks for a reload after package import.</p>
    </div>
  </div>
  <div class="engine-card">
    <h3>Godot first launch</h3>
    <p>Open the generated <code>Client/</code> project in Godot and run the generated login scene.</p>
  </div>
</div>

That gives you the shortest end-to-end path: generated shared contracts,
running server, selected transport, and a client connecting to the local
endpoint.

## What to Edit First

Start with the generated vertical slice, then change one layer at a time.

<div class="edit-grid">
  <div><strong>Shared/Contracts/</strong><p>Change RPC contracts, callbacks, DTOs, and shared stable types.</p></div>
  <div><strong>Server/Hotfix/</strong><p>Change gameplay rules or service behavior that should be replaceable.</p></div>
  <div><strong>Server/App/</strong><p>Change stable orchestration, actor state, host binding, or configuration.</p></div>
  <div><strong>Client/</strong><p>Change the selected engine UI and client session flow.</p></div>
</div>

Check `/_lakona/health/ready` again after changing runtime configuration. It is
the fastest way to catch missing endpoints, invalid service exposure, and unsafe
server startup state before the game reaches players.

<div class="outcome-grid">
  <div>
    <h3>Shared contracts</h3>
    <p>Server and client compile the same API surface.</p>
  </div>
  <div>
    <h3>Server and hotfix built</h3>
    <p>The stable host and replaceable gameplay assembly are ready.</p>
  </div>
  <div>
    <h3>Client ready to connect</h3>
    <p>Unity or Godot can point at the local Lakona endpoint.</p>
  </div>
</div>
