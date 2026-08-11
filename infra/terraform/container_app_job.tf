# Event-driven Container Apps Job running the same ACR image as the API
# (args = ["process-enrichment"]) — see Program.cs's process-enrichment CLI
# branch. KEDA fires an execution when the route-enrichment queue has
# messages; the execution drains it and exits, and the environment scales
# back to zero — a worker that costs nothing while idle, which is the
# decision plan 18 makes over a second, always-on process.
resource "azurerm_container_app_job" "enrichment" {
  name                         = "caj-${local.name_prefix}-enrichment"
  resource_group_name          = data.azurerm_resource_group.main.name
  location                     = data.azurerm_resource_group.main.location
  container_app_environment_id = azurerm_container_app_environment.main.id

  replica_timeout_in_seconds = 600 # covers the worst case: 8 messages x ~1 min each
  replica_retry_limit        = 0   # retries are queue-level (visibility timeout), not job-level
  workload_profile_name      = null

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
    name  = "db-connection-string"
    value = local.db_connection_string
  }

  secret {
    name  = "grafana-otlp-headers"
    value = var.grafana_otlp_headers
  }

  # Scaler-only credential: as of azurerm 5.0.1 the provider doesn't yet
  # expose an identity on a job's azure-queue KEDA trigger (the app code
  # itself never uses this — RouteEnrichmentQueue/EnrichmentQueueProcessor
  # authenticate via the managed identity like everything else — see
  # hashicorp/terraform-provider-azurerm#26570/#31530). A connection string
  # scoped to this one storage account is the least-bad fallback until that
  # lands; revisit and drop this secret once scale-rule identity ships.
  secret {
    name  = "queue-scaler-connection-string"
    value = "DefaultEndpointsProtocol=https;AccountName=${azurerm_storage_account.topos.name};AccountKey=${azurerm_storage_account.topos.primary_access_key};EndpointSuffix=core.windows.net"
  }

  event_trigger_config {
    parallelism              = 1
    replica_completion_count = 1

    scale {
      min_executions              = 0
      max_executions              = 1
      polling_interval_in_seconds = 30

      rules {
        name             = "route-enrichment-queue"
        custom_rule_type = "azure-queue"
        metadata = {
          queueName   = azurerm_storage_queue.route_enrichment.name
          accountName = azurerm_storage_account.topos.name
          queueLength = "1"
        }
        authentication {
          secret_name       = "queue-scaler-connection-string"
          trigger_parameter = "connection"
        }
      }
    }
  }

  template {
    container {
      name = "worker"
      # Real image tags are deployed out-of-band by CI, same as the API —
      # see .github/workflows/ci-cd.yml, which must roll this job alongside
      # azurerm_container_app.main so the two never run different models of
      # the schema.
      image  = var.worker_bootstrap_image
      cpu    = 0.5
      memory = "1.0Gi"
      args   = ["process-enrichment"]

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      env {
        name        = "ConnectionStrings__Default"
        secret_name = "db-connection-string"
      }

      env {
        name  = "Storage__AccountName"
        value = azurerm_storage_account.topos.name
      }

      env {
        name  = "Storage__ContainerName"
        value = azurerm_storage_container.topos.name
      }

      env {
        name  = "Storage__DataProtectionContainerName"
        value = azurerm_storage_container.dataprotection.name
      }

      env {
        name  = "Storage__EnrichmentQueueName"
        value = azurerm_storage_queue.route_enrichment.name
      }

      env {
        name  = "Storage__ManagedIdentityClientId"
        value = azurerm_user_assigned_identity.container_app.client_id
      }

      # Not a secret — see the plan's decision (§6): this is just a resource
      # name used to build the Foundry endpoint URL. Auth is Entra ID via
      # the managed identity above, never a stored API key.
      env {
        name  = "Anthropic__FoundryResource"
        value = local.ai_foundry_account_name
      }

      env {
        name  = "OTEL_EXPORTER_OTLP_ENDPOINT"
        value = var.grafana_otlp_endpoint
      }

      env {
        name        = "OTEL_EXPORTER_OTLP_HEADERS"
        secret_name = "grafana-otlp-headers"
      }

      env {
        name  = "OTEL_EXPORTER_OTLP_PROTOCOL"
        value = "http/protobuf"
      }

      env {
        name  = "OTEL_RESOURCE_ATTRIBUTES"
        value = "deployment.environment=production,service.name=acordanza-enrichment-worker"
      }
    }
  }

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}
