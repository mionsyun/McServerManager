using McServerManager.Services;

namespace McServerManager.Tests;

public sealed class DialogServiceTests
{
    [Fact]
    public void DialogService_ImplementsInterface()
    {
        var service = new DialogService();

        Assert.IsAssignableFrom<IDialogService>(service);
    }
}
