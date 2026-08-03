variable "project" {
  description = "Short name used as a prefix for all resource names."
  type        = string
  default     = "acordanza"
}

variable "environment" {
  description = "Deployment environment name, used in resource naming."
  type        = string
  default     = "prod"
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "spaincentral"
}

variable "postgres_admin_username" {
  description = "Admin login for the Postgres Flexible Server."
  type        = string
  default     = "acordanzaadmin"
}

variable "postgres_admin_password" {
  description = "Admin password for the Postgres Flexible Server. Supply via TF_VAR_postgres_admin_password (a GitHub Actions secret) — never commit this."
  type        = string
  sensitive   = true
}

variable "postgres_sku_name" {
  description = "Postgres Flexible Server compute SKU. B_Standard_B1ms is the cheapest burstable tier, plenty for a handful of users."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "postgres_storage_mb" {
  description = "Postgres Flexible Server storage size in MB. 32768 (32GB) is the smallest allowed size."
  type        = number
  default     = 32768
}

variable "postgres_version" {
  description = "Postgres major version."
  type        = string
  default     = "18"
}

variable "database_name" {
  description = "Name of the application database."
  type        = string
  default     = "acordanza"
}

variable "jwt_key" {
  description = "Symmetric signing key for JWTs. Supply via TF_VAR_jwt_key (a GitHub Actions secret) — never commit this. Generate with `openssl rand -base64 48`."
  type        = string
  sensitive   = true
}

variable "jwt_issuer" {
  description = "JWT issuer, must match backend/Acordanza.Api/appsettings.json for local dev."
  type        = string
  default     = "Acordanza.Api"
}

variable "jwt_audience" {
  description = "JWT audience, must match backend/Acordanza.Api/appsettings.json for local dev."
  type        = string
  default     = "Acordanza.Client"
}

variable "resend_api_key" {
  description = "Resend API key used to send password-reset emails. Supply via TF_VAR_resend_api_key (a GitHub Actions secret) — never commit this."
  type        = string
  sensitive   = true
}

variable "resend_from_email" {
  description = "Verified Resend sender address (Resend:FromEmail) password-reset emails are sent from."
  type        = string
  default     = "noreply@acordanza.eu"
}

variable "grafana_otlp_endpoint" {
  description = "Grafana Cloud OTLP gateway endpoint (Connections -> OpenTelemetry in the Grafana Cloud UI) traces/metrics/logs are exported to, e.g. https://otlp-gateway-prod-xx-xxx.grafana.net/otlp."
  type        = string
}

variable "grafana_otlp_headers" {
  description = "OTLP auth header sent with every export, in \"Authorization=Basic <base64 instanceId:token>\" form. Supply via TF_VAR_grafana_otlp_headers (a GitHub Actions secret) — never commit this."
  type        = string
  sensitive   = true
}

variable "cors_allowed_origins" {
  description = "Additional origins allowed to call the API, beyond the deployed frontend Container App's own URL and var.frontend_custom_domain (both added automatically)."
  type        = list(string)
  default     = []
}

variable "frontend_custom_domain" {
  description = "Custom domain bound to the frontend Container App, with a free Azure-managed certificate. Must be a subdomain (CNAME-validated) — see infra/README.md for the DNS records required before this can be applied."
  type        = string
  default     = "app.acordanza.eu"
}

variable "backend_custom_domain" {
  description = "Custom domain bound to the backend (API) Container App, with a free Azure-managed certificate. Must be a subdomain (CNAME-validated) — see infra/README.md for the DNS records required before this can be applied."
  type        = string
  default     = "api.acordanza.eu"
}

variable "fido2_server_domain" {
  description = "WebAuthn/passkey relying party ID (Fido2:ServerDomain). Must be the frontend's origin or a registrable parent of it — not var.backend_custom_domain — since the browser performs the WebAuthn ceremony on the page it's actually on (the frontend). The apex domain covers var.frontend_custom_domain as a subdomain, so passkeys keep working if the frontend's custom domain ever changes to another *.acordanza.eu name."
  type        = string
  default     = "acordanza.eu"
}

variable "landing_custom_domain" {
  description = "Apex domain bound to the landing page Static Web App. Apex domains use dns-txt-token validation, not CNAME — see infra/README.md for the DNS records required."
  type        = string
  default     = "acordanza.eu"
}

variable "landing_location" {
  description = "Azure region for the landing page Static Web App. Static Web Apps is only offered in a handful of regions, none of which is var.location (Spain Central)."
  type        = string
  default     = "westeurope"
}

variable "bootstrap_image" {
  description = "Placeholder public image used only on the very first `terraform apply`, before CI has ever pushed a real image to the ACR. Ignored on every subsequent apply — see the `lifecycle.ignore_changes` on the container app's image."
  type        = string
  default     = "mcr.microsoft.com/azuredocs/containerapps-helloworld:latest"
}

variable "container_app_min_replicas" {
  type    = number
  default = 0
}

variable "container_app_max_replicas" {
  description = "Capped at 1 for now: the backend caches WebAuthn/passkey challenges in-process (IMemoryCache, see PasskeysController/AuthController), which isn't shared across replicas. Raise this again once that cache is backed by something shared (e.g. Postgres or Redis) or ingress sticky sessions are enabled."
  type        = number
  default     = 1
}

variable "container_app_web_min_replicas" {
  type    = number
  default = 0
}

variable "container_app_web_max_replicas" {
  type    = number
  default = 2
}
