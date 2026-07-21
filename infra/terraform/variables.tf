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

variable "cors_allowed_origins" {
  description = "Additional origins allowed to call the API, beyond the deployed frontend Container App's own URL (which is added automatically) — e.g. a future custom domain."
  type        = list(string)
  default     = []
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
  type    = number
  default = 2
}

variable "container_app_web_min_replicas" {
  type    = number
  default = 0
}

variable "container_app_web_max_replicas" {
  type    = number
  default = 2
}
