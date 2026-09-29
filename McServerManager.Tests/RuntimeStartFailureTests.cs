using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class RuntimeStartFailureTests
{
    [Fact]
    public async Task StartAsync_WhenJavaIsMissing_ThrowsJavaNotFoundAndReturnsToStopped()
    {
        using var temp = new TemporaryDirectoryScope();
        File.WriteAllText(Path.Combine(temp.Path, "server.jar"), string.Empty);
        var missingJava = Path.Combine(temp.Path, "no-such-java", "java.exe");
        var config = new ServerConfig
        {
            ServerId = "java-missing",
            Name = "java-missing",
            Type = "Vanilla",
            DirectoryPath = temp.Path,
            JavaPath = missingJava,
            AutoRestartOnCrash = false
        };
        var manager = new ServerRuntimeManager();

        var ex = await Assert.ThrowsAsync<JavaNotFoundException>(() => manager.StartAsync(config, temp.Path));

        Assert.Equal(missingJava, ex.JavaPath);
        var runtime = manager.GetOrCreate(config);
        Assert.Equal(ServerStatus.Stopped, runtime.Status);
        // 未起動の Process が残ると、稼働モニターの HasExited 参照で例外になる
        Assert.Null(runtime.Process);
        Assert.True(manager.TryRelease(config.ServerId));
    }
}
