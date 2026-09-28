terraform {
  required_version = ">= 1.7.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}

provider "azurerm" {
  subscription_id = var.subscription_id
  tenant_id       = var.tenant_id
  features {}
}

# インストーラー配布用ストレージ（storage.tf）は別サブスクリプションにあるため専用の provider を使う
provider "azurerm" {
  alias           = "installer_storage"
  subscription_id = var.installer_storage_subscription_id
  tenant_id       = var.tenant_id
  features {}
}
