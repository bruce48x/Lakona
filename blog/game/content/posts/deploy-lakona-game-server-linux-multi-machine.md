---
title: "Deploying A Lakona.Game Server To Multiple Debian 13 Machines"
date: 2026-05-26T10:30:00+08:00
summary: "Publish the server, run Gateway and State processes on Debian 13 hosts, configure systemd, expose public endpoints, and keep internal traffic private."
tags:
  - lakona-game
  - linux
  - debian
  - deployment
  - dotnet
  - unity
  - godot
categories:
  - Tutorial
---

This guide shows a practical deployment shape for a generated Lakona.Game server on **Debian 13** machines.

The target topology is deliberately simple:

- one or more **Gateway** machines that accept client connections
- one or more **State** machines that run authoritative game state
- a private network between server machines
- a public load balancer or reverse proxy in front of Gateway
- systemd services to keep each process running

The commands assume:

- Debian 13 on every server
- `systemd`
- `nftables` for host firewall rules
- `nginx` on the public reverse-proxy machine when using WebSocket transport
- self-contained .NET publish output, so production hosts do not need the .NET runtime installed

Lakona.Game does not try to own your whole operations platform. You still choose your cloud, firewall rules, database, secrets store, logs, metrics, and rollout process. The framework gives you a C# server shape that can be split cleanly across machines.

## Before You Deploy

Start from a project that already runs locally:

```bash
lakona new --name MyGame --client-engine unity --transport websocket --serializer json
cd MyGame
dotnet run --project Server/State/State.csproj
dotnet run --project Server/Server/Server.csproj
```

Use your generated project's actual state project path if you renamed it.

For a multi-machine deployment, decide these values first:

- public Gateway host name, such as `game.example.com`
- public Gateway port, usually `443`
- internal Gateway endpoint, such as `10.0.1.10:20000`
- internal State endpoint, such as `10.0.2.10:21000`
- database connection strings, if your project uses persistence
- per-environment secrets, certificates, and API keys

Do not put production secrets in the repository. Put them in your deployment platform, systemd environment files with locked-down permissions, or a dedicated secret manager.

## Recommended Machine Layout

A small production-like layout can start with three machines:

```text
load-balancer-1
  public 443 -> gateway-1:20000 and gateway-2:20000

gateway-1
  runs Server/Server
  listens on 0.0.0.0:20000 inside the private network

gateway-2
  runs Server/Server
  listens on 0.0.0.0:20000 inside the private network

state-1
  runs Server/State
  accepts only private network traffic
```

Keep the state process off the public internet. Only Gateway should be reachable from players.

For the first production deployment, keep the routing model boring:

- Gateway nodes are public ingress.
- State nodes are private.
- Databases are private.
- Health checks are explicit.
- Rollouts replace one node at a time.

## Publish The Server

Build on CI or on a build machine that has the .NET SDK installed:

```bash
dotnet publish Server/Server/Server.csproj -c Release -r linux-x64 --self-contained true -o artifacts/linux-x64/gateway
dotnet publish Server/State/State.csproj -c Release -r linux-x64 --self-contained true -o artifacts/linux-x64/state
```

This is the recommended Debian 13 path for a first deployment because the published directory contains the runtime it needs. If your operations team already manages .NET runtime versions on every host, you can use framework-dependent output instead:

```bash
dotnet publish Server/Server/Server.csproj -c Release -o artifacts/linux-x64/gateway
dotnet publish Server/State/State.csproj -c Release -o artifacts/linux-x64/state
```

With framework-dependent output, install the matching .NET runtime on each Debian 13 server before starting systemd services.

## Prepare Debian 13 Hosts

On each Debian 13 machine, install basic packages:

```bash
sudo apt update
sudo apt install -y ca-certificates curl rsync nftables
```

On the reverse-proxy machine, also install Nginx:

```bash
sudo apt install -y nginx
```

Enable the firewall service:

```bash
sudo systemctl enable --now nftables
```

Create a dedicated user and directories on every application host:

```bash
sudo useradd --system --create-home --shell /usr/sbin/nologin lakona-game
sudo mkdir -p /opt/mygame/gateway /opt/mygame/state /etc/mygame
sudo chown -R lakona-game:lakona-game /opt/mygame
sudo chmod 750 /etc/mygame
```

Copy the published files:

```bash
rsync -av artifacts/linux-x64/gateway/ gateway-1:/opt/mygame/gateway/
rsync -av artifacts/linux-x64/gateway/ gateway-2:/opt/mygame/gateway/
rsync -av artifacts/linux-x64/state/ state-1:/opt/mygame/state/
```

