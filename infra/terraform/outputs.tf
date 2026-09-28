output "resource_group_name" {
  description = "Resource group name"
  value       = azurerm_resource_group.lp.name
}

output "static_web_app_name" {
  description = "Static Web App name"
  value       = azurerm_static_web_app.lp.name
}

output "static_web_app_default_hostname" {
  description = "Default Static Web App hostname (CNAME target for the custom domain)"
  value       = azurerm_static_web_app.lp.default_host_name
}

output "static_web_app_url" {
  description = "Landing page URL"
  value       = "https://${azurerm_static_web_app.lp.default_host_name}"
}

output "static_web_app_api_key" {
  description = "Deployment token for CI/CD (set as AZURE_STATIC_WEB_APPS_API_TOKEN_LP GitHub secret)"
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
  value       = try(azurerm_trusted_signing_account.main[0].name, "")
}

output "trusted_signing_endpoint" {
  description = "Trusted Signing regional endpoint (set as TRUSTED_SIGNING_ENDPOINT GitHub var)"
  value       = try("https://${lower(replace(azurerm_trusted_signing_account.main[0].location, " ", ""))}.codesigning.azure.net/", "")
}

output "trusted_signing_certificate_profile_name" {
  description = "Certificate profile name to create manually in the portal (set as TRUSTED_SIGNING_PROFILE GitHub var)"
  value       = var.trusted_signing_certificate_profile_name
}

output "trusted_signing_account_id" {
  description = "Trusted Signing account resource ID (scope for role assignments)"
  value       = try(azurerm_trusted_signing_account.main[0].id, "")
}

output "subscription_id" {
  description = "Azure subscription ID (set as AZURE_SUBSCRIPTION_ID GitHub var)"
  value       = var.subscription_id
}

output "tenant_id" {
  description = "Azure AD tenant ID (set as AZURE_TENANT_ID GitHub var)"
  value       = var.tenant_id
}

output "installer_storage_account_name" {
  description = "Installer distribution storage account name"
  value       = azurerm_storage_account.installer.name
}

output "installer_storage_primary_blob_endpoint" {
  description = "Primary blob endpoint of the installer storage account"
  value       = azurerm_storage_account.installer.primary_blob_endpoint
}

output "installer_download_base_url" {
  description = "Base URL for installer downloads (INSTALLER_BASE_URL in deploy-installer-to-storage.yml)"
  value       = "${azurerm_storage_account.installer.primary_blob_endpoint}${azurerm_storage_container.public.name}/${local.installer_storage_blob_prefix}"
}

output "installer_latest_download_url" {
  description = "Download URL of the latest installer (NUXT_PUBLIC_DOWNLOAD_URL for the landing page)"
  value       = "${azurerm_storage_account.installer.primary_blob_endpoint}${azurerm_storage_container.public.name}/${local.installer_storage_blob_prefix}/MaiPilotSetup.exe"
}

output "installer_storage_primary_connection_string" {
  description = "Primary connection string of the installer storage account (set as AZURE_STORAGE_CONNECTION_STRING GitHub secret)"
  value       = azurerm_storage_account.installer.primary_connection_string
  sensitive   = true
}
