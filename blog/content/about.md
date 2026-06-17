---
title: About Lakona
description: Lakona is open-source networking infrastructure for online multiplayer games.
date: 2026-05-07T11:20:00+08:00
---

## What is Lakona?

Lakona provides open-source networking infrastructure for online multiplayer games.

- **Lakona.Rpc** — a strongly typed bidirectional RPC framework for Unity, Godot, and .NET. Define your API once as a shared C# interface, and let source generators create the networking glue.
- **Lakona.Game** — an actor-based C# game server framework built on Lakona.Rpc. Features hot-reloadable game logic, session management, reliable push, and cluster routing.

## Design Philosophy

- **Shared contracts are the source of truth.** Server and client compile the same interfaces.
- **Zero magic.** Generated code is deterministic, readable, and IL2CPP-friendly.
- **Transports are pluggable.** TCP, WebSocket, KCP — swap without changing your game logic.
- **Production-ready.** Built for real games with real deadlines.
