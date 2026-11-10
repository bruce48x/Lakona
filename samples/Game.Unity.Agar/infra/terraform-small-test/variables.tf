variable "project_name" {
  description = "Name prefix used for Alicloud resources."
  type        = string
  default     = "agar-unity-distributed-test"
}

variable "resource_group_name" {
  description = "Alicloud resource group name."
  type        = string
  default     = "agar-unity-distributed-test"
}

variable "vpc_cidr" {
  description = "CIDR block for the test VPC."
  type        = string
  default     = "172.30.0.0/16"
}

variable "vswitch_cidr" {
  description = "CIDR block for the ECS vSwitch."
  type        = string
  default     = "172.30.1.0/24"
}

variable "data_instance_type" {
  description = "ECS instance type for PostgreSQL, Redis and the data-role server."
  type        = string
  default     = "ecs.c8a.large"
}

variable "battle_instance_type" {
  description = "ECS instance type for battle-role nodes."
  type        = string
  default     = "ecs.c8a.large"
}

variable "gateway_instance_type" {
  description = "ECS instance type for gateway nodes."
  type        = string
  default     = "ecs.c8a.large"
}

variable "battle_count" {
  description = "Number of battle ECS instances."
  type        = number
  default     = 2
  validation {
    condition     = var.battle_count >= 1 && floor(var.battle_count) == var.battle_count
    error_message = "battle_count must be a positive integer."
  }
}

variable "gateway_count" {
  description = "Number of gateway ECS instances."
  type        = number
  default     = 2
  validation {
    condition     = var.gateway_count >= 1 && floor(var.gateway_count) == var.gateway_count
    error_message = "gateway_count must be a positive integer."
  }
}

variable "image_id" {
  description = "ECS image id. Use Ubuntu 24.04 with curl, docker.io and docker-compose-v2 available, or an image with curl, Docker and Compose v2 preinstalled."
  type        = string
}

variable "system_disk_size" {
  description = "ECS system disk size in GiB."
  type        = number
  default     = 60
}

variable "internet_max_bandwidth_out" {
  description = "Public outbound bandwidth in Mbps."
  type        = number
  default     = 20
}

variable "ssh_allowed_cidr" {
  description = "CIDR allowed to SSH into the test ECS. Avoid 0.0.0.0/0 outside short-lived test environments."
  type        = string
}

variable "key_pair_name" {
  description = "Existing Alicloud ECS key pair name. Ignored when ssh_public_key is set."
  type        = string
  default     = ""
}

variable "ssh_public_key" {
  description = "Optional public key. When set, Terraform creates a key pair named project_name."
  type        = string
  default     = ""
}

variable "server_image" {
  description = "One image built from the gateway target, shared by data, gateway and battle roles. Must be pullable by the ECS nodes."
  type        = string
}

variable "postgres_password" {
  description = "PostgreSQL password used by the test compose stack."
  type        = string
  sensitive   = true
}

variable "redis_password" {
  description = "Redis password used by the test compose stack."
  type        = string
  sensitive   = true
}

variable "postgres_db" {
  description = "PostgreSQL database name."
  type        = string
  default     = "lakona_agar"
}

variable "postgres_user" {
  description = "PostgreSQL user name."
  type        = string
  default     = "lakona_agar"
}
