# インストーラー配布用 Azure Blob Storage
#
# 既存のストレージアカウント（手動作成）を Terraform 管理下に取り込むための定義。
# アップロード自体は .github/workflows/deploy-installer-to-storage.yml が
# GitHub Secret AZURE_STORAGE_CONNECTION_STRING を使って行う（このファイルでは扱わない）。
# コンテナ名 "public" と blob プレフィックス "downloads/" はワークフロー側にハードコードされているため、
# ここでも固定値として扱う。
#
# 注意: このストレージアカウントは LP / Trusted Signing（既定の azurerm provider、var.subscription_id）とは
# 別サブスクリプション（var.installer_storage_subscription_id）にあるため、
# alias 付き provider "azurerm.installer_storage" でリソース・import の両方を管理する。

locals {
  installer_storage_container_name = "public"
  installer_storage_blob_prefix    = "downloads"

  # import ブロック用の Azure Resource Manager ID
  installer_storage_account_id   = "/subscriptions/${var.installer_storage_subscription_id}/resourceGroups/${var.installer_storage_resource_group_name}/providers/Microsoft.Storage/storageAccounts/${var.installer_storage_account_name}"
  installer_storage_container_id = "${local.installer_storage_account_id}/blobServices/default/containers/${local.installer_storage_container_name}"

  # import ブロックの for_each 用（false のときは import せず新規作成扱い）
  installer_storage_import_keys = var.installer_storage_import_existing ? toset(["existing"]) : toset([])
}

resource "azurerm_storage_account" "installer" {
  provider = azurerm.installer_storage

  name                     = var.installer_storage_account_name
  resource_group_name      = var.installer_storage_resource_group_name
  location                 = var.installer_storage_location
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
    # （name / location / account_tier などの ForceNew 属性が既存値と食い違った場合もここで止まる）
    prevent_destroy = true

    # tags は Terraform で管理する方針のため ignore_changes には入れない。
    # 取り込み時にタグ追加の in-place 差分が出るのは想定どおり。
  }
}

resource "azurerm_storage_container" "public" {
  provider = azurerm.installer_storage

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

# 既存リソースの取り込み（Terraform >= 1.7: import ブロックの for_each を使用）
# 一度 state に取り込まれた後は no-op になるので、ブロックは残しておいてよい。
import {
  for_each = local.installer_storage_import_keys
  provider = azurerm.installer_storage
  to       = azurerm_storage_account.installer
  id       = local.installer_storage_account_id
}

import {
  for_each = local.installer_storage_import_keys
  provider = azurerm.installer_storage
  to       = azurerm_storage_container.public
  id       = local.installer_storage_container_id
}
