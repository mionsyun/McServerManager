using McServerManager.Services.Editions;

namespace McServerManager.TemplateTests.Editions;

public sealed class ProDistributionLinksTests
{
    // Synthetic fixtures only. These are not verified products or actual navigation targets.
    private const string FixtureItemUrl = "https://test-fixture.booth.pm/items/99999999999999999999";
    private const string FixtureLibraryUrl = "https://booth.pm/__library_fixture__";

    [Fact]
    public void ReleaseConfigurationIsDisabledUntilRealProRoutesAreVerified()
    {
        Assert.Null(ProDistributionLinks.Current.PurchaseUrl);
        Assert.Null(ProDistributionLinks.Current.UpdateUrl);
        Assert.False(ProDistributionLinks.Current.CanPurchase);
        Assert.False(ProDistributionLinks.Current.CanOpenUpdates);
    }

    [Fact]
    public void InternalReleaseConfigurationRequiresTwoSeparatelyVerifiedExactRoutes()
    {
        var links = new ProDistributionLinks(FixtureItemUrl, FixtureLibraryUrl, FixtureItemUrl, FixtureLibraryUrl);
        Assert.True(links.CanPurchase);
        Assert.True(links.CanOpenUpdates);
        Assert.Equal(FixtureItemUrl, links.PurchaseUrl!.AbsoluteUri);
        Assert.Equal(FixtureLibraryUrl, links.UpdateUrl!.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://booth.pm/")]
    [InlineData("https://maipilot.booth.pm/")]
    [InlineData("https://maipilot.booth.pm/items/8118402")]
    [InlineData("https://booth.pm/ja/items/8118402")]
    [InlineData("https://example.invalid/items/99999999999999999999")]
    [InlineData("https://test-fixture.booth.pm.evil.invalid/items/99999999999999999999")]
    [InlineData("https://test-fixture.booth.pm@evil.invalid/items/99999999999999999999")]
    [InlineData("https://user@test-fixture.booth.pm/items/99999999999999999999")]
    [InlineData("http://test-fixture.booth.pm/items/99999999999999999999")]
    [InlineData("https://test-fixture.booth.pm:443/items/99999999999999999999")]
    [InlineData("https://test-fixture.booth.pm/items/99999999999999999999?redirect=other")]
    [InlineData("https://test-fixture.booth.pm/items/99999999999999999999#download")]
    [InlineData("https://test-fixture.booth.pm/items/99999999999999999999/")]
    [InlineData("https://test-fixture.booth.pm/other/../items/99999999999999999999")]
    [InlineData("https://test-fixture.booth.pm/items/08118402")]
    [InlineData("https://test-fixture.booth.pm/items/123abc")]
    [InlineData("https://test-fixture.booth.pm/items/999999999999999999999")]
    public void InvalidGeneralOrSupportProductRoutesStayDisabledEvenIfMisconfigured(string url)
    {
        var links = new ProDistributionLinks(url, "", url, "");
        Assert.Null(links.PurchaseUrl);
        Assert.Null(links.UpdateUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://booth.pm/")]
    [InlineData("https://booth.pm/ja")]
    [InlineData("https://booth.pm/en")]
    [InlineData("https://booth.pm/ja/items/99999999999999999999")]
    [InlineData("https://test-fixture.booth.pm/__library_fixture__")]
    [InlineData("https://booth.pm/__library_fixture__?redirect=other")]
    [InlineData("https://booth.pm/__library_fixture__#download")]
    [InlineData("https://booth.pm/installer.exe")]
    [InlineData("http://booth.pm/__library_fixture__")]
    [InlineData("https://example.invalid/__library_fixture__")]
    public void LibraryDestinationMustBeSeparatelyVerifiedAndCannotBeAProductOrInstaller(string url)
    {
        var links = new ProDistributionLinks("", url, "", url);
        Assert.False(links.CanPurchase);
        Assert.False(links.CanOpenUpdates);
    }

    [Fact]
    public void InventedDestinationNotInReleaseAllowlistIsDisabled()
    {
        var links = new ProDistributionLinks(FixtureItemUrl, FixtureLibraryUrl,
            "https://test-fixture.booth.pm/items/99999999999999999998", "https://booth.pm/__invented_route__");
        Assert.False(links.CanPurchase);
        Assert.False(links.CanOpenUpdates);
    }

    [Fact]
    public void EmptyAllowlistDisablesEvenPlausibleDestinations()
    {
        var links = new ProDistributionLinks("", "", FixtureItemUrl, FixtureLibraryUrl);
        Assert.False(links.CanPurchase);
        Assert.False(links.CanOpenUpdates);
    }

    [Fact]
    public void MissingLibraryRouteNeverFallsBackToProductOrPublicFreeFeed()
    {
        var links = new ProDistributionLinks(FixtureItemUrl, "", FixtureItemUrl, FixtureItemUrl);
        Assert.True(links.CanPurchase);
        Assert.Null(links.UpdateUrl);
    }
}
