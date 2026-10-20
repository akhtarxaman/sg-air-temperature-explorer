variable "region" {
  description = "AWS region."
  type        = string
  default     = "ap-southeast-1"
}

variable "name" {
  description = "Name prefix for all resources."
  type        = string
  default     = "sgairtemp-poc"
}

variable "allowed_cidrs" {
  description = "CIDR ranges allowed to reach the server over HTTPS, e.g. [\"203.0.113.10/32\"]."
  type        = list(string)

  validation {
    condition     = length(var.allowed_cidrs) > 0
    error_message = "Set at least one CIDR range in allowed_cidrs."
  }
}

variable "instance_type" {
  description = "EC2 instance type. Everything runs on this one box, so 4 vCPU / 16 GB minimum."
  type        = string
  default     = "m6i.xlarge"
}

variable "root_volume_gb" {
  description = "Root volume size in GB."
  type        = number
  default     = 150
}

variable "termination_protection" {
  description = "Enable termination protection. Set to false before running terraform destroy."
  type        = bool
  default     = true
}
