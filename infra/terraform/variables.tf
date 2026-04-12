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
