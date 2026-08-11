output "resource_group_name" {
  value = data.azurerm_resource_group.main.name
}

output "container_app_name" {
  value = azurerm_container_app.main.name
}

output "container_app_fqdn" {
  # ingress[0].fqdn is the stable, revision-independent app hostname.
  # latest_revision_fqdn is per-revision and goes stale as soon as a newer
  # revision is deployed and the old one deactivates (single revision mode).
  value = azurerm_container_app.main.ingress[0].fqdn
}

output "container_app_frontend_name" {
  value = azurerm_container_app.frontend.name
}

output "container_app_frontend_fqdn" {
  value = azurerm_container_app.frontend.ingress[0].fqdn
}

output "backend_custom_domain" {
  value = var.backend_custom_domain
}

output "frontend_custom_domain" {
  value = var.frontend_custom_domain
}

# Shared by every Container App in the environment. Needed for the
# "asuid.<domain>" TXT records — see infra/README.md.
output "custom_domain_verification_id" {
  value     = azurerm_container_app.main.custom_domain_verification_id
  sensitive = true
}

output "acr_name" {
  value = azurerm_container_registry.main.name
}

output "acr_login_server" {
  value = azurerm_container_registry.main.login_server
}

output "postgres_fqdn" {
  value = azurerm_postgresql_flexible_server.main.fqdn
}

output "topos_storage_account_name" {
  value = azurerm_storage_account.topos.name
}

output "container_app_job_enrichment_name" {
  value = azurerm_container_app_job.enrichment.name
}

output "static_web_app_name" {
  value = azurerm_static_web_app.landing.name
}

output "static_web_app_default_host_name" {
  value = azurerm_static_web_app.landing.default_host_name
}

output "landing_custom_domain" {
  value = var.landing_custom_domain
}

# Goes into the apex domain's TXT record (see infra/README.md). Only
# populated until Azure finishes validating the domain, then cleared.
output "landing_custom_domain_validation_token" {
  value     = azurerm_static_web_app_custom_domain.landing.validation_token
  sensitive = true
}

# Deployment token for the GitHub Actions SWA deploy action
# (Azure/static-web-apps-deploy@v1). Static Web Apps doesn't support the
# OIDC-based `az` deploy the Container Apps use — this token is the only
# supported non-interactive auth for that action, so it has to be stored as
# a GitHub Actions secret rather than reusing the existing OIDC federation.
output "static_web_app_api_key" {
  value     = azurerm_static_web_app.landing.api_key
  sensitive = true
}
