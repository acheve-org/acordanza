# Name must be globally unique, alphanumeric only (no hyphens).
resource "azurerm_container_registry" "main" {
  name                = "acr${replace(var.project, "-", "")}${random_string.suffix.result}"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
  sku                 = "Basic"
  admin_enabled       = false
}
