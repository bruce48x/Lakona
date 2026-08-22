---
title: Create Your First Game Project with Lakona Hub
description: Use Lakona Hub to create a Unity client, shared C# contracts, and a local Lakona game server, then open and run the generated game.
date: 2026-08-22T00:00:00+08:00
---

**Lakona Hub** gives you a guided desktop workflow for creating and opening a complete Lakona project.
The game client, shared C# contracts, and .NET server are created together, so you can explore one connected workspace and run the game locally.

Lakona Hub creates three connected parts:

- <code>Client/</code> is the Unity project you open and run.
- <code>Shared/</code> contains the C# messages used by Unity and the server.
- <code>Server/</code> is a .NET program that receives requests and applies game rules.

A server is simply another program in this workflow. Unity sends a login or
gameplay request to it; the server checks the rules and sends a result back.
By the end of this page, the generated Unity game will connect to a local
server, accept a player name, and show the first game scene.

<div class="article-hero-panel">
  <div>
    <p class="article-kicker">First runnable Unity project</p>
    <p class="article-hero-copy">Create a project in Lakona Hub, open the client and server, start the local server, then press Play in Unity.</p>
  </div>
  <div class="mini-tree" aria-label="Generated project structure">
    <span>MyGame/</span>
    <span class="indent">Client/ <em>Unity project</em></span>
    <span class="indent">Shared/ <em>messages and contracts</em></span>
    <span class="indent">Server/ <em>.NET game server</em></span>
  </div>
</div>

<div class="journey-steps" aria-label="Getting started steps">
  <div class="journey-step">
    <span class="step-number">1</span>
    <h3>Prepare</h3>
    <p>Have Hub, Unity, and a server IDE ready.</p>
  </div>
  <div class="journey-step">
    <span class="step-number">2</span>
    <h3>Create</h3>
    <p>Fill in one project form.</p>
  </div>
  <div class="journey-step">
    <span class="step-number">3</span>
    <h3>Open</h3>
    <p>Open the server and client.</p>
  </div>
  <div class="journey-step">
    <span class="step-number">4</span>
    <h3>Play</h3>
    <p>Run the local server, then Unity.</p>
  </div>
</div>

## Before you start

<div class="requirement-grid">
  <div class="requirement-card">
    <h3>Lakona Hub</h3>
    <p>Desktop application for your operating system.</p>
    <a href="https://github.com/bruce48x/Lakona/releases" target="_blank" rel="noopener">Download Lakona Hub</a>
  </div>
  <div class="requirement-card">
    <h3>Unity 2022 LTS</h3>
    <p>Set up the editor through Unity Hub. Unity Hub and Lakona Hub are different applications.</p>
  </div>
</div>

You will also need an IDE for the server, such as Rider, Visual Studio, or
Visual Studio Code. Lakona Hub can detect these applications and use the one
you select in Settings. If no compatible .NET 10 SDK is available, Hub can
manage a private copy from the Runtime settings.

## 1. Open and prepare Lakona Hub

1. Start Lakona Hub.

Open **Settings** in Lakona Hub before creating the project:

- Check the **Runtime** card. If it says that .NET 10 is required, choose
  **Install .NET SDK** and confirm.
- In **Server editor**, choose the IDE you want to use for the server.
- In **Detected tools**, choose **Refresh detection** if Unity or an IDE was
  added while Lakona Hub was already open.

Lakona Hub does not change your system PATH when it manages its private .NET
SDK. The SDK is used by Hub's project operations and can be managed from Hub.

## 2. Create a project in Lakona Hub

Go back to **Projects** and click **Create project**. The form shows every
setting at once. Use these values for the first Unity project:

<div class="command-card primary-command">
  <div class="command-label">Recommended first project settings</div>
  <pre><code>Project name:       MyGame
Client type:        Unity
Client version:     Unity 2022 LTS
Transport:          WebSocket
Serializer:          MemoryPack
NuGetForUnity source: OpenUPM</code></pre>
</div>

The fields mean:

