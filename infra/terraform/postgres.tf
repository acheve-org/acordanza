# Public access + "allow Azure services" firewall rule rather than VNet
# integration: Container Apps Consumption-plan egress doesn't have a stable
# IP without a VNet + NAT gateway, which is more infra than a small
# friends-only app needs. Traffic is still authenticated (password) and
# TLS-encrypted; tightening this to a private endpoint is a reasonable
# future step if the trust model ever changes. The first trivy:ignore below
# accepts that documented risk so the CI security scan stays green — see
# "Security scanning" in infra/README.md.
#
# AZU-0021/0026 are scanner false positives, not accepted risks: Trivy's
# flexible-server adapter only recognizes the legacy single-server parameter
# name "connection_throttling" and the MySQL parameter name "tls_version",
# but this server sets the real Postgres Flexible Server equivalents
# ("connection_throttle.enable" where the version still supports it,
# "ssl_min_protocol_version") below.
#trivy:ignore:AVD-AZU-0022
#trivy:ignore:AVD-AZU-0021
#trivy:ignore:AVD-AZU-0026
resource "azurerm_postgresql_flexible_server" "main" {
  name                = "psql-${local.name_prefix}-${random_string.suffix.result}"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location

  version  = var.postgres_version
  sku_name = var.postgres_sku_name

  storage_mb = var.postgres_storage_mb

  administrator_login    = var.postgres_admin_username
  administrator_password = var.postgres_admin_password

  public_network_access_enabled = true

  backup_retention_days = 7

  lifecycle {
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "allow_azure_services" {
  name             = "AllowAllAzureServices"
  server_id        = azurerm_postgresql_flexible_server.main.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# Hardened server parameters. Azure's defaults are already sane
# (require_secure_transport and log_checkpoints default to on) but setting
# them explicitly means a portal-side change can't silently weaken them,
# and Terraform documents the intended posture.

# Refuse any client connection that isn't TLS. Defaults to on; pinned so it
# can never be flipped off out-of-band without showing up as drift.
resource "azurerm_postgresql_flexible_server_configuration" "require_secure_transport" {
  name      = "require_secure_transport"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "on"
}

# Floor the TLS version at 1.2 (the CIS-benchmark minimum). Not 1.3:
# Flexible Server's TLS 1.3 support varies by Postgres version/region, and
# getting it wrong severs the API's only path to the database.
resource "azurerm_postgresql_flexible_server_configuration" "ssl_min_protocol_version" {
  name      = "ssl_min_protocol_version"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "TLSv1.2"
}

# Log every connection/disconnection — on a publicly reachable server this
# is the audit trail for spotting brute-force attempts in Log Analytics.
resource "azurerm_postgresql_flexible_server_configuration" "log_connections" {
  name      = "log_connections"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "on"
}

resource "azurerm_postgresql_flexible_server_configuration" "log_disconnections" {
  name      = "log_disconnections"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "on"
}

resource "azurerm_postgresql_flexible_server_configuration" "log_checkpoints" {
  name      = "log_checkpoints"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "on"
}

# Temporarily bans hosts that keep failing authentication — cheap
# brute-force mitigation for a password-authenticated public endpoint.
# Azure removed the connection_throttle.* parameters in PostgreSQL 18,
# so this only exists on older server versions.
resource "azurerm_postgresql_flexible_server_configuration" "connection_throttling" {
  count = tonumber(var.postgres_version) < 18 ? 1 : 0

  name      = "connection_throttle.enable"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "on"
}

resource "azurerm_postgresql_flexible_server_database" "app" {
  name      = var.database_name
  server_id = azurerm_postgresql_flexible_server.main.id
  collation = "en_US.utf8"
  charset   = "utf8"
}
