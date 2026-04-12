namespace McServerManager.Tests;

public sealed class ExternalApiUrlsTests
{
    [Fact]
    public void ImportantEndpoints_AreHttps()
    {
        Assert.StartsWith("https://", GetConst("MinecraftVersionManifest"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", GetConst("PaperApiBase"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", GetConst("FabricApiBase"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", GetConst("PurpurApiBase"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", GetConst("ForgeMavenBase"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", GetConst("SpigotDirectJarTemplate"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", GetConst("SpigotDirectJarFallbackTemplate"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpigotTemplates_ContainVersionPlaceholder()
    {
        Assert.Contains("{0}", GetConst("SpigotDirectJarTemplate"), StringComparison.Ordinal);
        Assert.Contains("{0}", GetConst("SpigotDirectJarFallbackTemplate"), StringComparison.Ordinal);
    }

    private static string GetConst(string fieldName)
    {
        var type = Type.GetType("McServerManager.Services.ExternalApiUrls, McServerManager");
        Assert.NotNull(type);

        var field = type!.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);

        var value = field!.GetValue(null) as string;
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value!;
    }
}