| Field | What it controls | First-project choice |
| --- | --- | --- |
| Project name | The new folder name and the generated C# project names. | <code>MyGame</code> |
| Output location | The parent folder where Hub creates <code>MyGame/</code>. | Any empty folder you use for projects |
| Client type | Which client project Hub creates. | <code>Unity</code> |
| Client version | Which Unity editor the generated client expects. | <code>Unity 2022 LTS</code> |
| Transport | How Unity and the server send messages. | <code>WebSocket</code>; leave the default |
| Serializer | How C# messages are encoded for the connection. | <code>MemoryPack</code>; leave the default |
| NuGetForUnity source | Where Unity-side NuGet packages are obtained. | <code>OpenUPM</code>; leave the default |

Hub shows the final path under **Project will be created at**. Check that it
is the folder you want, then click **Create project**.

Project creation may take a few minutes. Hub creates the ordinary project files
and starts the selected Unity editor in a temporary restore project so it can
prepare and verify the Unity packages. Wait for the operation to finish before
opening the client.

When creation succeeds, <code>MyGame</code> appears in the **My projects** list.

## 3. Open the generated project

The project row has separate actions for the server and the client:

<div class="edit-grid">
  <div><strong>Server → Open</strong><p>Opens the <code>Server/</code> folder in the server IDE selected in Settings.</p></div>
  <div><strong>Client → Open</strong><p>Opens <code>Client/</code> in the matching Unity editor.</p></div>
  <div><strong>More → Open project folder</strong><p>Opens the top-level <code>MyGame/</code> folder in the file manager.</p></div>
  <div><strong>Package</strong><p>Creates a deployable server or Hotfix archive. You do not need this for the first local run.</p></div>
</div>

If a button is disabled, Hub has not found a matching application. Go to
**Settings → Detected tools**, refresh the list, or use **Add application** to
select the editor executable manually.

Click **Open** in the Client column and wait for Unity to import packages and
compile scripts. The generated game scene is:

<code>Client/Assets/Scenes/Game.unity</code>

Click **Open** in the Server column as well. This opens the server files; it
does not start the server yet.

## 4. Start the local server

Lakona Hub manages the project and opens the right tools. It does not build,
start, stop, or supervise the server process. Start the server from the IDE
Hub opened, or from a second terminal in the <code>MyGame/</code> folder:

<div class="command-card">
  <div class="command-label">Build the generated server once</div>
  <pre><code>dotnet build "Server/Server.slnx"</code></pre>
</div>

After the build succeeds, start the server and leave this terminal open:

<div class="command-card primary-command">
  <div class="command-label">Run the local server</div>
  <pre><code>dotnet run --project "Server/App/Server.App.csproj" --no-build</code></pre>
</div>

The default WebSocket connection is
<code>ws://127.0.0.1:20000/ws</code>. The generated server also exposes a local
readiness check at <code>http://127.0.0.1:20080/_lakona/health/ready</code>.
From another terminal, you can check it with:

<div class="command-card">
  <div class="command-label">Check that the server is ready</div>
  <pre><code>curl http://127.0.0.1:20080/_lakona/health/ready</code></pre>
</div>

A JSON response means the local server is listening. If the check cannot
connect, look at the terminal running <code>dotnet run</code> and make sure it
is still open.

## 5. Run the Unity client

With the server still running:

1. Return to the generated Unity project.
2. Open <code>Assets/Scenes/Game.unity</code> if Unity has not opened it.
3. Press **Play**.
4. Enter a name and click **PLAY NOW**.

Unity sends the login request to the server. The server checks the name and
returns the result; after a successful login, the generated game scene appears.

<div class="notice-card">
  <strong>Keep both programs running</strong>
  <p>Unity is the client process. <code>dotnet run</code> is the server process. Stop the server with <code>Ctrl+C</code> when you finish testing.</p>
</div>

## What to edit next

The generated project is made from ordinary files. Start with the folder that
owns the behavior you want to change:

