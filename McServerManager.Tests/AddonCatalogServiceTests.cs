using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class AddonCatalogServiceTests
{
    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyWithoutHttpCall()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called"));
        using var client = new HttpClient(handler);
        var service = new AddonCatalogService(client);

        var results = await service.SearchAsync("   ", "Forge", "1.20.1");

        Assert.Empty(results);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_ForgeBuildsFacets_AndSortsByDownloadCount()
    {
        string? requestedUrl = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedUrl = request.RequestUri?.ToString();
            return StubHttpMessageHandler.Json(
                """
                {
                  "hits": [
                    { "slug": "low", "project_id": "1", "title": "Low", "description": "", "project_type": "mod", "downloads": 10 },
                    { "slug": "", "project_id": "2", "title": "Invalid", "description": "", "project_type": "mod", "downloads": 9999 },
                    { "slug": "high", "project_id": "3", "title": "High", "description": "", "project_type": "mod", "downloads": 20 }
                  ]
                }
                """);
        });
        using var client = new HttpClient(handler);
        var service = new AddonCatalogService(client);

        var results = await service.SearchAsync("sodium", "Forge", "1.20.1", limit: 999);

        Assert.Equal(2, results.Count);
        Assert.Equal("high", results[0].Slug);
        Assert.Equal("low", results[1].Slug);

        Assert.NotNull(requestedUrl);
        Assert.Contains("query=sodium", requestedUrl!, StringComparison.Ordinal);
        Assert.Contains("limit=100", requestedUrl!, StringComparison.Ordinal);
        Assert.Contains("index=relevance", requestedUrl!, StringComparison.Ordinal);

        var facets = GetDecodedQueryParam(requestedUrl!, "facets");
        Assert.Contains("project_type:mod", facets, StringComparison.Ordinal);
        Assert.Contains("categories:forge", facets, StringComparison.Ordinal);
        Assert.Contains("categories:neoforge", facets, StringComparison.Ordinal);
        Assert.Contains("versions:1.20.1", facets, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_PaperBuildsPluginFacets()
    {
        string? requestedUrl = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedUrl = request.RequestUri?.ToString();
            return StubHttpMessageHandler.Json("""{ "hits": [] }""");
        });
        using var client = new HttpClient(handler);
        var service = new AddonCatalogService(client);

        _ = await service.SearchAsync("luckperms", "Paper", "1.21.1", limit: 10);

        Assert.NotNull(requestedUrl);
        var facets = GetDecodedQueryParam(requestedUrl!, "facets");
        Assert.Contains("project_type:plugin", facets, StringComparison.Ordinal);
        Assert.Contains("categories:paper", facets, StringComparison.Ordinal);
        Assert.Contains("categories:bukkit", facets, StringComparison.Ordinal);
        Assert.Contains("versions:1.21.1", facets, StringComparison.Ordinal);
    }

    private static string GetDecodedQueryParam(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return string.Empty;
    }
}
