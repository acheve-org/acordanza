locals {
  db_connection_string = "Host=${azurerm_postgresql_flexible_server.main.fqdn};Port=5432;Database=${azurerm_postgresql_flexible_server_database.app.name};Username=${var.postgres_admin_username};Password=${var.postgres_admin_password};Ssl Mode=Require;Trust Server Certificate=true"
}

# A user-assigned identity (rather than the container app's own
# system-assigned one) so AcrPull can be granted *before* the container app
# exists. With a system-assigned identity, the principal ID only exists once
# the container app resource is created, so the role assignment can only
# happen afterwards — but Azure validates the registry credential as part of
# the very first revision's creation, so that first apply always times out
# ("Operation expired") waiting on a role assignment that hasn't happened yet.
# See https://github.com/hashicorp/terraform-provider-azurerm/issues/21242.
resource "azurerm_user_assigned_identity" "container_app" {
  name                = "id-${local.name_prefix}"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.container_app.principal_id
}

# Azure RBAC assignments can take a short while to propagate; without this,
# the container app can still hit the same race the role assignment was
# meant to fix.
resource "time_sleep" "acr_pull_propagation" {
  depends_on      = [azurerm_role_assignment.acr_pull]
  create_duration = "30s"
}

resource "azurerm_container_app" "main" {
  name                         = "ca-${local.name_prefix}"
  resource_group_name          = data.azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"

  depends_on = [time_sleep.acr_pull_propagation]

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.container_app.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.container_app.id
  }

  secret {
    name  = "jwt-key"
    value = var.jwt_key
  }

  secret {
    name  = "db-connection-string"
    value = local.db_connection_string
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "http"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = var.container_app_min_replicas
    max_replicas = var.container_app_max_replicas

    container {
      name = "api"
      # Real image tags are deployed out-of-band by CI (`az containerapp
      # update`) after each push to main — see .github/workflows/backend-ci-cd.yml.
      # Terraform only owns the *infra*, not the running image, so it
      # ignores drift on this field after the first apply.
      image  = var.bootstrap_image
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "ASPNETCORE_HTTP_PORTS"
        value = "8080"
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      env {
        name  = "Jwt__Issuer"
        value = var.jwt_issuer
      }

      env {
        name  = "Jwt__Audience"
        value = var.jwt_audience
      }

      env {
        name        = "Jwt__Key"
        secret_name = "jwt-key"
      }

      env {
        name        = "ConnectionStrings__Default"
        secret_name = "db-connection-string"
      }

      dynamic "env" {
        for_each = { for idx, origin in var.cors_allowed_origins : idx => origin }
        content {
          name  = "Cors__AllowedOrigins__${env.key}"
          value = env.value
        }
      }
    }
  }

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}