For self-contained builds, make the entry files executable if needed:

```bash
chmod +x /opt/mygame/gateway/Gateway
chmod +x /opt/mygame/state/State
```

## Configure Gateway

Create `/etc/mygame/gateway.env` on each Gateway host:

```ini
DOTNET_ENVIRONMENT=Production
Endpoint__Transport=websocket
Endpoint__Host=0.0.0.0
Endpoint__Port=20000
Endpoint__Path=/ws
```

If your project has internal cluster settings, configure them with environment variables too:

```ini
Cluster__NodeId=gateway-1
Cluster__NodeEpoch=1
Cluster__InternalEndpoint=tcp://10.0.1.10:21000
Cluster__RouteDirectoryEndpoint=tcp://10.0.2.10:21001
Cluster__RouteLeaseSeconds=30
Cluster__SendTimeoutMilliseconds=2000
```

Use a unique `Cluster__NodeId` per machine. Do not copy `gateway-1` to every host.

Create `/etc/systemd/system/mygame-gateway.service`:

```ini
[Unit]
Description=MyGame Gateway
After=network-online.target
Wants=network-online.target

[Service]
User=lakona-game
Group=lakona-game
WorkingDirectory=/opt/mygame/gateway
EnvironmentFile=/etc/mygame/gateway.env
ExecStart=/opt/mygame/gateway/Gateway
Restart=always
RestartSec=5
KillSignal=SIGINT
SyslogIdentifier=mygame-gateway

[Install]
WantedBy=multi-user.target
```

For a framework-dependent build, use `dotnet` and the DLL instead:

```ini
ExecStart=/usr/bin/dotnet /opt/mygame/gateway/Server.dll
```

Start it:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now mygame-gateway
sudo systemctl status mygame-gateway
```

## Configure The State Process

Create `/etc/mygame/state.env` on the state host:

```ini
DOTNET_ENVIRONMENT=Production
State__Host=0.0.0.0
State__Port=21000
```

Use your project's actual configuration keys. The important rule is that the state process listens only on private network addresses.

Create `/etc/systemd/system/mygame-state.service`:

```ini
[Unit]
Description=MyGame State
After=network-online.target
Wants=network-online.target

[Service]
User=lakona-game
Group=lakona-game
WorkingDirectory=/opt/mygame/state
EnvironmentFile=/etc/mygame/state.env
ExecStart=/opt/mygame/state/State
Restart=always
RestartSec=5
KillSignal=SIGINT
SyslogIdentifier=mygame-state

[Install]
WantedBy=multi-user.target
```

If your project publishes a differently named state entrypoint, replace `State` with that file. For a framework-dependent build, use `/usr/bin/dotnet /opt/mygame/state/State.dll`.

Start it:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now mygame-state
sudo systemctl status mygame-state
```

## Put A Reverse Proxy In Front Of Gateway

For WebSocket transport, terminate TLS at Nginx or your cloud load balancer and forward to Gateway. If you use Let's Encrypt on the Debian 13 reverse-proxy machine, install Certbot:

```bash
sudo apt install -y certbot python3-certbot-nginx
sudo certbot certonly --nginx -d game.example.com
```

Then create `/etc/nginx/sites-available/mygame.conf`:

```nginx
upstream mygame_gateway {
    server 10.0.1.10:20000;
    server 10.0.1.11:20000;
}

server {
    listen 443 ssl http2;
    server_name game.example.com;

    ssl_certificate /etc/letsencrypt/live/game.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/game.example.com/privkey.pem;

    location /ws {
        proxy_pass http://mygame_gateway;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 300s;
    }
}
```

Enable the site:

```bash
sudo ln -s /etc/nginx/sites-available/mygame.conf /etc/nginx/sites-enabled/mygame.conf
sudo nginx -t
sudo systemctl reload nginx
```

For TCP or KCP transport, use a layer-4 load balancer instead of HTTP reverse proxy rules. Keep the public listener stable and route only to Gateway machines.

## Firewall Rules

Use firewall rules that match the process boundary:

- public internet can reach only the load balancer or Gateway public port
- Gateway machines can reach State machines on private ports
- State machines can reach databases and route-directory dependencies
- databases are never publicly reachable
- SSH is restricted to your operations network

On Debian 13, use `nftables`. A Gateway host that accepts traffic from a private `10.0.0.0/16` network and SSH from an operations network might use `/etc/nftables.conf` like this:

