# Azure AI Foundry resource + three Claude deployments for the worker:
# claude-opus-5 for enrichment's search-and-extract call (RouteEnrichmentService,
# plan 18), claude-haiku-4-5 for both cheap vision checks (enrichment's topo
# verification and topo import's gate), and claude-sonnet-5 for topo import's
# high-resolution extraction (RouteImportService, plan 30). All three are hosted
# on the same Foundry account (kind = AIServices).
#
# IMPORTANT: applying this auto-accepts the Anthropic Marketplace offer terms
# via the modelProviderData block on each deployment below (see
# https://www.anthropic.com/legal/commercial-terms) — set
# var.claude_organization_name/claude_country_code/claude_industry to the
# real values before the first apply; they're sent to Anthropic with every
# request this app makes, not just at provisioning time.
#
# azapi_resource, not azurerm_cognitive_account/azurerm_cognitive_deployment:
# as of azurerm 5.0.1 neither exposes allowProjectManagement (account) or
# modelProviderData (deployment), both required here — see
# hashicorp/terraform-provider-azurerm#31140. This mirrors the pattern
# Microsoft's own reference implementation uses
# (https://github.com/Azure-Samples/claude/blob/main/infra-terraform/infra/main.tf).
locals {
  ai_foundry_account_name = "aif-${local.name_prefix}${random_string.suffix.result}"
}

resource "azapi_resource" "ai_foundry" {
  type      = "Microsoft.CognitiveServices/accounts@2025-06-01"
  name      = local.ai_foundry_account_name
  parent_id = data.azurerm_resource_group.main.id
  location  = var.ai_foundry_location

  body = {
    kind = "AIServices"
    sku = {
      name = "S0"
    }
    properties = {
      customSubDomainName = local.ai_foundry_account_name
      # Lets the account carry Foundry projects, matching Microsoft's reference
      # implementation (linked above) — this is the property the top-of-file
      # comment cites as a reason for azapi_resource over azurerm_cognitive_account,
      # and it was never actually set until now. It is NOT what gates the
      # server-side tools: that is the deployment's model.version (see below).
      allowProjectManagement = true
      publicNetworkAccess    = "Enabled"
      # No local (API key) auth — the worker and API both authenticate via
      # the container app's managed identity (Entra ID token), matching the
      # "no stored API key" decision in the plan.
      disableLocalAuth = true
    }
  }
}

# GlobalStandard, "Hosted on Anthropic" (not "Hosted on Azure") — required:
# structured outputs and the server-side web search/fetch tools 400 on
# Azure-hosted deployments as of mid-2026 (see RouteEnrichmentService's
# search call).
#
# model.version — NOT model.format — is what selects the hosting type:
# version "1" is Hosted on Anthropic, version "2" is Hosted on Azure, and
# Azure assigns version 2 by default when none is pinned. `format` only names
# the publisher, so the original config (format set, version omitted) silently
# produced an Azure-hosted deployment, and every enrichment run 400'd with
# "code_execution, structured_outputs, web_fetch, web_search not supported in
# your workspace" — a message about the deployment's capability set, not about
# account-level permissions. Azure-hosted carries only Microsoft's documented
# "core capability set"; the server-side tools live on Anthropic-hosted:
# https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-hosting-comparison
#
# versionUpgradeOption is NoAutoUpgrade for the same reason: on
# OnceNewDefaultVersionAvailable, Azure moves the deployment to whatever the
# new default version is — i.e. back to the Azure-hosted variant — which would
# re-break enrichment silently, with no diff in this file to explain it.
#
# claude-opus-5, replacing the claude-sonnet-4-5 deployment this account
# originally carried. That pin existed only because the subscription had 0
# GlobalStandard TPM quota for claude-sonnet-5; opus-5 quota is available, so
# the workaround is gone rather than merely relaxed. The model choice is not
# incidental to the search-and-extract call it serves (plan 29 §5): on
# sonnet-4-5 that call's `web_search_20260209` tool ran without its dynamic
# filtering (server-side, 4.6-and-later only), and `output_config.effort` was
# skipped altogether by the ClaudeModelCapabilities guard, since sonnet-4-5
# 400s on it. Both now apply. Keep the deployment name equal to the model name: the
# worker passes it straight through as MessageCreateParams.Model
# (IRouteEnrichmentProvider.cs), so a mismatch is a 404 at request time.
#
# capacity = 1 carries over from the sonnet deployment, which sustained this
# workload's rate. The search loop is token-heavy per request, so if the job
# starts logging 429s from Foundry, capacity is the first thing to raise.
resource "azapi_resource" "claude_opus" {
  type                      = "Microsoft.CognitiveServices/accounts/deployments@2025-10-01-preview"
  name                      = "claude-opus-5"
  parent_id                 = azapi_resource.ai_foundry.id
  schema_validation_enabled = false # required to allow modelProviderData

  body = {
    sku = {
      name     = "GlobalStandard"
      capacity = 1
    }
    properties = {
      model = {
        format  = "Anthropic"
        name    = "claude-opus-5"
        version = "1" # Hosted on Anthropic — see the comment above
      }
      modelProviderData = {
        organizationName = var.claude_organization_name
        countryCode      = var.claude_country_code
        industry         = var.claude_industry
      }
      versionUpgradeOption = "NoAutoUpgrade"
    }
  }
}

