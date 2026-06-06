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
    postgres_db       = jsonencode(var.postgres_db)
    postgres_user     = jsonencode(var.postgres_user)
    postgres_password = jsonencode(var.postgres_password)
    redis_password    = jsonencode(var.redis_password)
    grain_storage_sql = file("${path.cwd}/../postgres/init/002-dapper-grain-storage.sql")
  })

  tags = {
    Project = var.project_name
    Role    = "data"
  }
}

resource "alicloud_instance" "silo" {
  count                      = var.silo_count
  availability_zone          = alicloud_vswitch.default.zone_id
  security_groups            = [alicloud_security_group.app.id]
  instance_type              = var.silo_instance_type
  image_id                   = var.image_id
  instance_charge_type       = "PostPaid"
  instance_name              = "${var.project_name}-silo-${count.index + 1}"
  host_name                  = "agar-silo-${count.index + 1}"
  resource_group_id          = alicloud_resource_manager_resource_group.default.id
  vswitch_id                 = alicloud_vswitch.default.id
  internet_max_bandwidth_out = var.internet_max_bandwidth_out
  system_disk_size           = var.system_disk_size

  user_data = templatefile("${path.module}/user-data-silo.sh.tftpl", {
    silo_image                 = var.silo_image
    cluster_id                 = jsonencode(var.cluster_id)
    service_id                 = jsonencode(var.service_id)
    postgres_connection_string = jsonencode(local.postgres_connection_string)
    node_name                  = jsonencode("silo-${count.index + 1}")
  })

  tags = {
    Project = var.project_name
    Role    = "silo"
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

  user_data = templatefile("${path.module}/user-data-gateway.sh.tftpl", {
    gateway_image                 = var.gateway_image
    cluster_id                 = jsonencode(var.cluster_id)
    service_id                 = jsonencode(var.service_id)
    postgres_connection_string = jsonencode(local.postgres_connection_string)
    node_id                    = jsonencode("gateway-${count.index + 1}")
  })

  tags = {
    Project = var.project_name
    Role    = "gateway"
  }

  depends_on = [alicloud_instance.silo]
}

resource "alicloud_ecs_key_pair_attachment" "all" {
  key_pair_name = local.effective_key_pair_name
  instance_ids  = concat([alicloud_instance.data.id], alicloud_instance.silo[*].id, alicloud_instance.gateway[*].id)

  lifecycle {
    precondition {
      condition     = local.effective_key_pair_name != ""
      error_message = "Set key_pair_name to an existing Alicloud key pair, or set ssh_public_key so Terraform can create one."
    }
  }
}
