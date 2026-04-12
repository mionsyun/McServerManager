using System.Net;

namespace McServerManager.Tests.TestInfrastructure;

internal static class LiveTestGate
{
    public const string LiveEnvVar = "MCSM_ENABLE_LIVE_TESTS";
    public const string LiveDownloadEnvVar = "MCSM_ENABLE_LIVE_DOWNLOAD_TESTS";
    public const string LiveForgeDownloadEnvVar = "MCSM_ENABLE_LIVE_FORGE_DOWNLOAD";

    public static bool IsLiveEnabled()
    {
        var raw = Environment.GetEnvironmentVariable(LiveEnvVar);
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
               || bool.TryParse(raw, out var enabled) && enabled;
    }

    public static bool IsLiveDownloadEnabled()
    {
        var raw = Environment.GetEnvironmentVariable(LiveDownloadEnvVar);
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
               || bool.TryParse(raw, out var enabled) && enabled;
    }

    public static bool IsLiveForgeDownloadEnabled()
    {
        var raw = Environment.GetEnvironmentVariable(LiveForgeDownloadEnvVar);
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
               || bool.TryParse(raw, out var enabled) && enabled;
    }

    public static bool CanResolveHost(string host)
    {
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            return addresses.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool CanResolveAnyHost(params string[] hosts)
    {
        foreach (var host in hosts)
        {
            if (CanResolveHost(host))
            {
                return true;
            }
        }

        return false;
    }
}
