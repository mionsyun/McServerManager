# インストーラー配布用 Azure Blob Storage
#
# アップロード自体は .github/workflows/deploy-installer-to-storage.yml が
# GitHub Secret AZURE_STORAGE_CONNECTION_STRING を使って行う（このファイルでは扱わない）。
# コンテナ名 "public" と blob プレフィックス "downloads/" はワークフロー・installer/build.ps1・landing に
# ハードコードされているため、ここでも固定値として扱う。

locals {
  installer_storage_resource_group = var.installer_storage_resource_group_name != "" ? var.installer_storage_resource_group_name : azurerm_resource_group.lp.name
  installer_storage_location       = var.installer_storage_location != "" ? var.installer_storage_location : azurerm_resource_group.lp.location
  installer_storage_container_name = "public"
  installer_storage_blob_prefix    = "downloads"
}

resource "azurerm_storage_account" "installer" {
  name                     = var.installer_storage_account_name
  resource_group_name      = local.installer_storage_resource_group
  location                 = local.installer_storage_location
  account_tier             = var.installer_storage_account_tier
  account_replication_type = var.installer_storage_account_replication_type
  account_kind             = var.installer_storage_account_kind

  min_tls_version = "TLS1_2"

  # public コンテナ（匿名 blob 読み取り）でインストーラーを配布するため必須
  allow_nested_items_to_be_public = true

  # GitHub Actions が接続文字列（アカウントキー）でアップロードしているため無効化しない
  shared_access_key_enabled = true

  tags = merge(local.common_tags, {
    workload = "installer-distribution"
  })

  lifecycle {
    # 配布中のインストーラーが消えないよう、再作成・削除を伴う plan はエラーにする。
    # 本当に作り直す場合は一時的にこの行を外してから apply する。
    prevent_destroy = true
  }
}

resource "azurerm_storage_container" "public" {
  name = local.installer_storage_container_name
  # azurerm 4.9 以降は storage_account_name（データプレーン）ではなく
  # storage_account_id（Resource Manager）を使う
  storage_account_id    = azurerm_storage_account.installer.id
  container_access_type = "blob"

  lifecycle {
    # コンテナ削除 = 配布中インストーラーの全削除になるため保護する
    prevent_destroy = true
  }
}
