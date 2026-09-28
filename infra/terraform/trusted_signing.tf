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

# 証明書プロファイル（PublicTrust）は Terraform では作成しない。
# - azurerm 4.x には証明書プロファイルのリソースが存在しない
# - プロファイル作成には Azure ポータルでの本人確認（Identity validation）の完了が前提で、
#   その ID を Terraform から事前に知る手段がない
# プロファイルはポータルで手動作成し、名前は var.trusted_signing_certificate_profile_name に合わせる（README 参照）。
