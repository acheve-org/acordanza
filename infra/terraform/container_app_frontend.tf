# Shares the API's user-assigned identity (already has AcrPull on the
# registry, plus the propagation delay in time_sleep.acr_pull_propagation)
# rather than provisioning a second identity/role-assignment/sleep for a
# second app pulling from the same registry.
resource "azurerm_container_app" "frontend" {
  name                         = "ca-${local.name_prefix}-web"
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
    min_replicas = var.container_app_web_min_replicas
    max_replicas = var.container_app_web_max_replicas

    container {
      name = "web"
      # Real image tags are deployed out-of-band by CI (`az containerapp
      # update`) after each push to main — see .github/workflows/ci-cd.yml.
      # Terraform only owns the *infra*, not the running image, so it
      # ignores drift on this field after the first apply. VITE_API_URL is
      # baked into the JS bundle at Docker build time, not passed as a
      # runtime env var here.
      image  = var.bootstrap_image
      cpu    = 0.25
      memory = "0.5Gi"
    }
  }

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}
