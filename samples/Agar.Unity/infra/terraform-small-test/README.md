# Agar.Unity Distributed Test Terraform

This Terraform module creates a small distributed Alicloud test environment for the `samples/Agar.Unity` server.

Topology:

- 1 data ECS instance running PostgreSQL and Redis in Docker Compose
- `silo_count` Silo ECS instances
- `gateway_count` gateway ECS instances
- Public gateway control-plane WebSocket on TCP `20000`
- Public gateway realtime KCP on UDP `20001`

This is a distributed test topology for validating cross-machine Silo/Gateway behavior. It is still not a production topology: PostgreSQL and Redis run on a single ECS instance, there is no ALB/TLS/autoscaling, and clients connect directly to one of the gateway public IPs.

## Prerequisites

Set Alicloud provider credentials before running Terraform:

```powershell
$env:ALICLOUD_ACCESS_KEY="***"
$env:ALICLOUD_SECRET_KEY="***"
$env:ALICLOUD_REGION="cn-hangzhou"
```

Build and push the two server images from the repository root:

```powershell
docker build -f samples/Agar.Unity/Server/Dockerfile --target gateway -t registry.example.com/lakona-game/agar-gateway:small-test .
docker build -f samples/Agar.Unity/Server/Dockerfile --target silo -t registry.example.com/lakona-game/agar-silo:small-test .
docker push registry.example.com/lakona-game/agar-gateway:small-test
docker push registry.example.com/lakona-game/agar-silo:small-test
```

Copy `terraform.tfvars.example` to `terraform.tfvars`, then fill in:

- `image_id`
- `ssh_allowed_cidr`
- `key_pair_name` or `ssh_public_key`
- `gateway_image`
- `silo_image`
- `postgres_password`
- `redis_password`

The server images must include the current distributed routing and persistence support from this repository change. Rebuild and push images after changing the server code.

## Apply

```powershell
terraform init
terraform plan
terraform apply
```

Useful outputs:

- `control_plane_url`: Unity WebSocket control endpoints, one per gateway
- `realtime_endpoint`: Unity KCP endpoints, one per gateway
- `silo_private_ips`: private Silo node addresses
- `ssh_command`: SSH commands for all nodes

On the data node, inspect PostgreSQL and Redis with:

```bash
cd /opt/agar
docker compose ps
docker compose logs -f postgres redis
```

On Silo or gateway nodes, inspect containers with:

```bash
docker ps
docker logs -f agar-silo
docker logs -f agar-gateway
```

## Notes

- This environment stores PostgreSQL and Redis data in Docker named volumes on the data ECS system disk.
- It is suitable for internal smoke tests and distributed manual multiplayer tests.
- It does not provide managed database, managed Redis, ALB, TLS, autoscaling, backups, or production-grade failover.
- `postgres_password` and `redis_password` are sensitive variables, but Terraform still stores rendered `user_data` in state. Keep state files private.
- Open SSH only to your own IP by setting `ssh_allowed_cidr` to a `/32` CIDR.
