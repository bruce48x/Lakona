resource "alicloud_ecs_key_pair" "generated" {
  count         = var.ssh_public_key == "" ? 0 : 1
  key_pair_name = var.project_name
  public_key    = var.ssh_public_key
}

resource "alicloud_instance" "data" {
  availability_zone          = alicloud_vswitch.default.zone_id
  security_groups            = [alicloud_security_group.data.id]
  instance_type              = var.data_instance_type
  image_id                   = var.image_id
  instance_charge_type       = "PostPaid"
  instance_name              = "${var.project_name}-data"
  host_name                  = "agar-data"
  resource_group_id          = alicloud_resource_manager_resource_group.default.id
  vswitch_id                 = alicloud_vswitch.default.id
  internet_max_bandwidth_out = var.internet_max_bandwidth_out
  system_disk_size           = var.system_disk_size

  user_data = templatefile("${path.module}/user-data-data.sh.tftpl", {
    postgres_db       = replace(jsonencode(var.postgres_db), "$", "$$")
    postgres_user     = replace(jsonencode(var.postgres_user), "$", "$$")
    postgres_password = replace(jsonencode(var.postgres_password), "$", "$$")
    redis_password    = replace(jsonencode(var.redis_password), "$", "$$")
    user_storage_sql  = file("${path.module}/../postgres/init/001-agar-users.sql")
    node_bootstrap = templatefile("${path.module}/user-data-node.sh.tftpl", {
      server_image_base64 = base64encode(var.server_image)
      config_base64       = base64encode(jsonencode(local.data_config))
      public_endpoint     = false
    })
  })

  tags = {
    Project = var.project_name
    Role    = "data"
  }
}

moved {
  from = alicloud_instance.silo
  to   = alicloud_instance.battle
}

resource "alicloud_instance" "battle" {
  count                      = var.battle_count
  availability_zone          = alicloud_vswitch.default.zone_id
  security_groups            = [alicloud_security_group.app.id]
  instance_type              = var.battle_instance_type
  image_id                   = var.image_id
  instance_charge_type       = "PostPaid"
  instance_name              = "${var.project_name}-battle-${count.index + 1}"
  host_name                  = "agar-battle-${count.index + 1}"
  resource_group_id          = alicloud_resource_manager_resource_group.default.id
  vswitch_id                 = alicloud_vswitch.default.id
  internet_max_bandwidth_out = var.internet_max_bandwidth_out
  system_disk_size           = var.system_disk_size

  user_data = templatefile("${path.module}/user-data-node.sh.tftpl", {
    server_image_base64 = base64encode(var.server_image)
    public_endpoint     = true
    config_base64 = base64encode(jsonencode({
      Lakona = merge(local.common_lakona, {
        Node = { Id = "battle-${count.index + 1}", Roles = ["battle"] }, Endpoints = local.battle_endpoints
      })
      ConnectionStrings = { LakonaClusterRedis = local.redis_connection_string }
    }))
  })

  tags = {
    Project = var.project_name
    Role    = "battle"
  }

  depends_on = [alicloud_instance.data]
}

resource "alicloud_instance" "gateway" {
  count                      = var.gateway_count
  availability_zone          = alicloud_vswitch.default.zone_id
  security_groups            = [alicloud_security_group.app.id]
  instance_type              = var.gateway_instance_type
  image_id                   = var.image_id
  instance_charge_type       = "PostPaid"
  instance_name              = "${var.project_name}-gateway-${count.index + 1}"
  host_name                  = "agar-gateway-${count.index + 1}"
  resource_group_id          = alicloud_resource_manager_resource_group.default.id
  vswitch_id                 = alicloud_vswitch.default.id
  internet_max_bandwidth_out = var.internet_max_bandwidth_out
  system_disk_size           = var.system_disk_size

  user_data = templatefile("${path.module}/user-data-node.sh.tftpl", {
    server_image_base64 = base64encode(var.server_image)
    public_endpoint     = true
    config_base64 = base64encode(jsonencode({
      Lakona = merge(local.common_lakona, {
        Node = { Id = "gateway-${count.index + 1}", Roles = ["gateway"] }, Endpoints = local.gateway_endpoints
      })
      ConnectionStrings = { LakonaClusterRedis = local.redis_connection_string }
    }))
  })

  tags = {
    Project = var.project_name
    Role    = "gateway"
  }

  depends_on = [alicloud_instance.battle]
}

resource "alicloud_ecs_key_pair_attachment" "all" {
  key_pair_name = local.effective_key_pair_name
  instance_ids  = concat([alicloud_instance.data.id], alicloud_instance.battle[*].id, alicloud_instance.gateway[*].id)

  lifecycle {
    precondition {
      condition     = local.effective_key_pair_name != ""
      error_message = "Set key_pair_name to an existing Alicloud key pair, or set ssh_public_key so Terraform can create one."
    }
  }
}
