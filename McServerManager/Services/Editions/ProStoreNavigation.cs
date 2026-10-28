using System.Diagnostics;

namespace McServerManager.Services.Editions;

/// <summary>Opens only release-verified BOOTH navigation. Never downloads or executes an installer.</summary>
public sealed class ProStoreNavigation(ProDistributionLinks links) : IProStoreNavigation
{
    public bool OpenPurchase() => Open(links.PurchaseUrl);
    public bool OpenUpdates() => Open(links.UpdateUrl);
    private static bool Open(Uri? uri)
    {
        if (uri is null) return false;
        try { return Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }) is not null; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
        { return false; }
    }
}