```nft
#!/usr/sbin/nft -f

flush ruleset

table inet filter {
    chain input {
        type filter hook input priority 0;
        policy drop;

        iif "lo" accept
        ct state established,related accept

        ip saddr 10.0.0.0/16 tcp dport { 20000, 21000 } accept
        ip saddr 203.0.113.0/24 tcp dport 22 accept

        ip protocol icmp accept
        ip6 nexthdr ipv6-icmp accept
    }

    chain forward {
        type filter hook forward priority 0;
        policy drop;
    }

    chain output {
        type filter hook output priority 0;
        policy accept;
    }
}
```

Apply it:

```bash
sudo nft -f /etc/nftables.conf
sudo systemctl restart nftables
sudo nft list ruleset
```

Replace `203.0.113.0/24` with your real operations network. If Nginx runs on the same Gateway host, also allow public `443` and keep `20000` private:

```nft
tcp dport 443 accept
```

## Health Checks And Logs

If the generated project includes cluster deployment scaffolding, Gateway supports:

```bash
cd /opt/mygame/gateway
./Gateway --health-check
```

For a framework-dependent build, use `dotnet Server.dll --health-check` instead.

Use it from systemd, CI smoke tests, or your load balancer health probe when appropriate. A basic manual check is:

```bash
journalctl -u mygame-gateway -f
journalctl -u mygame-state -f
systemctl status mygame-gateway
systemctl status mygame-state
```

For production, add structured logs, metrics, and alerts around:

- Gateway process restarts
- failed client connection attempts
- reconnect and state-lost results
- reliable push replay count and pending count
- state process latency
- database latency
- cluster route lookup failures, if cluster mode is enabled

## Optional Compose Rehearsal

For a local cluster deployment rehearsal, generate with:

```bash
lakona new --name MyGame --deploy-profile compose
```

That profile can generate:

- `Server/Dockerfile`
- `docker-compose.cluster.yml`
- `.env.cluster.example`
- `ops/CLUSTER_OPERATIONS.md`

Use this to validate packaging and environment-variable wiring before moving to real Debian 13 hosts. Do not treat the generated compose file as a complete production platform. It intentionally avoids production secrets and durable infrastructure choices.

## Rolling Out A New Version

A conservative rollout looks like this:

1. Publish a new version into a versioned directory such as `/opt/mygame/releases/2026-05-26-1030/gateway`.
2. Stop one Gateway node.
3. Replace its current symlink or copied files.
4. Start the Gateway node.
5. Run health checks and a client smoke test.
6. Repeat for the next Gateway node.
7. Roll state nodes only after you know how active sessions and authoritative state should drain or recover.

Do not restart all Gateway and State nodes at the same time unless your game flow can tolerate every session reconnecting or starting fresh.

For stateful services, prefer explicit draining:

- stop accepting new ownership or new rooms
- finish bounded in-flight work
- flush required state
- stop the process
- start the new version

## Client Configuration

Clients should connect to the public endpoint, not directly to a machine:

```text
wss://game.example.com/ws
```

Do not ship private IP addresses in the client. Keep the client endpoint stable so you can replace Gateway machines without publishing a new game build.

## Common Failures

If the client cannot connect:

1. Confirm Gateway is running with `systemctl status mygame-gateway`.
2. Check `journalctl -u mygame-gateway`.
3. Confirm the load balancer forwards WebSocket upgrade headers.
4. Confirm the public path matches `Endpoint__Path`.
5. Confirm firewall rules allow traffic to Gateway.

If Gateway starts but cannot reach state:

1. Confirm the state process is running.
2. Check private network routing between hosts.
3. Confirm internal endpoint environment variables are different per host.
4. Confirm no production secret or connection string is missing.
5. Check route-directory or cluster dependency health if cluster mode is enabled.

If reconnect behaves badly after deployment:

1. Confirm clients reconnect to the same public endpoint.
2. Confirm session state is not being lost unexpectedly during restarts.
3. Confirm reliable push acknowledgement storage matches your expected durability model.
4. Use explicit `StateLost` or `StateRefreshRequired` handling instead of pretending every reconnect can resume.

## Summary

The deployment model is:

1. Publish Gateway and State as separate Debian 13 `linux-x64` artifacts.
2. Run each process under systemd with environment-based configuration.
3. Put only Gateway behind a public load balancer or reverse proxy.
4. Keep State, databases, and cluster dependencies on private networks.
5. Use health checks, logs, and one-node-at-a-time rollouts.

Start with the simplest two-machine deployment, then add more Gateway or State nodes only when load, isolation, or operations needs justify it.
