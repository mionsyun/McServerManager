using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;
using McServerManager.ViewModels;

namespace McServerManager.Tests;

public sealed class ShareAddressTests
{
    [Theory]
    [InlineData("Vanilla", 25570, "203.0.113.10:25570")]
    [InlineData(ServerEditions.BedrockType, 19132, "203.0.113.10")]
    public void PublicAddress_IsFetchedOnOpen_AndFormattedPerEdition(string type, int port, string expected)
    {
        using var appData = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        var services = TestAppServicesFactory.CreateForAddonTests(paths, new TestDialogService());
        ((StubNetworkService)services.Network).PublicIp = "203.0.113.10";
        var config = new ServerConfig
        {
            ServerId = "share-test",
            Name = "share-test",
            Type = type,
            DirectoryPath = Directory.CreateDirectory(Path.Combine(appData.AppDataPath, "share-test")).FullName,
            Port = port
        };

        var vm = new ServerNetworkViewModel(
            services, config, new AppSettings(), new PortForwardingService(new StubUpnpService(), new BedrockPropertiesService()));

        Assert.True(vm.HasPublicIp);
        Assert.Equal(expected, vm.PublicAddress);
    }
}
