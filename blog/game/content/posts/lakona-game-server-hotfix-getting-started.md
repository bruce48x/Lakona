---
title: "Server Hotfix In Lakona.Game: A Step By Step Guide"
date: 2026-05-29T12:30:00+08:00
summary: "Learn how to split stable state and reloadable game rules, load a hotfix DLL, and change server logic without losing live state."
tags:
  - lakona-game
  - hotfix
  - dotnet
  - server
  - tutorial
categories:
  - Tutorial
---

This guide is for your first time using Lakona.Game server hotfix.

The goal is simple:

> keep the live state in memory, but replace the game rules at runtime.

For example, a player is already online. The server has this data in memory:

- level
- experience
- room state
- match state

You want to change the rule for "how much exp is needed to level up" without restarting the server and without losing that player state.

That is what the server hotfix module is for.

## The Idea In One Minute

Lakona.Game uses this model:

```txt
stable state + reloadable rules
```

Stable state stays in the normal server assembly. It owns the long-lived data.

Hotfix code lives in another assembly. It owns replaceable rules.

When you reload, Lakona.Game does not create a new player object. It loads a new hotfix DLL and swaps the rule table. Existing state objects keep living.

Think of it like this:

```txt
PlayerRuntime object stays alive
AddExp rule changes
```

The rule can change. The player data stays.

## What You Will Build

In this guide, you will build a small example:

1. Create a stable `PlayerRuntime` state class.
2. Mark it as hotfix state.
3. Add a stable wrapper method named `AddExp`.
4. Create a hotfix project.
5. Write a hotfix system method.
6. Load the hotfix DLL in the server.
7. Change the hotfix rule and reload it.

The code is small on purpose. After you understand it, the Agar sample uses the same idea for `ArenaSimulation`.

## Step 1: Add The Packages

You normally use three hotfix packages.

In the project that owns stable state, add:

```xml
<ItemGroup>
  <PackageReference Include="Lakona.Game.Server.Hotfix.Abstractions" Version="0.1.0" />
  <PackageReference Include="Lakona.Game.Server.Hotfix" Version="0.1.0" />
  <PackageReference Include="Lakona.Game.Server.Hotfix.Generators" Version="0.1.0"
                    PrivateAssets="all" />
</ItemGroup>
```

In the hotfix project, add only the small contract package and a reference to the stable state project:

```xml
<ItemGroup>
  <PackageReference Include="Lakona.Game.Server.Hotfix.Abstractions" Version="0.1.0" />
  <ProjectReference Include="../Game.Stable/Game.Stable.csproj" />
</ItemGroup>
```

In the server host project, add the runtime package:

```xml
<ItemGroup>
  <PackageReference Include="Lakona.Game.Server.Hotfix" Version="0.1.0" />
</ItemGroup>
```

If one of your stable projects is also used by Unity or Godot, keep server-only hotfix references under a server build condition, or put hotfix-ready state in a server-side stable project. The client does not need to load server hotfix DLLs.

## Step 2: Create Stable State

Put long-lived data in a stable assembly.

```csharp
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;

[HotfixState]
public sealed partial class PlayerRuntime
{
    private int level = 1;
    private int exp;

    public int Level => level;
    public int Exp => exp;

    public void AddExp(int amount)
    {
        HotfixDispatch.Invoke<PlayerRuntime>(
            nameof(AddExp),
            this,
            new[] { typeof(int) },
            new object?[] { amount });
    }

    public void ApplyProgressFromHotfix(int newLevel, int newExp)
    {
        level = newLevel;
        exp = newExp;
    }
}
```

Important details:

- The class is `partial`.
- `[HotfixState]` tells the generator to create helper methods for private fields.
- `AddExp` is stable. It stays in the normal server assembly.
- The real rule will live in the hotfix assembly.
- `ApplyProgressFromHotfix` is the stable write-back point. Keep these write-back methods small and clear.

After build, the generator creates hidden helper methods such as:

```csharp
public int __hotfix_exp()
public int __hotfix_level()
```

In this first version, these helpers are public because the hotfix DLL must call them from another assembly. Treat `[FriendOf]` as a clear sign of intent, not as a security wall.

The generated helpers read private fields. They do not generate setter methods. If hotfix code needs to change stable state, provide a small stable method such as `ApplyProgressFromHotfix`.

## Step 3: Create The Hotfix Project

Create a new class library, for example:

```txt
Server/Hotfix/Game.Hotfix.csproj
```

The hotfix project should reference:

- `Lakona.Game.Server.Hotfix.Abstractions`
- the stable state project that contains `PlayerRuntime`

Then add a system class:

```csharp
using Lakona.Game.Server.Hotfix.Abstractions;

[FriendOf(typeof(PlayerRuntime))]
[HotfixSystemOf(typeof(PlayerRuntime))]
public static class PlayerRuntimeSystem
{
    public static void AddExp(this PlayerRuntime self, int amount)
    {
        var currentExp = self.__hotfix_exp();
        var currentLevel = self.__hotfix_level();

        // First rule: every 100 exp gives one level.
        var totalExp = currentExp + amount;
        var newLevel = currentLevel + (totalExp / 100);
        var leftExp = totalExp % 100;

        self.ApplyProgressFromHotfix(newLevel, leftExp);
    }
}
```

The method must follow these rules:

- It must be `public static`.
- It must be an extension method.
- The first parameter must be `this PlayerRuntime self`.
- The system class must use `[HotfixSystemOf(typeof(PlayerRuntime))]`.

