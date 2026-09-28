variable "subscription_id" {
  description = "Azure subscription ID to deploy resources into"
  type        = string
  default     = "1456e0ca-79d5-4b67-a5e0-5e98062498fc"
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

variable "static_web_app_location" {
  description = "Azure region for the Static Web App control plane. Must be a supported region (West US 2, Central US, East US 2, West Europe, East Asia). Japan East is not supported. Content is served globally regardless."
  type        = string
  default     = "East Asia"
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
  description = "Certificate profile name for Trusted Signing. The profile itself is created manually in the Azure portal after identity validation; use this same name there (used in CI signing commands)."
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
  description = "Custom domain to bind to the Static Web App (e.g. www.maipilot.jp). Leave empty to disable. When rebuilding, apply once with empty, point the DNS CNAME to the new default hostname, then set this and apply again."
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
  description = "Storage account name for installer distribution (globally unique, 3-24 lowercase letters and digits). URLs in landing / installer / workflows assume stmailpilotje."
  type        = string
  default     = "stmailpilotje"

  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.installer_storage_account_name))
    error_message = "installer_storage_account_name must be 3-24 characters of lowercase letters and digits."
  }
}

variable "installer_storage_resource_group_name" {
  description = "Resource group for the installer storage account. Leave empty to use the landing page resource group."
  type        = string
  default     = ""
}

variable "installer_storage_location" {
  description = "Azure region for the installer storage account. Leave empty to use the resource group location."
  type        = string
  default     = ""
}

variable "installer_storage_account_tier" {
  description = "Installer storage account tier: Standard or Premium"
  type        = string
  default     = "Standard"

  validation {
    condition     = contains(["Standard", "Premium"], var.installer_storage_account_tier)
    error_message = "installer_storage_account_tier must be Standard or Premium."
  }
}

variable "installer_storage_account_replication_type" {
  description = "Installer storage account replication type: LRS, GRS, RAGRS, ZRS, GZRS or RAGZRS"
  type        = string
  default     = "LRS"

  validation {
    condition     = contains(["LRS", "GRS", "RAGRS", "ZRS", "GZRS", "RAGZRS"], var.installer_storage_account_replication_type)
    error_message = "installer_storage_account_replication_type must be one of LRS, GRS, RAGRS, ZRS, GZRS, RAGZRS."
  }
}

variable "installer_storage_account_kind" {
  description = "Installer storage account kind: StorageV2, BlobStorage, BlockBlobStorage, FileStorage or Storage"
  type        = string
  default     = "StorageV2"

  validation {
    condition     = contains(["StorageV2", "BlobStorage", "BlockBlobStorage", "FileStorage", "Storage"], var.installer_storage_account_kind)
    error_message = "installer_storage_account_kind must be one of StorageV2, BlobStorage, BlockBlobStorage, FileStorage, Storage."
  }
}
