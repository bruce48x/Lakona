data "alicloud_zones" "default" {
  available_instance_type = var.silo_instance_type
}

resource "alicloud_vpc" "default" {
  vpc_name          = "${var.project_name}-vpc"
  cidr_block        = var.vpc_cidr
  resource_group_id = alicloud_resource_manager_resource_group.default.id
}

resource "alicloud_vswitch" "default" {
  vswitch_name = "${var.project_name}-vsw"
  vpc_id       = alicloud_vpc.default.id
  cidr_block   = var.vswitch_cidr
  zone_id      = data.alicloud_zones.default.zones[0].id
}

resource "alicloud_security_group" "data" {
  security_group_name = "${var.project_name}-data-sg"
  resource_group_id   = alicloud_resource_manager_resource_group.default.id
  vpc_id              = alicloud_vpc.default.id
}

resource "alicloud_security_group" "app" {
  security_group_name = "${var.project_name}-app-sg"
  resource_group_id   = alicloud_resource_manager_resource_group.default.id
  vpc_id              = alicloud_vpc.default.id
}

resource "alicloud_security_group_rule" "ssh_data" {
  security_group_id = alicloud_security_group.data.id
  type              = "ingress"
  ip_protocol       = "tcp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "22/22"
  priority          = 1
  cidr_ip           = var.ssh_allowed_cidr
  description       = "ssh"
}

resource "alicloud_security_group_rule" "ssh_app" {
  security_group_id = alicloud_security_group.app.id
  type              = "ingress"
  ip_protocol       = "tcp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "22/22"
  priority          = 1
  cidr_ip           = var.ssh_allowed_cidr
  description       = "ssh"
}

resource "alicloud_security_group_rule" "postgres" {
  security_group_id = alicloud_security_group.data.id
  type              = "ingress"
  ip_protocol       = "tcp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "5432/5432"
  priority          = 1
  cidr_ip           = var.vpc_cidr
  description       = "PostgreSQL from VPC"
}

resource "alicloud_security_group_rule" "redis" {
  security_group_id = alicloud_security_group.data.id
  type              = "ingress"
  ip_protocol       = "tcp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "6379/6379"
  priority          = 1
  cidr_ip           = var.vpc_cidr
  description       = "Redis from VPC"
}

resource "alicloud_security_group_rule" "state_service" {
  security_group_id = alicloud_security_group.app.id
  type              = "ingress"
  ip_protocol       = "tcp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "11111/11111"
  priority          = 1
  cidr_ip           = var.vpc_cidr
  description       = "State service from VPC"
}

resource "alicloud_security_group_rule" "state_gateway" {
  security_group_id = alicloud_security_group.app.id
  type              = "ingress"
  ip_protocol       = "tcp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "30000/30000"
  priority          = 1
  cidr_ip           = var.vpc_cidr
  description       = "State gateway from VPC"
}

resource "alicloud_security_group_rule" "control_plane" {
  security_group_id = alicloud_security_group.app.id
  type              = "ingress"
  ip_protocol       = "tcp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "20000/20000"
  priority          = 1
  cidr_ip           = "0.0.0.0/0"
  description       = "Agar control WebSocket"
}

resource "alicloud_security_group_rule" "realtime" {
  security_group_id = alicloud_security_group.app.id
  type              = "ingress"
  ip_protocol       = "udp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "20001/20001"
  priority          = 1
  cidr_ip           = "0.0.0.0/0"
  description       = "Agar realtime KCP"
}
