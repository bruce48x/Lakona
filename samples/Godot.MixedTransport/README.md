# Godot.MixedTransport

Godot client sample that uses two transports in one flow:

- `TCP` for login/authentication
- `KCP` for battle session traffic

The client starts on a login screen. After login succeeds, the server returns a `token` plus a pre-assigned KCP `conv`. The client reconnects to the battle server with that `conv` and enters a lightweight `agar.io`-style arena.

## Structure

- `Shared`: contracts and DTOs used by both server and Godot client
- `Server`: .NET 10 dual-endpoint sample server
- `Client`: Godot 4.6 C# client

## Quick Start

Generate binders and build the server:

```powershell
pwsh -NoProfile -File .\scripts\sample.ps1 -Sample Godot.MixedTransport
```

Run the server:

```powershell
pwsh -NoProfile -File .\scripts\sample.ps1 -Sample Godot.MixedTransport -Run
```

Default ports:

- TCP login: `20000`
- KCP battle: `20001`

Open `samples/Godot.MixedTransport/Client` in Godot 4.6 Mono, build once, then run `Main.tscn`.
