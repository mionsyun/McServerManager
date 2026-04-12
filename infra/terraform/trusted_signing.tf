locals {
  default_trusted_signing_account_name = "${var.prefix}-${var.environment}-tsa"
  resolved_trusted_signing_account     = var.trusted_signing_account_name != "" ? var.trusted_signing_account_name : local.default_trusted_signing_account_name
}

resource "azurerm_trusted_signing_account" "main" {
  name                = local.resolved_trusted_signing_account
  resource_group_name = azurerm_resource_group.lp.name
  location            = var.trusted_signing_location
  sku_name            = var.trusted_signing_sku_name
  tags                = local.common_tags
}

resource "azurerm_trusted_signing_certificate_profile" "main" {
  name                       = var.trusted_signing_certificate_profile_name
  trusted_signing_account_id = azurerm_trusted_signing_account.main.id
  profile_type               = "PublicTrust"
  include_street_address     = false
}
