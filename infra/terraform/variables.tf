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
