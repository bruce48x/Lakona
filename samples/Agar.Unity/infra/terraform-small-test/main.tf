resource "alicloud_resource_manager_resource_group" "default" {
  resource_group_name = var.resource_group_name
  display_name        = "Agar Unity small test"
}

locals {
  effective_key_pair_name = var.ssh_public_key != "" ? alicloud_ecs_key_pair.generated[0].key_pair_name : var.key_pair_name
  postgres_connection_string = join("", [
    "Host=${alicloud_instance.data.private_ip};",
    "Port=5432;",
    "Database=${var.postgres_db};",
    "Username=${var.postgres_user};",
    "Password=${var.postgres_password}"
  ])
}
