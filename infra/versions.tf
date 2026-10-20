terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.67.0"
    }
  }

  # Local state for now. TODO: S3 backend before this is used by more than one person.
}
