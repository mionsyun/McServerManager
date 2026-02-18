resource "random_string" "suffix" {
  length  = 6
  lower   = true
  upper   = false
  numeric = true
  special = false
}

locals {
  default_resource_group_name = "${var.prefix}-${var.environment}-rg"
  resolved_resource_group     = var.resource_group_name != "" ? var.resource_group_name : local.default_resource_group_name
  default_static_web_app_name = "${var.prefix}-${var.environment}-lp-${random_string.suffix.result}"
  resolved_static_web_app     = var.static_web_app_name != "" ? var.static_web_app_name : local.default_static_web_app_name

  common_tags = merge(
    {
      project     = "McServerManager"
      environment = var.environment
      managedBy   = "terraform"
      workload    = "landing-page"
    },
    var.tags
  )
}

resource "azurerm_resource_group" "lp" {
  name     = local.resolved_resource_group
  location = var.location
  tags     = local.common_tags
}

resource "azurerm_static_web_app" "lp" {
  name                = local.resolved_static_web_app
  resource_group_name = azurerm_resource_group.lp.name
  location            = azurerm_resource_group.lp.location
  sku_tier            = var.static_web_app_sku_tier
  sku_size            = var.static_web_app_sku_size
  tags                = local.common_tags
}

resource "azurerm_static_web_app_custom_domain" "lp" {
  count = var.custom_domain_name == "" ? 0 : 1

  static_web_app_id = azurerm_static_web_app.lp.id
  domain_name       = var.custom_domain_name
  validation_type   = var.custom_domain_validation_type
}
