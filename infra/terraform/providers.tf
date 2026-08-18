terraform {
  required_version = "= 1.15.8"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "5.0.1"
    }
    # Used only by ai_foundry.tf: as of azurerm 5.0.1, azurerm_cognitive_account/
    # azurerm_cognitive_deployment don't yet expose allowProjectManagement or
    # modelProviderData, both required for an Anthropic-hosted Claude deployment
    # (see hashicorp/terraform-provider-azurerm#31140) — azapi_resource talks to
    # the same ARM API directly until that support lands.
    azapi = {
      source  = "azure/azapi"
      version = "2.12.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "3.9.0"
    }
    time = {
      source  = "hashicorp/time"
      version = "0.14.0"
    }
  }

  # resource_group_name / storage_account_name / container_name / key are
  # supplied via `-backend-config` flags at `terraform init` time (see
  # infra/README.md and .github/workflows/backend-ci-cd.yml) so the storage
  # account name never has to be hardcoded here. use_azuread_auth means the
  # same OIDC-derived Azure login is used to read/write state — no storage
  # account key needed as a secret anywhere.
  backend "azurerm" {
    use_azuread_auth = true
  }
}

provider "azurerm" {
  features {}
}
