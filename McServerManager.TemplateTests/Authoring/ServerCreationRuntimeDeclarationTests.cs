using System.Text.Json;
using McServerManager.Models;
using McServerManager.Models.Authoring;
using McServerManager.Models.Editions;
using McServerManager.Services;
using McServerManager.Services.Authoring;
using McServerManager.Services.Editions;
using McServerManager.Services.Templates;

namespace McServerManager.TemplateTests.Authoring;

// AppPathsService reads a process-level override. Keep these isolated fixtures away
// from parallel tests, and restore the override immediately after constructing paths.
[CollectionDefinition("Creation runtime provisioning", DisableParallelization = true)]
public sealed class CreationRuntimeProvisioningCollection { }

[Collection("Creation runtime provisioning")]
public sealed class ServerCreationRuntimeDeclarationTests
{
    private const string ServerId = "1234567890abcdef1234567890abcdef";

    [Theory]
    [InlineData("1.21")]
    [InlineData("1.21.1")]
    [InlineData("26.1")]
    [InlineData("01.021.001")]
    public void FixedNumericReleaseIsRecordedWithoutNormalization(string version)
    {
        var declaration = ServerCreationRuntimeDeclarationPolicy.Create(ServerId, "Vanilla", version);
        Assert.Equal(new ServerCreationRuntimeDeclaration(1, ServerId, "Vanilla", version), declaration);
        Assert.True(ServerCreationRuntimeDeclarationPolicy.IsMatching(declaration, ServerId, "Vanilla", version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("release")]
    [InlineData("snapshot")]
    [InlineData("24w14a")]
    [InlineData("1.21-pre1")]
    [InlineData("1.21-rc1")]
    [InlineData("1")]
    [InlineData("1.21.1.1")]
    [InlineData("1..21")]
    [InlineData(".1.21")]
    [InlineData("1.21.")]
    [InlineData("+1.21")]
    [InlineData("-1.21")]
    [InlineData("1.21e1")]
    [InlineData("1,21")]
    [InlineData(" 1.21")]
    [InlineData("1.21 ")]
    [InlineData("1.21\n")]
    [InlineData("1.21\r\n")]
    [InlineData("1.21\0")]
    [InlineData("１.２１")]
    [InlineData("١.٢١")]
    [InlineData("1.21/../../server.jar")]
    [InlineData("https://example.invalid/1.21")]
    public void FloatingMalformedOrHostileVersionIsNeverRecorded(string? version)
    {
        Assert.Null(ServerCreationRuntimeDeclarationPolicy.Create(ServerId, "Vanilla", version));
        var malformed = new ServerCreationRuntimeDeclaration(1, ServerId, "Vanilla", version!);
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(malformed, ServerId, "Vanilla", version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("vanilla")]
    [InlineData("VANILLA")]
    [InlineData("Vanilla ")]
    [InlineData(" Vanilla")]
    [InlineData("Vanilla\n")]
    [InlineData("Vanilla\0")]
    [InlineData("Paper")]
    [InlineData("Fabric")]
    [InlineData("Forge")]
    [InlineData("Bedrock")]
    [InlineData("Unknown")]
    public void OnlyExactVanillaTypeIsRecorded(string? type)
    {
        Assert.Null(ServerCreationRuntimeDeclarationPolicy.Create(ServerId, type, "1.21.1"));
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(
            new(1, ServerId, type!, "1.21.1"), ServerId, type, "1.21.1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("server-1")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("1234567890ABCDEF1234567890ABCDEF")]
    [InlineData("12345678-90ab-cdef-1234-567890abcdef")]
    [InlineData("{12345678-90ab-cdef-1234-567890abcdef}")]
    [InlineData(" 1234567890abcdef1234567890abcdef")]
    [InlineData("1234567890abcdef1234567890abcdef ")]
    [InlineData("1234567890abcdef1234567890abcdef\n")]
    [InlineData("1234567890abcdef1234567890abcdeg")]
    [InlineData("../../server")]
    public void OnlyCanonicalGuidNIdentityIsRecorded(string? id)
    {
        Assert.Null(ServerCreationRuntimeDeclarationPolicy.Create(id, "Vanilla", "1.21.1"));
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(
            new(1, id!, "Vanilla", "1.21.1"), id, "Vanilla", "1.21.1"));
    }

    [Fact]
    public void ReleaseLengthUsesStrictManifestBound()
    {
        var boundary = new string('1', TemplatePolicy.MaxRuntimeVersionLength - 2) + ".1";
        Assert.NotNull(ServerCreationRuntimeDeclarationPolicy.Create(ServerId, "Vanilla", boundary));
        Assert.Null(ServerCreationRuntimeDeclarationPolicy.Create(ServerId, "Vanilla", "1" + boundary));
        Assert.Null(ServerCreationRuntimeDeclarationPolicy.Create(ServerId, "Vanilla", new string('1', 100_000) + ".1"));
    }

    [Fact]
    public void MatchingRequiresSupportedSchemaAndExactIdentityTypeAndVersion()
    {
        var declaration = ServerCreationRuntimeDeclarationPolicy.Create(ServerId, "Vanilla", "1.21.1")!;
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(null, ServerId, "Vanilla", "1.21.1"));
        foreach (var schema in new[] { int.MinValue, -1, 0, 2, int.MaxValue })
            Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(declaration with { SchemaVersion = schema },
                ServerId, "Vanilla", "1.21.1"));
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(declaration, Guid.NewGuid().ToString("N"), "Vanilla", "1.21.1"));
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(declaration, ServerId.ToUpperInvariant(), "Vanilla", "1.21.1"));
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(declaration, ServerId, "vanilla", "1.21.1"));
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(declaration, ServerId, "Paper", "1.21.1"));
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(declaration, ServerId, "Vanilla", "1.21.2"));
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(declaration, ServerId, "Vanilla", "01.21.1"));
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(declaration, null, null, null));
    }

    [Fact]
    public void ExistingAndDefaultConfigDoNotAcquireHistoricalDeclarations()
    {
        Assert.Null(new ServerConfig().CreationRuntimeDeclaration);
        var existing = JsonSerializer.Deserialize<ServerConfig>(
            "{\"ServerId\":\"" + ServerId + "\",\"Type\":\"Vanilla\",\"Version\":\"1.21.1\"}")!;
        Assert.Null(existing.CreationRuntimeDeclaration);
        Assert.Null(JsonSerializer.Deserialize<ServerConfig>(JsonSerializer.Serialize(existing))!.CreationRuntimeDeclaration);
        Assert.Null(JsonSerializer.Deserialize<ServerConfig>("{\"CreationRuntimeDeclaration\":null}")!.CreationRuntimeDeclaration);
    }

    [Fact]
    public void DeclarationSurvivesConfigJsonRoundTripAndMalformedJsonDataDoesNotMatch()
    {
        var config = new ServerConfig
        {
            ServerId = ServerId, Type = "Vanilla", Version = "1.21.1",
            CreationRuntimeDeclaration = ServerCreationRuntimeDeclarationPolicy.Create(ServerId, "Vanilla", "1.21.1")
        };
        var restored = JsonSerializer.Deserialize<ServerConfig>(JsonSerializer.Serialize(config))!;
        Assert.Equal(config.CreationRuntimeDeclaration, restored.CreationRuntimeDeclaration);
        Assert.True(ServerCreationRuntimeDeclarationPolicy.IsMatching(restored.CreationRuntimeDeclaration,
            restored.ServerId, restored.Type, restored.Version));
        var malformed = JsonSerializer.Deserialize<ServerConfig>("{\"CreationRuntimeDeclaration\":{\"SchemaVersion\":1}}")!;
        Assert.False(ServerCreationRuntimeDeclarationPolicy.IsMatching(malformed.CreationRuntimeDeclaration,
            ServerId, "Vanilla", "1.21.1"));
    }

    [Fact]
    public async Task NormalProvisioningPersistsDeclarationOnlyAfterSuccessfulDownloadAndPropertiesSave()
    {
        using var fixture = new ProvisioningFixture();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Jars.Download = _ => gate.Task;
        fixture.Properties.BeforeSave = () => Assert.Equal(0, fixture.Configs.SaveCalls);
        var creation = fixture.Service.CreateAsync(fixture.Options());
        Assert.False(creation.IsCompleted);
        Assert.Equal(0, fixture.Configs.SaveCalls);
        Assert.Empty(Directory.GetFiles(fixture.Root, "config.json", SearchOption.AllDirectories));
        gate.SetResult();
        var config = await creation;

        Assert.Equal(1, fixture.Properties.SaveCalls);
        Assert.Equal(1, fixture.Configs.SaveCalls);
        Assert.Equal("Vanilla", fixture.Jars.RequestType);
        Assert.Equal("1.21.1", fixture.Jars.RequestVersion);
        Assert.True(ServerCreationRuntimeDeclarationPolicy.IsMatching(config.CreationRuntimeDeclaration,
            config.ServerId, config.Type, config.Version));
        var persisted = fixture.Configs.LoadFromDirectory(config.DirectoryPath)!;
        Assert.Equal(config.CreationRuntimeDeclaration, persisted.CreationRuntimeDeclaration);
        Assert.Equal("eula=true", File.ReadAllText(Path.Combine(config.DirectoryPath, "eula.txt")));
    }

    [Fact]
    public async Task ProvisionedDeclarationRoundTripsIntoStrictlyValidatedProTemplateExport()
    {
        using var fixture = new ProvisioningFixture();
        var config = await fixture.Service.CreateAsync(fixture.Options());
        var persisted = fixture.Configs.LoadFromDirectory(config.DirectoryPath)!;
        var configBytes = await File.ReadAllBytesAsync(Path.Combine(config.DirectoryPath, "config.json"));
        var jarBytes = await File.ReadAllBytesAsync(Path.Combine(config.DirectoryPath, "server.jar"));
        var parser = new TemplateManifestService();
        var author = new TemplateAuthoringService(new EditionPolicy(AppEdition.Pro), parser);
        var opened = author.CreateFromRegisteredSettings(RegisteredServerTemplateSourceFactory.Create(persisted));
        Assert.True(opened.IsValid);
        var prepared = author.PrepareExport(opened.Session!, opened.Draft!);
        Assert.True(prepared.IsValid);
        Assert.True(prepared.PreparedExport!.InputValidatedOnly);
        var exported = parser.Parse(author.Export(opened.Session!, opened.Draft!, prepared.PreparedExport));
        Assert.True(exported.IsValid);
        Assert.Equal("vanilla", exported.Manifest!.Runtime.Type);
        Assert.Equal("1.21.1", exported.Manifest.Runtime.MinecraftVersion);
        Assert.Empty(exported.Manifest.Addons);
        Assert.Null(exported.Manifest.ClientDefinition);
        Assert.Equal(configBytes, await File.ReadAllBytesAsync(Path.Combine(config.DirectoryPath, "config.json")));
        Assert.Equal(jarBytes, await File.ReadAllBytesAsync(Path.Combine(config.DirectoryPath, "server.jar")));
    }

    [Theory]
    [InlineData("Vanilla", "1.21.1", "Paper", "1.20.4", true)]
    [InlineData("Vanilla", "1.21.1", "Bedrock", "latest", true)]
    [InlineData("Vanilla", "latest", "Vanilla", "1.21.1", false)]
    [InlineData("Paper", "1.21.1", "Vanilla", "1.21.1", false)]
    public async Task DownloadMutationCannotChangeTheRecordedRuntime(string initialType, string initialVersion,
        string changedType, string changedVersion, bool hasDeclaration)
    {
        using var fixture = new ProvisioningFixture();
        var options = fixture.Options(initialType, initialVersion);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Jars.Download = _ => gate.Task;
        var creation = fixture.Service.CreateAsync(options);
        options.Type = changedType;
        options.Version = changedVersion;
        gate.SetResult();
        var config = await creation;

        Assert.Equal(initialType, fixture.Jars.RequestType);
        Assert.Equal(initialVersion, fixture.Jars.RequestVersion);
        Assert.Equal(initialType, config.Type);
        Assert.Equal(initialVersion, config.Version);
        Assert.Equal(hasDeclaration, config.CreationRuntimeDeclaration is not null);
        Assert.Equal(config.CreationRuntimeDeclaration, fixture.Configs.LoadFromDirectory(config.DirectoryPath)!.CreationRuntimeDeclaration);
    }

    [Fact]
    public async Task ProgressCallbackCannotChangeDispatchOrDeclaration()
    {
        using var fixture = new ProvisioningFixture();
        var options = fixture.Options();
        var progress = new InlineProgress(() => { options.Type = "Bedrock"; options.Version = "latest"; });
        var config = await fixture.Service.CreateAsync(options, progress);
        Assert.Equal("Vanilla", fixture.Jars.RequestType);
        Assert.Equal("1.21.1", fixture.Jars.RequestVersion);
        Assert.Equal("Vanilla", config.Type);
        Assert.Equal("1.21.1", config.Version);
        Assert.NotNull(config.CreationRuntimeDeclaration);
        Assert.Equal(0, fixture.Bedrock.InstallCalls);
    }

    [Theory]
    [InlineData("Vanilla", "latest")]
    [InlineData("Vanilla", "24w14a")]
    [InlineData("Vanilla", "1.21-pre1")]
    [InlineData("Vanilla", "1.21\n")]
    [InlineData("vanilla", "1.21.1")]
    [InlineData("Paper", "1.21.1")]
    [InlineData("Fabric", "1.21.1")]
    [InlineData("Forge", "1.21.1")]
    [InlineData("Unknown", "1.21.1")]
    public async Task OtherSuccessfulJavaSelectionsHaveNoDeclaration(string type, string version)
    {
        using var fixture = new ProvisioningFixture();
        var config = await fixture.Service.CreateAsync(fixture.Options(type, version));
        Assert.Null(config.CreationRuntimeDeclaration);
        Assert.Null(fixture.Configs.LoadFromDirectory(config.DirectoryPath)!.CreationRuntimeDeclaration);
        Assert.Equal(1, fixture.Configs.SaveCalls);
    }

    [Fact]
    public async Task BedrockHasNoDeclarationAndKeepsItsInitialVersion()
    {
        using var fixture = new ProvisioningFixture();
        var options = fixture.Options("Bedrock", "1.21.1");
        fixture.Bedrock.OnInstall = () => { options.Type = "Vanilla"; options.Version = "1.22.1"; };
        var config = await fixture.Service.CreateAsync(options);
        Assert.Equal(1, fixture.Bedrock.InstallCalls);
        Assert.Equal(0, fixture.Jars.DownloadCalls);
        Assert.Equal("Bedrock", config.Type);
        Assert.Equal("1.21.1", config.Version);
        Assert.Null(config.CreationRuntimeDeclaration);
        Assert.Null(fixture.Configs.LoadFromDirectory(config.DirectoryPath)!.CreationRuntimeDeclaration);
    }

    [Fact]
    public async Task FailedDownloadCannotPersistADeclaration()
    {
        using var fixture = new ProvisioningFixture();
        fixture.Jars.Download = _ => Task.FromException(new IOException("Synthetic download failure."));
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.CreateAsync(fixture.Options()));
        Assert.Equal(0, fixture.Properties.SaveCalls);
        Assert.Equal(0, fixture.Configs.SaveCalls);
        Assert.Empty(Directory.GetFiles(fixture.Root, "config.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task FailedPropertyWriteCannotPersistADeclaration()
    {
        using var fixture = new ProvisioningFixture();
        fixture.Properties.BeforeSave = () => throw new IOException("Synthetic properties failure.");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.CreateAsync(fixture.Options()));
        Assert.Equal(0, fixture.Configs.SaveCalls);
        Assert.Empty(Directory.GetFiles(fixture.Root, "config.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MissingEulaConsentDoesNotDownloadOrSave()
    {
        using var fixture = new ProvisioningFixture();
        var options = fixture.Options();
        options.EulaAccepted = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync(options));
        Assert.Equal(0, fixture.Jars.DownloadCalls);
        Assert.Equal(0, fixture.Configs.SaveCalls);
    }

    private sealed class ProvisioningFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "creation-runtime-" + Guid.NewGuid().ToString("N"));
        public JarDownloader Jars { get; } = new();
        public PropertiesWriter Properties { get; } = new();
        public BedrockInstaller Bedrock { get; } = new();
        public ConfigWriter Configs { get; }
        public ServerProvisioningService Service { get; }

        public ProvisioningFixture()
        {
            var previous = Environment.GetEnvironmentVariable(AppPathsService.AppDataRootOverrideEnvVar);
            AppPathsService paths;
            try
            {
                Environment.SetEnvironmentVariable(AppPathsService.AppDataRootOverrideEnvVar, Root);
                paths = new AppPathsService();
            }
            finally { Environment.SetEnvironmentVariable(AppPathsService.AppDataRootOverrideEnvVar, previous); }
            Configs = new(new ServerConfigService(paths));
            Service = new(paths, Properties, Configs, Jars, Bedrock, new BedrockPropertiesWriter());
        }

        public NewServerOptions Options(string type = "Vanilla", string version = "1.21.1") =>
            new() { Name = "Synthetic provisioning test", DirectoryPath = Path.Combine(Root, "servers"),
                Type = type, Version = version, EulaAccepted = true };

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class JarDownloader : IServerJarService
    {
        public Func<string, Task> Download { get; set; } = _ => Task.CompletedTask;
        public int DownloadCalls { get; private set; }
        public string? RequestType { get; private set; }
        public string? RequestVersion { get; private set; }
        public async Task DownloadAsync(string serverType, string versionId, string destinationPath,
            string? javaPath, IProgress<string>? progress = null)
        {
            DownloadCalls++;
            RequestType = serverType;
            RequestVersion = versionId;
            await Download(destinationPath);
            await File.WriteAllTextAsync(destinationPath, "Synthetic test bytes, not an installed runtime or executable JAR.");
        }
    }

    private sealed class PropertiesWriter : IServerPropertiesService
    {
        public int SaveCalls { get; private set; }
        public Action? BeforeSave { get; set; }
        public ServerProperties Load(string serverDirectory, ServerConfig fallback) => throw new NotSupportedException();
        public void Save(string serverDirectory, ServerProperties properties)
        {
            BeforeSave?.Invoke();
            File.WriteAllText(Path.Combine(serverDirectory, "server.properties"), "# Synthetic properties fixture");
            SaveCalls++;
        }
    }

    private sealed class ConfigWriter(ServerConfigService inner) : IServerConfigService
    {
        public int SaveCalls { get; private set; }
        public IReadOnlyList<ServerConfig> LoadAll(IEnumerable<string>? additionalDirectories = null) => inner.LoadAll(additionalDirectories);
        public ServerConfig? Load(string serverId) => inner.Load(serverId);
        public ServerConfig? LoadFromDirectory(string serverDirectory) => inner.LoadFromDirectory(serverDirectory);
        public void Save(ServerConfig config) => throw new NotSupportedException();
        public void SaveToDirectory(ServerConfig config, string serverDirectory)
        {
            SaveCalls++;
            inner.SaveToDirectory(config, serverDirectory);
        }
    }

    private sealed class BedrockInstaller : IBedrockServerService
    {
        public int InstallCalls { get; private set; }
        public Action? OnInstall { get; set; }
        public Task<IReadOnlyList<BedrockVersionInfo>> GetAvailableVersionsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task InstallFromUrlAsync(string downloadUrl, string serverDirectory, IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            OnInstall?.Invoke();
            return Task.CompletedTask;
        }
        public Task InstallFromZipAsync(string zipPath, string serverDirectory, IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) => InstallFromUrlAsync(zipPath, serverDirectory, progress, cancellationToken);
        public bool IsInstalled(string serverDirectory) => throw new NotSupportedException();
    }

    private sealed class BedrockPropertiesWriter : IBedrockPropertiesService
    {
        public BedrockServerProperties Load(string serverDirectory) => new();
        public void Save(string serverDirectory, BedrockServerProperties properties) { }
        public IReadOnlyList<BedrockAllowlistEntry> LoadAllowlist(string serverDirectory) => throw new NotSupportedException();
        public void SaveAllowlist(string serverDirectory, IEnumerable<BedrockAllowlistEntry> entries) => throw new NotSupportedException();
    }

    private sealed class InlineProgress(Action onReport) : IProgress<string>
    {
        public void Report(string value) => onReport();
    }
}
