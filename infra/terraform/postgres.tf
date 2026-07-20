# Public access + "allow Azure services" firewall rule rather than VNet
# integration: Container Apps Consumption-plan egress doesn't have a stable
# IP without a VNet + NAT gateway, which is more infra than a small
# friends-only app needs. Traffic is still authenticated (password) and
# TLS-encrypted; tightening this to a private endpoint is a reasonable
# future step if the trust model ever changes.
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

resource "azurerm_postgresql_flexible_server_database" "app" {
  name      = var.database_name
  server_id = azurerm_postgresql_flexible_server.main.id
  collation = "en_US.utf8"
  charset   = "utf8"
}
