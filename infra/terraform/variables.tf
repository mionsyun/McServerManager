variable "subscription_id" {
  description = "Azure subscription ID to deploy resources into"
  type        = string
  default     = "c5a6db5e-a2c1-4e3c-8a67-f7e7e4d166a2"
}

variable "tenant_id" {
  description = "Azure AD tenant ID"
  type        = string
  default     = "d546234e-6471-4152-a48b-609d4cc0ecd6"
}

variable "prefix" {
  description = "Resource name prefix"
  type        = string
  default     = "mcsm"
}

variable "environment" {
  description = "Deployment environment label"
  type        = string
  default     = "prod"
}

variable "location" {
  description = "Azure region for resource group"
  type        = string
  default     = "Japan East"
}

variable "resource_group_name" {
  description = "Existing or new resource group name"
  type        = string
  default     = ""
}

variable "static_web_app_name" {
  description = "Static Web App name. Leave empty to auto-generate a unique name."
  type        = string
  default     = ""
}

variable "static_web_app_sku_tier" {
  description = "Static Web App SKU tier"
  type        = string
  default     = "Free"
}

variable "static_web_app_sku_size" {
  description = "Static Web App SKU size"
  type        = string
  default     = "Free"
}

variable "trusted_signing_location" {
  description = "Azure region for Trusted Signing account. Must be a supported region (e.g. East Asia, East US). Japan East is not supported."
  type        = string
  default     = "East Asia"
}

variable "trusted_signing_account_name" {
  description = "Trusted Signing account name. Leave empty to auto-generate."
  type        = string
  default     = ""
}

variable "trusted_signing_certificate_profile_name" {
  description = "Certificate profile name for Trusted Signing (used in CI signing commands)"
  type        = string
  default     = "MaiPilot"
}

variable "trusted_signing_sku_name" {
  description = "Trusted Signing account SKU: Basic or Premium"
  type        = string
  default     = "Basic"

  validation {
    condition     = contains(["Basic", "Premium"], var.trusted_signing_sku_name)
    error_message = "trusted_signing_sku_name must be Basic or Premium."
  }
}

variable "tags" {
  description = "Additional tags"
  type        = map(string)
  default     = {}
}

variable "custom_domain_name" {
  description = "Custom domain to bind to the Static Web App (e.g. lp.example.com). Leave empty to disable."
  type        = string
  default     = ""
}

variable "custom_domain_validation_type" {
  description = "Validation type for custom domain: cname-delegation or dns-txt-token. Apex domains must use dns-txt-token."
  type        = string
  default     = "cname-delegation"

  validation {
    condition     = contains(["cname-delegation", "dns-txt-token"], var.custom_domain_validation_type)
    error_message = "custom_domain_validation_type must be cname-delegation or dns-txt-token."
  }
}

variable "installer_storage_account_name" {
  description = "Storage account name for installer distribution (existing account is imported). Must be 3-24 lowercase letters and digits."
  type        = string
  default     = "stmailpilotje"

  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.installer_storage_account_name))
    error_message = "installer_storage_account_name must be 3-24 characters of lowercase letters and digits."
  }
}

variable "installer_storage_subscription_id" {
  description = "Azure subscription ID that contains the installer storage account. Differs from subscription_id (LP / Trusted Signing); verify which one is current before apply."
  type        = string
  default     = "1456e0ca-79d5-4b67-a5e0-5e98062498fc"

  validation {
    condition     = can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", var.installer_storage_subscription_id))
    error_message = "installer_storage_subscription_id must be a subscription GUID."
  }
}

variable "installer_storage_resource_group_name" {
  description = "Resource group that contains the existing installer storage account (in installer_storage_subscription_id). Required: check with `az storage account show --name <account> --subscription <id> --query resourceGroup`."
  type        = string

  validation {
    condition     = length(trimspace(var.installer_storage_resource_group_name)) > 0
    error_message = "installer_storage_resource_group_name must not be empty."
  }
}

variable "installer_storage_location" {
  description = "Azure region of the installer storage account. Must match the existing account (changing it forces replacement)."
  type        = string
  default     = "Japan East"
}

variable "installer_storage_account_tier" {
  description = "Installer storage account tier: Standard or Premium. Must match the existing account (changing it forces replacement)."
  type        = string
  default     = "Standard"

  validation {
    condition     = contains(["Standard", "Premium"], var.installer_storage_account_tier)
    error_message = "installer_storage_account_tier must be Standard or Premium."
  }
}

variable "installer_storage_account_replication_type" {
  description = "Installer storage account replication type: LRS, GRS, RAGRS, ZRS, GZRS or RAGZRS. Must match the existing account."
  type        = string
  default     = "LRS"

  validation {
    condition     = contains(["LRS", "GRS", "RAGRS", "ZRS", "GZRS", "RAGZRS"], var.installer_storage_account_replication_type)
    error_message = "installer_storage_account_replication_type must be one of LRS, GRS, RAGRS, ZRS, GZRS, RAGZRS."
  }
}

variable "installer_storage_account_kind" {
  description = "Installer storage account kind: StorageV2, BlobStorage, BlockBlobStorage, FileStorage or Storage. Must match the existing account."
  type        = string
  default     = "StorageV2"

  validation {
    condition     = contains(["StorageV2", "BlobStorage", "BlockBlobStorage", "FileStorage", "Storage"], var.installer_storage_account_kind)
    error_message = "installer_storage_account_kind must be one of StorageV2, BlobStorage, BlockBlobStorage, FileStorage, Storage."
  }
}

variable "installer_storage_import_existing" {
  description = "Import the existing installer storage account and public container via import blocks. Set false only when creating them from scratch (e.g. new subscription)."
  type        = bool
  default     = true
}
