# Static Web Apps is a separate Azure primitive from Container Apps: a
# free-tier static-file host with its own CDN, no container image or ACR
# involved. It only serves the marketing landing page at the bare apex
# domain (var.landing_custom_domain) — the actual app keeps living on the
# Container Apps in container_app.tf / container_app_frontend.tf.
#
# Static Web Apps is only available in a handful of regions, and
# var.location (Spain Central) isn't one of them — hence the separate
# var.landing_location.
resource "azurerm_static_web_app" "landing" {
  name                = "stapp-${local.name_prefix}-landing"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = var.landing_location
  sku_tier            = "Free"
  sku_size            = "Free"
}

# Apex ("bare") domains can't be CNAMEd without breaking any other record at
# the zone root (e.g. MX), so this must use dns-txt-token validation instead
# of the cname-delegation Container Apps' custom domains use. Unlike the
# Container Apps flow, this doesn't need DNS to exist before the first
# apply: creating this resource generates validation_token (exposed below
# as a sensitive output), which then goes into the TXT record. Azure
# validates asynchronously afterwards — Terraform doesn't wait on it, so
# the domain stays "pending validation" in the Azure portal until DNS
# propagates and Azure's background job picks it up. See infra/README.md
# for the exact DNS records.
resource "azurerm_static_web_app_custom_domain" "landing" {
  static_web_app_id = azurerm_static_web_app.landing.id
  domain_name       = var.landing_custom_domain
  validation_type   = "dns-txt-token"
}
