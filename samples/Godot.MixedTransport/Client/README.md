# Godot.MixedTransport Client

1. Open this folder with Godot 4.6 Mono.
2. Let Godot restore the C# solution, or run `dotnet restore Client.csproj`.
3. Build once so the generated RPC assemblies load.
4. Run `Main.tscn`.

Default connection settings:

- TCP login: `127.0.0.1:20000`
- KCP battle: login reply decides the `conv` and KCP port

Controls:

- `WASD` or arrow keys to move