<div class="edit-grid">
  <div><strong>Client/</strong><p>Unity scenes, UI, input, and client-side connection code.</p></div>
  <div><strong>Shared/Contracts/</strong><p>Request, response, and notification types that Unity and the server both use.</p></div>
  <div><strong>Server/Hotfix/</strong><p>Game rules such as validating a player name or changing what happens after login.</p></div>
  <div><strong>Server/App/</strong><p>Server startup and local configuration, such as the listening port.</p></div>
</div>

For example, to add a field to a login request, add it under
<code>Shared/Contracts/Game/</code>, rebuild the server, and then read the field
from the corresponding code in <code>Server/Hotfix/</code>. Both Unity and the
server compile against the same C# type, so they agree on the data shape.

After changing shared or server code, rebuild the hotfix output before starting
the server again:

<div class="command-card">
  <div class="command-label">Refresh server game code</div>
  <pre><code>dotnet build "Server/Hotfix/Server.Hotfix.csproj"</code></pre>
</div>

## Use Hub for your next steps

<div class="engine-grid">
  <div class="engine-card">
    <h3>Open an existing project</h3>
    <p>Use <strong>Import existing project</strong> on the Projects page and select the project's top-level folder. Hub reads its structure and adds it to the list; it does not add hidden management files or change the project.</p>
  </div>
  <div class="engine-card">
    <h3>Package a server</h3>
    <p>Click <strong>Package</strong> beside a project, choose <strong>Deployable server package</strong>, select Release and a runtime such as <code>win-x64</code> or <code>linux-x64</code>, then choose <strong>Build package</strong>.</p>
  </div>
  <div class="engine-card">
    <h3>Package a Hotfix</h3>
    <p>Choose <strong>Hotfix package</strong> in the same dialog when you only changed <code>Server/Hotfix/</code>. Hotfix packages do not need a runtime selection.</p>
  </div>
</div>

Hub opens the output folder after a successful package. Packaging is a local
file operation; uploading the archive or deploying it to a remote machine is a
separate operation.

<div class="outcome-grid">
  <div>
    <h3>Hub creates</h3>
    <p>A complete Unity, shared-contract, and server project in one folder.</p>
  </div>
  <div>
    <h3>Hub opens</h3>
    <p>The matching Unity editor and the server IDE you selected.</p>
  </div>
  <div>
    <h3>You run</h3>
    <p>The server and Unity as two local programs while you develop.</p>
  </div>
</div>

## Prefer the command line?

Lakona.Tool is the command-line path for creating and packaging Lakona projects.
It is useful when you prefer a terminal, want a repeatable setup command, or
need to create a project from a script or CI job.

Install the tool once:

<div class="command-card">
  <div class="command-label">Install Lakona.Tool</div>
  <pre><code>dotnet tool install --global Lakona.Tool</code></pre>
</div>

From the directory where you keep your projects, create a Unity project with
the same settings used in this tutorial:

<div class="command-card">
  <div class="command-label">Create a Unity project from the terminal</div>
  <pre><code>lakona-tool new --name MyGame --client-engine unity --client-engine-version 2022 --transport websocket --serializer memorypack --nugetforunity-source openupm</code></pre>
</div>

If you would rather answer questions one at a time, use the interactive form:

<div class="command-card">
  <div class="command-label">Create a project interactively</div>
  <pre><code>lakona-tool new</code></pre>
</div>

When the command finishes, open <code>MyGame/Client/</code> in Unity and follow
**4. Start the local server** and **5. Run the Unity client** above. The CLI
creates the project; you still run the server and Unity as two local programs
while developing.

To create a deployable Linux server package from the project root, run:

<div class="command-card">
  <div class="command-label">Package the server</div>
  <pre><code>lakona-tool server pack --runtime linux-x64 --configuration Release</code></pre>
</div>

The package is written to <code>Server/Build/</code> by default. Keep all
options in the command when using it in CI so it can run without prompts.

## From a local game to a real cluster

Your first project is the starting point. Continue with
[Agar: from a local game to a distributed cluster](../agar/) to see the same
game grow into a complete system with separate application, database, cache,
and monitoring nodes, plus OpenTelemetry-based observability.
