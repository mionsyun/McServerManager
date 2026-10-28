using McServerManager.Models.Editions;
using McServerManager.Services.Editions;
using McServerManager.ViewModels;
namespace McServerManager.TemplateTests;

public sealed class EditionPresentationTests
{
    [Theory]
    [InlineData(AppEdition.Free)]
    [InlineData(AppEdition.Pro)]
    public void UnconfiguredStoreNeverNavigatesEvenOnDirectCommandExecution(AppEdition edition)
    {
        var navigation = new Navigation();
        var vm = new EditionViewModel(new EditionPolicy(edition), ProDistributionLinks.Current, navigation);
        Assert.Equal(edition == AppEdition.Pro, vm.IsPro);
        Assert.Equal(edition == AppEdition.Free, vm.UsesPublicUpdater);
        Assert.False(vm.CanPurchase);
        Assert.False(vm.CanOpenUpdates);
        Assert.False(vm.PurchaseCommand.CanExecute(null));
        Assert.False(vm.UpdatesCommand.CanExecute(null));
        vm.PurchaseCommand.Execute(null);
        vm.UpdatesCommand.Execute(null);
        Assert.Equal(0, navigation.Count);
        Assert.Equal(string.Empty, vm.Status);
    }
    [Theory]
    [InlineData(AppEdition.Free)]
    [InlineData(AppEdition.Pro)]
    public void VerifiedFixtureLinksRouteOnlyTheInstalledEdition(AppEdition edition)
    {
        // Synthetic configuration fixtures, not a real Pro listing or published destination.
        const string product = "https://example-shop.booth.pm/items/99999999";
        const string library = "https://booth.pm/test-purchased-library";
        var navigation = new Navigation();
        var links = new ProDistributionLinks(product, library, product, library);
        var vm = new EditionViewModel(new EditionPolicy(edition), links, navigation);
        Assert.Equal(edition == AppEdition.Free, vm.CanPurchase);
        Assert.Equal(edition == AppEdition.Pro, vm.CanOpenUpdates);
        vm.PurchaseCommand.Execute(null);
        vm.UpdatesCommand.Execute(null);
        Assert.Equal(1, navigation.Count);
        Assert.NotEmpty(vm.Status);
    }

    private sealed class Navigation : IProStoreNavigation
    {
        public int Count { get; private set; }
        public bool OpenPurchase() { Count++; return true; }
        public bool OpenUpdates() { Count++; return true; }
    }
}
