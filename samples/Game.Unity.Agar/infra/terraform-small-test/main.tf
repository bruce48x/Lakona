resource "alicloud_resource_manager_resource_group" "default" {
  resource_group_name = var.resource_group_name
  display_name        = "Agar Unity small test"
}

locals {
  effective_key_pair_name = var.ssh_public_key != "" ? alicloud_ecs_key_pair.generated[0].key_pair_name : var.key_pair_name
  # Persistence runs on the data ECS alongside PostgreSQL; no self-reference in user_data.
  postgres_connection_string = join("", [
    "Host=127.0.0.1;",
    "Port=5432;",
    "Database=${var.postgres_db};",
    "Username=${var.postgres_user};",
    "Password=${var.postgres_password}"
  ])
  redis_connection_string = join("", [
    "${alicloud_instance.data.private_ip}:6379,",
    "password=${var.redis_password}"
  ])
  local_redis_connection_string = "127.0.0.1:6379,password=${var.redis_password}"

  common_lakona = {
    Hotfix     = { DebugWatcher = "On" }
    Management = { Http = { Host = "127.0.0.1", Port = 20080 } }
    Health     = { Enabled = true, RequireLoopback = true }
    Cluster    = { Membership = { Provider = "Redis", ConnectionStringName = "LakonaClusterRedis" } }
  }
  data_config = {
    Lakona = merge(local.common_lakona, {
      Node = { Id = "data-1", Roles = ["data"] }, Endpoints = []
    })
    Agar = { Persistence = {
      Postgres = { ConnectionStringName = "AgarGamePostgres" }
      Redis    = { ConnectionStringName = "AgarGameRedis" }
    } }
    ConnectionStrings = {
      AgarGamePostgres   = local.postgres_connection_string
      AgarGameRedis      = local.local_redis_connection_string
      LakonaClusterRedis = local.local_redis_connection_string
    }
  }
  gateway_endpoints = [{
    Transport = "websocket", Serializer = "memorypack", Host = "0.0.0.0",
    Port      = 20000, Path = "/ws", ReliablePush = true, RpcServices = ["login", "player"]
  }]
  battle_endpoints = [{
    Transport = "kcp", Serializer = "memorypack", Host = "0.0.0.0",
    Port      = 20001, ReliablePush = false, RpcServices = ["battle"]
  }]
}
