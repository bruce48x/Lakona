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
  description = "ECS instance type for PostgreSQL and Redis."
  type        = string
  default     = "ecs.c8a.large"
}

variable "silo_instance_type" {
  description = "ECS instance type for Silo nodes."
  type        = string
  default     = "ecs.c8a.large"
}

variable "gateway_instance_type" {
  description = "ECS instance type for gateway nodes."
  type        = string
  default     = "ecs.c8a.large"
}

variable "silo_count" {
  description = "Number of Silo ECS instances."
  type        = number
  default     = 2
}

variable "gateway_count" {
  description = "Number of gateway ECS instances."
  type        = number
  default     = 2
}

variable "image_id" {
  description = "ECS image id. Use a Debian/Ubuntu image that can install Docker, or a custom image with Docker preinstalled."
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

variable "gateway_image" {
  description = "Docker image for samples/Game.Unity.Agar Server/Dockerfile gateway target. Must include the current distributed routing and persistence changes."
  type        = string
}

variable "silo_image" {
  description = "Docker image for samples/Game.Unity.Agar Server/Dockerfile state target. Must include the current distributed routing and persistence changes."
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

variable "cluster_id" {
  description = "Cluster id for the small test environment."
  type        = string
  default     = "small-test"
}

variable "service_id" {
  description = "Service id for the Game.Unity.Agar sample."
  type        = string
  default     = "ULinkGame-AgarUnity"
}

variable "postgres_db" {
  description = "PostgreSQL database name."
  type        = string
  default     = "ulinkgame"
}

variable "postgres_user" {
  description = "PostgreSQL user name."
  type        = string
  default     = "ulinkgame"
}
