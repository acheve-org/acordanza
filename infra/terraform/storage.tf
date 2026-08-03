# Blob storage for route "topo" images (see backend/Acordanza.Api/Services/BlobStorageService.cs).
# The container app authenticates via its existing user-assigned managed
# identity (RBAC role assignment below) — no storage account key is ever
# generated or stored, consistent with this repo's "no long-lived
# credential" posture already used for ACR and the Terraform state backend.
#
# Public network access (no network_rules block, so default_action is
# implicitly "Allow") mirrors the exact trade-off already accepted for
# Postgres in postgres.tf: Container Apps Consumption egress has no stable
# IP without a VNet + NAT gateway, which is more infra than this app needs.
# Every blob read/write still requires a valid Azure AD token (managed
# identity) or a short-lived user delegation SAS — there's no anonymous
# path in, since allow_nested_items_to_be_public is false below.
#
# LRS (not GRS/ZRS), Microsoft-managed keys (not customer-managed), and no
# infrastructure (double) encryption are the same kind of accepted
# cost/complexity trade-offs already documented for Postgres' backups and
# ACR's Basic tier in infra/README.md's Checkov section — topo photos for a
# handful of friends don't warrant enterprise-grade redundancy/encryption
# tooling. Storage Analytics logging is skipped for the same reason.
#trivy:ignore:AVD-AZU-0012
#trivy:ignore:AVD-AZU-0057
#trivy:ignore:AVD-AZU-0058
#trivy:ignore:AVD-AZU-0060
#trivy:ignore:AVD-AZU-0061
resource "azurerm_storage_account" "topos" {
  # Storage account names must be globally unique, lowercase alphanumeric
  # only, no hyphens, <=24 chars — "stacordanzaprod" + 6-char suffix is 21.
  name                = "stacordanzaprod${random_string.suffix.result}"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location

  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  # No anonymous/public blob access at the account level — every read goes
  # through a backend-issued, short-lived user delegation SAS URL instead.
  allow_nested_items_to_be_public = false
}

resource "azurerm_storage_container" "topos" {
  name                  = "topos"
  storage_account_id    = azurerm_storage_account.topos.id
  container_access_type = "private"
}

# ASP.NET Core Data Protection keys (see Program.cs) — a separate container
# from "topos" since it's unrelated data, but the same storage account, so
# the role assignment below (scoped to the whole account) already covers it;
# no separate RBAC grant needed. Without this, keys default to the
# container's local disk, which doesn't survive Container Apps' scale-to-zero
# restarts — every cold start would mint a fresh key ring and invalidate any
# password-reset token issued before the previous restart.
resource "azurerm_storage_container" "dataprotection" {
  name                  = "dataprotection-keys"
  storage_account_id    = azurerm_storage_account.topos.id
  container_access_type = "private"
}

resource "azurerm_role_assignment" "topos_blob_contributor" {
  scope                = azurerm_storage_account.topos.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.container_app.principal_id
}
