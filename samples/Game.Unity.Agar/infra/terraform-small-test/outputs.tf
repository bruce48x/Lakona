output "data_private_ip" {
  description = "Private IP of the data ECS."
  value       = alicloud_instance.data.private_ip
}

output "silo_private_ips" {
  description = "Private IPs of Silo ECS instances."
  value       = alicloud_instance.silo[*].private_ip
}

output "gateway_public_ips" {
  description = "Public IPs of gateway ECS instances."
  value       = alicloud_instance.gateway[*].public_ip
}

output "gateway_private_ips" {
  description = "Private IPs of gateway ECS instances."
  value       = alicloud_instance.gateway[*].private_ip
}

output "control_plane_url" {
  description = "Unity control-plane WebSocket endpoints."
  value       = [for instance in alicloud_instance.gateway : "ws://${instance.public_ip}:20000/ws"]
}

output "realtime_endpoint" {
  description = "Unity realtime KCP endpoints."
  value       = [for instance in alicloud_instance.gateway : "${instance.public_ip}:20001/udp"]
}

output "ssh_command" {
  description = "Example SSH commands."
  value = concat(
    ["ssh root@${alicloud_instance.data.public_ip}"],
    [for instance in alicloud_instance.silo : "ssh root@${instance.public_ip}"],
    [for instance in alicloud_instance.gateway : "ssh root@${instance.public_ip}"]
  )
}
