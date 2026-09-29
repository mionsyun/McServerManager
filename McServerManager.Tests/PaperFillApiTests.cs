using McServerManager.Services;

namespace McServerManager.Tests;

public sealed class PaperFillApiTests
{
    [Fact]
    public void ParsePaperLatestBuild_ReadsServerJarUrlAndSha256()
    {
        const string json = """
            {"id":133,"channel":"STABLE","downloads":{"server:default":{"name":"paper-1.21.1-133.jar",
            "checksums":{"sha256":"39bd8c00b9e18de91dcabd3cc3dcfa5328685a53b7187a2f63280c22e2d287b9"},"size":49394394,
            "url":"https://fill-data.papermc.io/v1/objects/39bd8c00/paper-1.21.1-133.jar"}}}
            """;

        var (url, sha256) = ServerJarService.ParsePaperLatestBuild(json);

        Assert.Equal("https://fill-data.papermc.io/v1/objects/39bd8c00/paper-1.21.1-133.jar", url);
        Assert.Equal("39bd8c00b9e18de91dcabd3cc3dcfa5328685a53b7187a2f63280c22e2d287b9", sha256);
    }

    [Fact]
    public void ParsePaperLatestBuild_WithoutServerDownload_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ServerJarService.ParsePaperLatestBuild("""{"downloads":{}}"""));
    }
}
