output "resource_group_name" {
  description = "Resource group name"
  value       = azurerm_resource_group.lp.name
}

output "static_web_app_name" {
  description = "Static Web App name"
  value       = azurerm_static_web_app.lp.name
}

output "static_web_app_default_hostname" {
  description = "Default Static Web App hostname"
  value       = azurerm_static_web_app.lp.default_host_name
}

output "static_web_app_url" {
  description = "Landing page URL"
  value       = "https://${azurerm_static_web_app.lp.default_host_name}"
}

output "static_web_app_api_key" {
  description = "Deployment token for CI/CD"
  value       = azurerm_static_web_app.lp.api_key
  sensitive   = true
}

output "custom_domain_name" {
  description = "Configured custom domain name (empty when disabled)"
  value       = try(azurerm_static_web_app_custom_domain.lp[0].domain_name, "")
}

output "custom_domain_validation_type" {
  description = "Validation type used for custom domain"
  value       = try(azurerm_static_web_app_custom_domain.lp[0].validation_type, "")
}

output "custom_domain_validation_token" {
  description = "DNS TXT validation token (used when validation_type = dns-txt-token)"
  value       = try(azurerm_static_web_app_custom_domain.lp[0].validation_token, "")
  sensitive   = true
}

output "azure_portal_link" {
  description = "Azure portal link to the Static Web App"
  value       = "https://portal.azure.com/#@/resource${azurerm_static_web_app.lp.id}/overview"
}

output "trusted_signing_account_name" {
  description = "Trusted Signing account name (set as TRUSTED_SIGNING_ACCOUNT GitHub var)"
  value       = azurerm_trusted_signing_account.main.name
}

output "trusted_signing_endpoint" {
  description = "Trusted Signing regional endpoint (set as TRUSTED_SIGNING_ENDPOINT GitHub var)"
  value       = "https://${lower(replace(azurerm_trusted_signing_account.main.location, " ", ""))}.codesigning.azure.net/"
}

output "trusted_signing_certificate_profile_name" {
  description = "Certificate profile name (set as TRUSTED_SIGNING_PROFILE GitHub var)"
  value       = azurerm_trusted_signing_certificate_profile.main.name
}
