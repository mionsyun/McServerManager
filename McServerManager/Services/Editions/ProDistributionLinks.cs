namespace McServerManager.Services.Editions;

/// <summary>
/// Release-owned BOOTH navigation only. These are not installer URLs or an update manifest feed.
/// Keep every value empty until the actual Pro item and purchased-library instructions are verified.
/// </summary>
public sealed class ProDistributionLinks
{
    private const string ExistingSupportItemId = "8118402";

    // Intentionally disabled. A release must independently verify the real Pro product and the
    // purchased-library route before setting their exact allowlist entries and destinations.
    // Never infer either route or reuse the existing same-feature support item.
    // These values are compiled into the distribution, never loaded from AppSettings or JSON.
    private const string VerifiedProProductUrl = "";
    private const string VerifiedBoothLibraryUrl = "";
    private const string ConfiguredPurchaseUrl = "";
    private const string ConfiguredUpdateUrl = "";

    public static ProDistributionLinks Current { get; } =
        new(VerifiedProProductUrl, VerifiedBoothLibraryUrl, ConfiguredPurchaseUrl, ConfiguredUpdateUrl);

    internal ProDistributionLinks(string verifiedProProductUrl, string verifiedBoothLibraryUrl,
        string purchaseUrl, string updateUrl)
    {
        PurchaseUrl = ValidateProductUrl(verifiedProProductUrl, purchaseUrl);
        UpdateUrl = ValidateLibraryUrl(verifiedBoothLibraryUrl, updateUrl);
    }

    public Uri? PurchaseUrl { get; }
    public Uri? UpdateUrl { get; }
    public bool CanPurchase => PurchaseUrl is not null;
    public bool CanOpenUpdates => UpdateUrl is not null;

    private static Uri? ValidateProductUrl(string verifiedUrl, string configuredUrl)
    {
        if (!IsExactVerifiedDestination(verifiedUrl, configuredUrl, out var uri)) return null;
        var segments = uri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var itemIndex = uri.Host == "booth.pm" ? 1 : 0;
        if (segments.Length != itemIndex + 2 || segments[itemIndex] != "items" ||
            itemIndex == 1 && segments[0] is not ("ja" or "en")) return null;
        var itemId = segments[itemIndex + 1];
        if (itemId.Length is < 1 or > 20 || itemId[0] == '0' || itemId == ExistingSupportItemId ||
            itemId.Any(c => !char.IsAsciiDigit(c))) return null;
        return uri;
    }

    private static Uri? ValidateLibraryUrl(string verifiedUrl, string configuredUrl)
    {
        if (!IsExactVerifiedDestination(verifiedUrl, configuredUrl, out var uri) ||
            uri!.Host != "booth.pm") return null;
        // The verified library route is a separate exact allowlist entry. No shop homepage,
        // locale homepage or product page can accidentally stand in for purchased downloads.
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Length == 1 && segments[0] is "ja" or "en" ||
            segments.Any(segment => segment == "items") ||
            uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        return uri;
    }

    private static bool IsExactVerifiedDestination(string verifiedUrl, string configuredUrl, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrEmpty(verifiedUrl) ||
            !string.Equals(verifiedUrl, configuredUrl, StringComparison.Ordinal) ||
            !Uri.TryCreate(configuredUrl, UriKind.Absolute, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(candidate.UserInfo) ||
            !candidate.IsDefaultPort || !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment) || candidate.AbsoluteUri != configuredUrl ||
            configuredUrl.Contains('%') || candidate.AbsolutePath.Contains("//", StringComparison.Ordinal) ||
            candidate.AbsolutePath.EndsWith('/')) return false;

        var host = candidate.Host;
        if (host != "booth.pm")
        {
            const string suffix = ".booth.pm";
            if (!host.EndsWith(suffix, StringComparison.Ordinal)) return false;
            var shop = host[..^suffix.Length];
            if (shop.Length == 0 || shop.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return false;
        }
        // Require the canonical host form and no explicitly supplied port (even :443).
        if (!configuredUrl.StartsWith($"https://{host}/", StringComparison.Ordinal)) return false;
        uri = candidate;
        return true;
    }
}
