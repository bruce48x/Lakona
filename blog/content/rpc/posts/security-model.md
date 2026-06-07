+++
title = "Security Model"
date = 2026-05-12T09:25:00+08:00
+++

Lakona.Rpc security settings live at the frame layer. The main entry point is `TransportSecurityConfig`. It can enable compression and symmetric encryption, but it is not a complete replacement for authentication, authorization, or TLS.

## What `TransportSecurityConfig` Does

When the client enables security through `RpcClientOptions.UseSecurity(...)`, or the server enables it through `RpcServerHostBuilder.UseSecurity(...)`, the runtime wraps the underlying transport with `TransformingTransport`.

Current options include:

- `EnableCompression`: compress frames before sending.
- `CompressionThresholdBytes`: only try compression when the frame reaches this size, default `1024`.
- `MaxDecompressedFrameBytes`: maximum decompressed frame size, default from `RpcProtocolLimits.DefaultMaxDecompressedFrameBytes`.
- `EnableEncryption`: enable symmetric encryption.
- `EncryptionKey` / `EncryptionKeyBase64`: symmetric key source.

Encryption currently uses AES-CBC plus HMAC-SHA256. HKDF derives an encryption key and a MAC key from the configured key. The receiver verifies HMAC; verification failure throws and causes the connection to fail.

## What It Does Not Do

`TransportSecurityConfig` does not verify remote identity. If an attacker obtains the same symmetric key, they can construct frames that pass verification.

It does not provide user login, permission checks, session tickets, token refresh, or replay protection. Business identity and authorization still need to be implemented in service methods.

It does not replace TLS / WSS. TLS / WSS provides transport-layer certificate validation, link encryption, and a mature deployment ecosystem; Lakona.Rpc frame encryption only applies to application frames.

It does not hide connection metadata such as IP, port, connection time, or traffic patterns visible from frame sizes.

## Relationship to TLS / WSS

If you use WebSocket over the public internet, prefer `wss://` and standard TLS termination. Lakona.Rpc's `WsTransport` currently accepts a URI; whether you use `ws://` or `wss://` depends on your endpoint and host configuration.

`TransportSecurityConfig` can be used as an additional frame-level protection layer, but client and server configuration must match exactly. Mismatched compression, encryption, or keys cause decoding failures.

## Key and Configuration Guidance

Do not hard-code production keys in Unity, Godot, or Tuanjie clients. Static keys inside client packages can be extracted. If frame encryption is required, evaluate the risk together with login, versioning, environment, and key-rotation strategy.

Do not compress payloads that contain both attacker-controlled content and secrets before encryption unless you explicitly accept compression side-channel risk. The current configuration supports compress-then-encrypt; that can help bandwidth, but it does not fit every threat model.

Enable `MaxDecompressedFrameBytes`-related limits on both server and client to prevent abnormal compressed payloads from causing excessive memory use.

## Not Implemented Today

The current repository does not include certificate management, mTLS, JWT validation, server authorization middleware, automatic key exchange, a key-rotation protocol, persistent nonce-based replay protection, or a built-in account system.
