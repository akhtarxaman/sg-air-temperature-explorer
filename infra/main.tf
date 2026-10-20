# Single Windows server for OutSystems 11: IIS, Platform Server and SQL Server Express.
# Platform Server is installed manually after apply (needs a licence), see README.md.

# Windows Server 2022 + SQL Server 2022 Express, licence included.
data "aws_ssm_parameter" "windows_sql_express" {
  name = "/aws/service/ami-windows-latest/Windows_Server-2022-English-Full-SQL_2022_Express"
}

resource "aws_vpc" "this" {
  #checkov:skip=CKV2_AWS_11:Flow logs left out for the PoC, see README.
  cidr_block           = "10.0.0.0/24"
  enable_dns_support   = true
  enable_dns_hostnames = true
  tags                 = { Name = var.name }
}

resource "aws_internet_gateway" "this" {
  vpc_id = aws_vpc.this.id
  tags   = { Name = var.name }
}

resource "aws_subnet" "public" {
  vpc_id                  = aws_vpc.this.id
  cidr_block              = "10.0.0.0/24"
  availability_zone       = "${var.region}a"
  map_public_ip_on_launch = false # uses the Elastic IP below
  tags                    = { Name = "${var.name}-public" }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.this.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.this.id
  }

  tags = { Name = "${var.name}-public" }
}

resource "aws_route_table_association" "public" {
  subnet_id      = aws_subnet.public.id
  route_table_id = aws_route_table.public.id
}

# Remove all rules from the default security group.
resource "aws_default_security_group" "this" {
  vpc_id = aws_vpc.this.id
}

# HTTPS in from allowed_cidrs only. No RDP, admin access is through SSM.
resource "aws_security_group" "server" {
  name        = "${var.name}-server"
  description = "OutSystems all-in-one server"
  vpc_id      = aws_vpc.this.id

  ingress {
    description = "HTTPS to IIS (OutSystems apps, Service Center, LifeTime)"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = var.allowed_cidrs
  }

  egress {
    description = "HTTPS out: SSM, Windows Update, OutSystems licensing, data.gov.sg API"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    description = "HTTP out: Windows Update and certificate revocation checks"
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = { Name = "${var.name}-server" }
}

# Instance role for SSM (Session Manager, Fleet Manager, patching).
resource "aws_iam_role" "server" {
  name = "${var.name}-server"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ec2.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "ssm" {
  role       = aws_iam_role.server.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_instance_profile" "server" {
  name = "${var.name}-server"
  role = aws_iam_role.server.name
}

resource "aws_instance" "server" {
  ami                     = data.aws_ssm_parameter.windows_sql_express.insecure_value
  instance_type           = var.instance_type
  subnet_id               = aws_subnet.public.id
  vpc_security_group_ids  = [aws_security_group.server.id]
  iam_instance_profile    = aws_iam_instance_profile.server.name
  ebs_optimized           = true
  monitoring              = true
  disable_api_termination = var.termination_protection

  # IMDSv2 only
  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 1
  }

  root_block_device {
    volume_type = "gp3"
    volume_size = var.root_volume_gb
    encrypted   = true
  }

  tags = { Name = var.name }

  lifecycle {
    # don't rebuild the server when AWS publishes a newer AMI
    ignore_changes = [ami]
  }
}

# Static public IP for the DNS record.
resource "aws_eip" "server" {
  domain   = "vpc"
  instance = aws_instance.server.id
  tags     = { Name = var.name }

  depends_on = [aws_internet_gateway.this]
}
