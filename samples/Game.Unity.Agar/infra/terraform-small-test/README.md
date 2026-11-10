# Game.Unity.Agar Distributed Test Terraform

This module creates a small Alicloud test deployment of the current Agar sample:

- One data ECS runs PostgreSQL, Redis and the `data` role.
- `gateway_count` ECS nodes run the `gateway` role: login/player RPC over WebSocket.
- `battle_count` ECS nodes run the `battle` role: battle RPC over KCP.
- All roles use the same server image, with different generated configuration.

This is a manual distributed test environment, with a single database host and
direct public client connections. It has no managed databases, TLS, load balancer,
autoscaling, backups or production failover.

## Prepare

Use an Ubuntu 24.04 ECS image with `curl` and access to the `docker.io` and
`docker-compose-v2` packages, or an image with those tools already installed.
Every node needs access to ECS instance metadata and the image registry. Private
registries require Docker credentials already configured on the ECS image.

From the repository root, build and push one image:

```powershell
docker build -f samples/Game.Unity.Agar/Server/Dockerfile --target gateway -t registry.example.com/lakona-game/agar-server:small-test .
docker push registry.example.com/lakona-game/agar-server:small-test
```

The Docker target is named `gateway`, but its App and Hotfix support all three
roles. Rebuild the image after server changes.

Copy `terraform.tfvars.example` to `terraform.tfvars` in this directory. Set
`image_id`, `server_image`, `ssh_allowed_cidr`, `key_pair_name` or `ssh_public_key`,
and the PostgreSQL/Redis passwords. Use passwords compatible with the connection
string formats: PostgreSQL values must not contain semicolons, and Redis passwords
must not contain commas. Use a `/32` SSH CIDR for your own address.

Set Alicloud credentials in your environment before planning or applying:

```powershell
$env:ALICLOUD_ACCESS_KEY="***"
$env:ALICLOUD_SECRET_KEY="***"
$env:ALICLOUD_REGION="cn-hangzhou"
```

## Static validation and deployment

From this directory, static checks require no cloud credentials or resources:

```powershell
terraform fmt -check
terraform init -backend=false
terraform validate
```

To deploy after reviewing your configuration:

```powershell
terraform init
terraform plan
terraform apply
```

You can also run from another directory using
`terraform -chdir=<absolute-module-directory> ...`. SQL and bootstrap templates
are resolved relative to the module, not the launching directory.

## Configuration and endpoints

Each host runs `agar-server`, mounting `/opt/agar/appsettings.json` read-only.
The generated configuration uses `Lakona:Node:Roles`, Redis cluster membership,
and role-specific `Lakona:Endpoints`. Only the data role receives persistence
connection strings. PostgreSQL and Redis must be healthy before that role starts.
All roles load the packaged Hotfix assembly.

The shared bootstrap reads ECS private/public IPs from instance metadata. Cluster
RPC binds the private address; client endpoints advertise the public address.

| Traffic | Host | Port/access |
| --- | --- | --- |
| Login/player WebSocket | Gateway | TCP 20000, public, `/ws` |
| Battle KCP | Battle | UDP 20001, public |
| Cluster RPC | All roles | TCP 21001, VPC only |
| PostgreSQL / Redis | Data | TCP 5432 / 6379, VPC only |
| Management / health | All roles | TCP 20080, loopback only |
| SSH | All hosts | TCP 22, `ssh_allowed_cidr` only |

Gateway and battle share an app security group; each role listens only on its
configured client transport. Useful Terraform outputs:

- `control_plane_url`: WebSocket URL for each gateway.
- `realtime_endpoint`: KCP address for each battle node; matchmaking selects the
  actual room endpoint from the battle node's advertised address.
- `data_private_ip`, `battle_private_ips`, `gateway_private_ips`: cluster hosts.
- `battle_public_ips`, `gateway_public_ips`, `ssh_command`: external access.

Inspect the server on any host with `docker logs -f agar-server`. On the data host:

```bash
cd /opt/agar
docker compose ps
docker compose logs -f postgres redis
```

Database volumes live on the data ECS system disk. PostgreSQL 18 mounts its named
volume at `/var/lib/postgresql`. Terraform state contains rendered credentials,
even though password variables are marked sensitive; keep state private.

## Migrating the previous Silo example

- Replace `gateway_image` and `silo_image` with the single `server_image`.
- Rename `silo_count` / `silo_instance_type` to `battle_count` /
  `battle_instance_type`, and consume `battle_private_ips` instead of
  `silo_private_ips`.
- The `moved` block maps existing Silo resource addresses to battle addresses.
  This preserves state identity; it does not guarantee an in-place deployment.
- Old bootstrap scripts and configuration keys are replaced, including cluster
  ports 11111/30000 with 21001. KCP now belongs to battle nodes, not gateways.
- Existing machines do not automatically rerun cloud-init when `user_data`
  changes. Review the plan and recreate affected test nodes to install the new
  bootstrap. Back up data before replacing the data ECS; existing PostgreSQL
  volumes are not automatically migrated to the updated mount layout.
