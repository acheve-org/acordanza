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

output "acr_name" {
  value = azurerm_container_registry.main.name
}

output "acr_login_server" {
  value = azurerm_container_registry.main.login_server
}

output "postgres_fqdn" {
  value = azurerm_postgresql_flexible_server.main.fqdn
}
