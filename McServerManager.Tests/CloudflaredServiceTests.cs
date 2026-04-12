using System.Reflection;
using McServerManager.Services;

namespace McServerManager.Tests;

public sealed class CloudflaredServiceTests
{
    [Fact]
    public void ExtractSha256_ParsesHash()
    {
        var hash = InvokePrivateStatic<string>(
            nameof(CloudflaredService),
            "ExtractSha256",
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", hash);
    }

    [Fact]
    public void ExtractSha256FromReleaseBody_ParsesMatchingAssetLine()
    {
        var body = """
                   cloudflared-linux-amd64  aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
                   cloudflared-windows-amd64.exe  bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
                   """;

        var hash = InvokePrivateStatic<string>(
            nameof(CloudflaredService),
            "ExtractSha256FromReleaseBody",
            body,
            "cloudflared-windows-amd64.exe");

        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", hash);
    }

    [Fact]
    public void IsTrustedSignerSubject_ReturnsTrueForCloudflare()
    {
        var trusted = InvokePrivateStatic<bool>(
            nameof(CloudflaredService),
            "IsTrustedSignerSubject",
            "O=Cloudflare, Inc., CN=Cloudflare, Inc.");
        var notTrusted = InvokePrivateStatic<bool>(
            nameof(CloudflaredService),
            "IsTrustedSignerSubject",
            "CN=Unknown Publisher");

        Assert.True(trusted);
        Assert.False(notTrusted);
    }

    [Fact]
    public async Task StopAsync_WhenNotStarted_IsSafe()
    {
        using var service = new CloudflaredService();

        var ex = await Record.ExceptionAsync(() => service.StopAsync());

        Assert.Null(ex);
        Assert.False(service.IsRunning);
        Assert.Null(service.TunnelUrl);
    }

    private static T InvokePrivateStatic<T>(string typeName, string methodName, params object?[] args)
    {
        var type = typeof(CloudflaredService);
        Assert.Equal(type.Name, typeName);

        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var value = method!.Invoke(null, args);
        Assert.NotNull(value);
        return (T)value!;
    }
}
