output "public_ip" {
  description = "Elastic IP of the server. Point a DNS name at it."
  value       = aws_eip.server.public_ip
}

output "instance_id" {
  description = "EC2 instance ID."
  value       = aws_instance.server.id
}

output "ssm_session_command" {
  description = "Open a shell on the server without RDP or open ports."
  value       = "aws ssm start-session --region ${var.region} --target ${aws_instance.server.id}"
}