# Serves two callers now: enrichment's topo verification (plan 18) and topo
# import's gate (plan 30 §3). Also Hosted on Anthropic, for a narrower reason
# than claude_opus: neither uses server-side tools, but both use structured outputs
# (ClaudeTopoVerificationProvider's JsonOutputFormat), which is on the same
# Azure-hosted exclusion list. Pinning only the opus deployment would leave
# every topo verification 400ing.
#
# Note the version is "20251001", not "1" — the Anthropic-hosted version id is
# per-model, not a convention, and claude-haiku-4-5 has no version "1" at all
# (an apply would fail outright). Confirm the right value against the catalog
# rather than copying opus's, since the hosting type is what actually matters:
#
#   az rest --method get --url "https://management.azure.com/subscriptions/\
#   $SUB/providers/Microsoft.CognitiveServices/locations/swedencentral/\
#   models?api-version=2025-10-01-preview"
#
# and take the version whose capabilities carry `"hostedOn": "anthropic"`.
resource "azapi_resource" "claude_haiku" {
  type                      = "Microsoft.CognitiveServices/accounts/deployments@2025-10-01-preview"
  name                      = "claude-haiku-4-5"
  parent_id                 = azapi_resource.ai_foundry.id
  schema_validation_enabled = false

  body = {
    sku = {
      name     = "GlobalStandard"
      capacity = 1
    }
    properties = {
      model = {
        format  = "Anthropic"
        name    = "claude-haiku-4-5"
        version = "20251001" # Hosted on Anthropic — see the comment above
      }
      modelProviderData = {
        organizationName = var.claude_organization_name
        countryCode      = var.claude_country_code
        industry         = var.claude_industry
      }
      versionUpgradeOption = "NoAutoUpgrade"
    }
  }

  # Foundry serializes deployment creation per account — chaining avoids a
  # 409 from both deployments landing concurrently on the first apply.
  depends_on = [azapi_resource.claude_opus]
}

# The route-import extraction calls (plan 30 §3 and plan 31 §5, Acordanza.Worker's
# ClaudeTopoExtractionProvider for a photo, ClaudePageExtractionProvider for a
# page). Hosted on Anthropic for the same reason as
# claude_haiku: it uses structured outputs, which is on the Azure-hosted
# exclusion list, even though it uses no server-side tools.
#
# Sonnet 5 rather than reusing the haiku deployment the gate runs on, and the
# reason is resolution, not intelligence: Claude downsamples images server-side
# to a per-tier long edge — 1568 px on haiku-4-5, 2576 px on sonnet-5. For the
# gate's binary "is there a line drawn on this rock" 1568 px is plenty; for
# reading «La Loca 6b+ 25m» off the small print of a guidebook page it is very
# possibly the difference between a correct extraction and a garbled one. The
# split falls out of the tiers rather than out of taste. Plan 31's page
# extraction shares this deployment and adds none of its own: reading Spanish
# prose and deciding which route a post is principally about is a comprehension
# judgment worth the same tier, and it carries no image at all.
#
# VERIFY THE VERSION BEFORE THE FIRST APPLY. "1" follows claude_opus's
# same-generation value, but the Anthropic-hosted version id is per-model, not a
# convention — claude-haiku-4-5's is "20251001" and has no version "1" at all, so
# a wrong value here fails the apply outright. Confirm against the catalog with
# the `az rest` query documented on claude_haiku above and take the version whose
# capabilities carry `"hostedOn": "anthropic"`.
#
# Keep the deployment name equal to the model name: the worker passes it straight
# through as MessageCreateParams.Model, so a mismatch is a 404 at request time.
resource "azapi_resource" "claude_sonnet" {
  type                      = "Microsoft.CognitiveServices/accounts/deployments@2025-10-01-preview"
  name                      = "claude-sonnet-5"
  parent_id                 = azapi_resource.ai_foundry.id
  schema_validation_enabled = false

  body = {
    sku = {
      name     = "GlobalStandard"
      capacity = 1
    }
    properties = {
      model = {
        format  = "Anthropic"
        name    = "claude-sonnet-5"
        version = "1" # Hosted on Anthropic — see the comment above
      }
      modelProviderData = {
        organizationName = var.claude_organization_name
        countryCode      = var.claude_country_code
        industry         = var.claude_industry
      }
      versionUpgradeOption = "NoAutoUpgrade"
    }
  }

  # Keeps the per-account creation chain serialized, same as claude_haiku.
  depends_on = [azapi_resource.claude_haiku]
}

# Grants the container app's managed identity (used by both the API and the
# enrichment Container Apps Job — see container_app_job.tf) the least-privilege
# inference role: Microsoft.CognitiveServices/accounts/MaaS/* data actions,
# nothing else. This is also the role each contributor running the worker
# (`dotnet run` from backend/Acordanza.Worker/) locally needs on their own
# `az login` identity — see backend/CLAUDE.md's local-dev Foundry RBAC prerequisite.
resource "azurerm_role_assignment" "foundry_cognitive_services_user" {
  scope                = azapi_resource.ai_foundry.id
  role_definition_name = "Cognitive Services User"
  principal_id         = azurerm_user_assigned_identity.container_app.principal_id
}
