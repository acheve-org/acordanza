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
# tooling. Storage Analytics logging is skipped for the same reason — for
# both blob (AVD-AZU-0057/0058/0060/0061) and, since this account also holds
# the route-enrichment queue (plan 18), queue services (AVD-AZU-0009).
#trivy:ignore:AVD-AZU-0009
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

# Route enrichment queue (plan 18) — on the same storage account topo images
# use, not a separate account: one new resource, no new Azurite-equivalent
# infra to reason about.
resource "azurerm_storage_queue" "route_enrichment" {
  name               = "route-enrichment"
  storage_account_id = azurerm_storage_account.topos.id
}

# Route import queue (plans 30 and 31) — a second queue rather than a second
# message shape on route-enrichment: EnrichmentQueueProcessor drops an
# undeserializable message as poison, and the two want different visibility
# timeouts (five minutes for a web-search loop, two for an import). Both import
# kinds — an uploaded topo photo and a pasted URL — ride this one queue: the
# message is a row id, and which reader runs is a column on the row.
resource "azurerm_storage_queue" "route_import" {
  name               = "route-import"
  storage_account_id = azurerm_storage_account.topos.id
}

# Sweeps abandoned route-import uploads (the Image kind only — a Url import
# stages nothing). The API writes each upload to
# "_imports/" and the worker promotes it to the route's own prefix on success
# (or deletes it on a discard) — but a run that crashes in between leaves the
# blob behind, and nothing else would ever collect it. One rule beats cleanup
# code that has to be gotten right on a path that only runs after a failure.
#
# Seven days is well past the queue's own give-up point (five deliveries against
# a two-minute visibility timeout), so this can only ever catch blobs no
# execution is still coming for.
resource "azurerm_storage_management_policy" "topos" {
  storage_account_id = azurerm_storage_account.topos.id

  rule {
    name    = "expire-abandoned-route-imports"
    enabled = true

    filters {
      prefix_match = ["${azurerm_storage_container.topos.name}/_imports/"]
      blob_types   = ["blockBlob"]
    }

    actions {
      base_blob {
        delete_after_days_since_creation_greater_than = 7
      }
    }
  }
}

# The blob role above (Storage Blob Data Contributor) covers blobs only —
# queues need their own data-plane role for the API (enqueue on
# POST /routes/{id}/enrich and POST /route-imports) and the worker (dequeue/delete
# in EnrichmentQueueProcessor/RouteImportQueueProcessor) to operate without a
# stored connection string. Scoped to the account, so it covers both queues.
resource "azurerm_role_assignment" "topos_queue_contributor" {
  scope                = azurerm_storage_account.topos.id
  role_definition_name = "Storage Queue Data Contributor"
  principal_id         = azurerm_user_assigned_identity.container_app.principal_id
}
