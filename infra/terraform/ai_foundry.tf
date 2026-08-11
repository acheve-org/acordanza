# Azure AI Foundry resource + two Claude deployments for the route
# enrichment worker (plan 18): claude-sonnet-5 for the search-and-extract
# call (RouteEnrichmentService), claude-haiku-4-5 for the topo vision check.
# Both deployments are hosted on the same Foundry account (kind = AIServices).
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
      publicNetworkAccess = "Enabled"
      # No local (API key) auth — the worker and API both authenticate via
      # the container app's managed identity (Entra ID token), matching the
      # "no stored API key" decision in the plan.
      disableLocalAuth = true
    }
  }
}

# GlobalStandard, "Hosted on Anthropic" (not "Hosted on Azure") — required:
# structured outputs and the server-side web search tool 400 on
# Azure-hosted deployments as of mid-2026 (see RouteEnrichmentService's
# search call). model.format = "Anthropic" is what selects that hosting type.
resource "azapi_resource" "claude_sonnet" {
  type                      = "Microsoft.CognitiveServices/accounts/deployments@2025-10-01-preview"
  name                      = "claude-sonnet-5"
  parent_id                 = azapi_resource.ai_foundry.id
  schema_validation_enabled = false # required to allow modelProviderData

  body = {
    sku = {
      name     = "GlobalStandard"
      capacity = 1
    }
    properties = {
      model = {
        format = "Anthropic"
        name   = "claude-sonnet-5"
      }
      modelProviderData = {
        organizationName = var.claude_organization_name
        countryCode      = var.claude_country_code
        industry         = var.claude_industry
      }
      versionUpgradeOption = "OnceNewDefaultVersionAvailable"
    }
  }
}

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
        format = "Anthropic"
        name   = "claude-haiku-4-5"
      }
      modelProviderData = {
        organizationName = var.claude_organization_name
        countryCode      = var.claude_country_code
        industry         = var.claude_industry
      }
      versionUpgradeOption = "OnceNewDefaultVersionAvailable"
    }
  }

  # Foundry serializes deployment creation per account — chaining avoids a
  # 409 from both deployments landing concurrently on the first apply.
  depends_on = [azapi_resource.claude_sonnet]
}

# Grants the container app's managed identity (used by both the API and the
# enrichment Container Apps Job — see container_app_job.tf) the least-privilege
# inference role: Microsoft.CognitiveServices/accounts/MaaS/* data actions,
# nothing else. This is also the role each contributor running
# `dotnet run -- process-enrichment` locally needs on their own `az login`
# identity — see backend/CLAUDE.md's local-dev Foundry RBAC prerequisite.
resource "azurerm_role_assignment" "foundry_cognitive_services_user" {
  scope                = azapi_resource.ai_foundry.id
  role_definition_name = "Cognitive Services User"
  principal_id         = azurerm_user_assigned_identity.container_app.principal_id
}
