locals {
  name_prefix = "${var.project}-${var.environment}"
}

# ACR and Postgres server names must be globally unique across Azure, so a
# short random suffix is appended to those specifically (not to every
# resource — resource group / app / environment names only need to be
# unique within the subscription).
resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}

# The resource group itself is created out-of-band by scripts/bootstrap-azure.sh,
# not by Terraform. The GitHub Actions service principal's role assignment is
# scoped to this one resource group (least privilege), and a principal scoped
# to a single resource group cannot create new resource groups — that needs
# subscription-level permissions we deliberately don't grant it.
data "azurerm_resource_group" "main" {
  name = "rg-${local.name_prefix}"
}

resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${local.name_prefix}"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_container_app_environment" "main" {
  name                       = "cae-${local.name_prefix}"
  resource_group_name        = data.azurerm_resource_group.main.name
  location                   = data.azurerm_resource_group.main.location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  # azurerm v5 made logs_destination non-Computed, defaulting to empty
  # (streaming-only). Without this, upgrading the provider would silently
  # stop persisting container app logs to the Log Analytics workspace above.
  logs_destination = "log-analytics"
}