Keep this code focused on rules. Do not start timers, open sockets, write files, or store long-lived static state here.

## Step 4: Build The Hotfix DLL

Build the hotfix project:

```bash
dotnet build Server/Hotfix/Game.Hotfix.csproj
```

The output will look like this:

```txt
Server/Hotfix/bin/Debug/net10.0/Game.Hotfix.dll
```

This DLL is the file your server will load.

## Step 5: Register Hotfix In The Server

In your server startup code, register a hotfix source:

```csharp
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Loading;

var hotfixDirectory = Path.GetFullPath("Server/Hotfix/bin/Debug/net10.0");

builder.Services.AddLakonaGameHotfix(
    new CurrentDirectoryHotfixAssemblySource(
        hotfixDirectory,
        "Game.Hotfix.dll"),
    sharedAssemblyNames: new[] { typeof(PlayerRuntime).Assembly.GetName().Name! });
```

The `sharedAssemblyNames` part is important.

It tells the hotfix loader:

> use the same `PlayerRuntime` type that the running server already uses.

Without this, the hotfix DLL may load another copy of the stable assembly. Then the types look the same in code, but they are not the same type at runtime.

## Step 6: Load The Hotfix DLL On Startup

After building the host, call reload once:

```csharp
var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var hotfix = scope.ServiceProvider.GetRequiredService<IHotfixManager>();
    var result = await hotfix.ReloadAsync();

    if (!result.Succeeded)
    {
        Console.WriteLine("Hotfix load failed:");
        Console.WriteLine(result.ErrorMessage);

        foreach (var line in result.Diagnostics)
        {
            Console.WriteLine(line);
        }
    }
}

await host.RunAsync();
```

Now the first hotfix rule is active.

## Step 7: Call The Stable Wrapper

Your normal server code should call the stable wrapper:

```csharp
var player = new PlayerRuntime();

player.AddExp(120);

Console.WriteLine(player.Level);
Console.WriteLine(player.Exp);
```

The server code does not call the hotfix DLL directly. It calls the stable object. The stable object uses `HotfixDispatch` to find the current hotfix method.

This is the key point:

```txt
server code -> stable wrapper -> current hotfix rule
```

## Step 8: Change The Rule

Now change the hotfix method:

```csharp
public static void AddExp(this PlayerRuntime self, int amount)
{
    var currentExp = self.__hotfix_exp();
    var currentLevel = self.__hotfix_level();

    // New rule: every 50 exp gives one level.
    var totalExp = currentExp + amount;
    var newLevel = currentLevel + (totalExp / 50);
    var leftExp = totalExp % 50;

    self.ApplyProgressFromHotfix(newLevel, leftExp);
}
```

Build the hotfix project again:

```bash
dotnet build Server/Hotfix/Game.Hotfix.csproj
```

Then call reload again:

```csharp
var result = await hotfix.ReloadAsync();
```

Existing `PlayerRuntime` objects stay alive. The next `AddExp` call uses the new rule.

## Step 9: What Happens If Reload Fails?

If the new DLL has a bad method shape, missing dependency, or load error, reload fails.

Lakona.Game keeps the old rule active.

That means live rooms and actors can keep running with the last good hotfix. You should still log the error and fix the DLL, but a failed reload does not clear your current state.

## Step 10: Add File Watch Reload For Development

For local development, you can add the optional file watcher:

```csharp
builder.Services.AddLakonaGameHotfixFileWatcher(options =>
{
    options.Enabled = true;
});
```

This is useful while you test. For production, an explicit admin command is usually safer. You normally want to choose when a new DLL becomes active.

## What Not To Hotfix

Do not use this first hotfix version for these changes:

- changing the shape of saved state
- changing RPC contracts
- changing serializer rules
- changing transport code
- changing actor runtime code
- moving live state into the hotfix DLL

Use a normal deploy, restart, or migration for those changes.

Hotfix is best for game rules:

- damage formulas
- reward points
- match settlement
- bot choice rules
- room timing rules
- small balance changes

## Where To Look In The Agar Sample

The Agar sample uses the same idea with real gameplay code.

Stable state:

```txt
samples/Agar.Unity/Shared/Gameplay/ArenaSimulation.cs
```

Hotfix rules:

```txt
samples/Agar.Unity/Server/Hotfix/Gameplay/ArenaSimulationSystem.cs
samples/Agar.Unity/Server/Hotfix/Gameplay/ArenaSettlementSystem.cs
```

Server registration:

```txt
samples/Agar.Unity/Server/Gateway/Program.cs
```

Tests that show reload and state survival:

```txt
samples/Agar.Unity/tests/BusinessLogic.Tests/AgarHotfixTests.cs
```

In Agar, `RoomRuntime` owns the tick loop, session cleanup, profile writes, leaderboard writes, logging, and network messages. `ArenaSimulation` owns the live state. The hotfix systems own replaceable arena rules.

That is the main pattern you should copy.

## A Short Checklist

When adding hotfix to your own project, check these items:

- Stable state is in a stable assembly.
- Stable state is marked `[HotfixState]` and is `partial`.
- Hotfix logic is in a separate class library.
- Hotfix methods are `public static` extension methods.
- The server calls stable wrapper methods, not hotfix methods directly.
- `AddLakonaGameHotfix` uses the correct DLL path.
- `sharedAssemblyNames` includes the stable state assembly name.
- Hotfix code does not own timers, static events, or long-lived state.
- A failed reload keeps the old rule active.

If those are true, you have the basic Lakona.Game hotfix model working.
