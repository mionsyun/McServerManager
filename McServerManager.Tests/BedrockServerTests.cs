using System.IO.Compression;
using System.Text;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;
using McServerManager.ViewModels;

namespace McServerManager.Tests;

public sealed class BedrockServerTests
{
    private const string DownloadLinksJson =
        """
        {
          "result": {
            "links": [
              { "downloadType": "serverBedrockWindows", "downloadUrl": "https://www.minecraft.net/bedrockdedicatedserver/bin-win/bedrock-server-1.26.60.3.zip" },
              { "downloadType": "serverBedrockLinux", "downloadUrl": "https://www.minecraft.net/bedrockdedicatedserver/bin-linux/bedrock-server-1.26.60.3.zip" },
              { "downloadType": "serverBedrockPreviewWindows", "downloadUrl": "https://www.minecraft.net/bedrockdedicatedserver/bin-win-preview/bedrock-server-1.26.70.21.zip" },
              { "downloadType": "serverJar", "downloadUrl": "https://piston-data.mojang.com/v1/objects/abc/server.jar" }
            ]
          }
        }
        """;

    // ─── ダウンロードリンク ────────────────────────────────────────

    [Fact]
    public void ParseDownloadLinks_ReturnsWindowsReleaseThenPreview()
    {
        var versions = BedrockServerService.ParseDownloadLinks(DownloadLinksJson);

        Assert.Equal(2, versions.Count);
        Assert.False(versions[0].IsPreview);
        Assert.Equal("1.26.60.3", versions[0].Version);
        Assert.Contains("bin-win/", versions[0].DownloadUrl);
        Assert.True(versions[1].IsPreview);
        Assert.Equal("1.26.70.21", versions[1].Version);
    }

    [Fact]
    public void ParseDownloadLinks_IgnoresUntrustedHosts()
    {
        const string json = """
            { "links": [ { "downloadType": "serverBedrockWindows", "downloadUrl": "https://evil.example.com/bedrock-server-1.0.0.1.zip" } ] }
            """;

        Assert.Empty(BedrockServerService.ParseDownloadLinks(json));
    }

    [Theory]
    [InlineData("https://www.minecraft.net/bedrockdedicatedserver/bin-win/bedrock-server-1.21.100.7.zip", "1.21.100.7")]
    [InlineData(@"C:\Downloads\bedrock-server-1.26.60.3.zip", "1.26.60.3")]
    [InlineData(@"C:\Downloads\server.zip", null)]
    public void TryGetVersionFromFileName_ExtractsVersion(string input, string? expected)
    {
        Assert.Equal(expected, BedrockServerService.TryGetVersionFromFileName(input));
    }

    [Theory]
    [InlineData("https://www.minecraft.net/x.zip", true)]
    [InlineData("https://minecraft.net/x.zip", true)]
    [InlineData("http://www.minecraft.net/x.zip", false)]
    [InlineData("https://minecraft.net.evil.com/x.zip", false)]
    public void IsTrustedDownloadUrl_OnlyAllowsHttpsMinecraftNet(string url, bool expected)
    {
        Assert.Equal(expected, BedrockServerService.IsTrustedDownloadUrl(url));
    }

    // ─── zip 展開 ─────────────────────────────────────────────────

    [Fact]
    public void ExtractServerZip_UpdatePreservesUserFiles()
    {
        using var temp = new TemporaryDirectoryScope();
        var serverDir = Path.Combine(temp.Path, "server");
        var zip = CreateBdsZip(temp.Path, "server-name=Dedicated Server");

        BedrockServerService.ExtractServerZip(zip, serverDir);
        Assert.True(File.Exists(Path.Combine(serverDir, "bedrock_server.exe")));
        Assert.True(File.Exists(Path.Combine(serverDir, "behavior_packs", "vanilla", "manifest.json")));

        File.WriteAllText(Path.Combine(serverDir, "server.properties"), "server-name=My Server");
        File.WriteAllText(Path.Combine(serverDir, "allowlist.json"), "[{\"name\":\"Steve\"}]");
        Directory.CreateDirectory(Path.Combine(serverDir, "worlds", "Bedrock level"));

        BedrockServerService.ExtractServerZip(zip, serverDir);

        Assert.Equal("server-name=My Server", File.ReadAllText(Path.Combine(serverDir, "server.properties")));
        Assert.Contains("Steve", File.ReadAllText(Path.Combine(serverDir, "allowlist.json")));
        Assert.True(Directory.Exists(Path.Combine(serverDir, "worlds", "Bedrock level")));
    }

    [Fact]
    public void ExtractServerZip_RejectsZipWithoutExecutable()
    {
        using var temp = new TemporaryDirectoryScope();
        var zip = Path.Combine(temp.Path, "not-bds.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "bedrock_server", "linux binary");
        }

        Assert.Throws<InvalidOperationException>(() =>
            BedrockServerService.ExtractServerZip(zip, Path.Combine(temp.Path, "server")));
    }

    [Fact]
    public void ExtractServerZip_RejectsPathTraversal()
    {
        using var temp = new TemporaryDirectoryScope();
        var zip = Path.Combine(temp.Path, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "bedrock_server.exe", "exe");
            WriteEntry(archive, "../outside.txt", "evil");
        }

        Assert.Throws<InvalidOperationException>(() =>
            BedrockServerService.ExtractServerZip(zip, Path.Combine(temp.Path, "server")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "outside.txt")));
    }

    // ─── server.properties / allowlist ─────────────────────────────

    [Fact]
    public void PropertiesSave_KeepsCommentsAndUnknownKeys()
    {
        using var temp = new TemporaryDirectoryScope();
        File.WriteAllLines(Path.Combine(temp.Path, "server.properties"),
        [
            "server-name=Dedicated Server",
            "# Used as the server name",
            "gamemode=survival",
            "compression-threshold=1",
            "transport=nethernet"
        ]);
        var service = new BedrockPropertiesService();

        var props = service.Load(temp.Path);
        props.ServerName = "友達;サーバー";
        props.GameMode = "creative";
        props.Transport = "raknet";
        service.Save(temp.Path, props);

        var lines = File.ReadAllLines(Path.Combine(temp.Path, "server.properties"));
        Assert.Contains("server-name=友達サーバー", lines);
        Assert.Contains("# Used as the server name", lines);
        Assert.Contains("gamemode=creative", lines);
        Assert.Contains("compression-threshold=1", lines);
        Assert.Contains("transport=raknet", lines);
    }

    [Fact]
    public void PropertiesSave_DoesNotAddEmptyNetherNetKeysToOldServers()
    {
        using var temp = new TemporaryDirectoryScope();
        File.WriteAllLines(Path.Combine(temp.Path, "server.properties"), ["server-name=Old", "server-port=19132"]);
        var service = new BedrockPropertiesService();

        service.Save(temp.Path, service.Load(temp.Path));

        var text = File.ReadAllText(Path.Combine(temp.Path, "server.properties"));
        Assert.DoesNotContain("transport=", text);
        Assert.DoesNotContain("server-udp-ports=", text);
        Assert.DoesNotContain("server-ip=", text);
    }

    [Fact]
    public void Allowlist_RoundTripsWithBdsFieldNames()
    {
        using var temp = new TemporaryDirectoryScope();
        var service = new BedrockPropertiesService();

        service.SaveAllowlist(temp.Path, [new BedrockAllowlistEntry { Name = "Steve", IgnoresPlayerLimit = true }]);

        var json = File.ReadAllText(Path.Combine(temp.Path, "allowlist.json"));
        Assert.Contains("\"name\": \"Steve\"", json);
        Assert.Contains("\"ignoresPlayerLimit\": true", json);
        Assert.DoesNotContain("xuid", json);
        Assert.Equal("Steve", Assert.Single(service.LoadAllowlist(temp.Path)).Name);
    }

    // ─── 通信方式とポート ─────────────────────────────────────────

    [Theory]
    [InlineData("", "1.26.51.1", true)]
    [InlineData("", "1.21.100.7", false)]
    [InlineData("", "unknown", false)]
    [InlineData("raknet", "1.26.60.3", false)]
    [InlineData("nethernet", "1.21.100.7", true)]
    public void UsesNetherNet_UsesTransportOrVersionDefault(string transport, string version, bool expected)
    {
        var props = new BedrockServerProperties { Transport = transport };
        Assert.Equal(expected, BedrockNetworkPlanner.UsesNetherNet(props, version));
    }

    [Fact]
    public void GetRequiredPorts_NetherNet_IsTcpSignalingPlusUdpRange()
    {
        var props = new BedrockServerProperties
        {
            Transport = "nethernet",
            ServerPort = 19132,
            ServerUdpPorts = "203.0.113.10:19300-19309:19400-19409"
        };

        var ports = BedrockNetworkPlanner.GetRequiredPorts(props, "1.26.60.3");

        Assert.Equal(2, ports.Count);
        Assert.Equal(new PortRequirement(NetworkProtocol.Tcp, 19132, 19132, 19132, ports[0].Purpose), ports[0]);
        Assert.Equal(NetworkProtocol.Udp, ports[1].Protocol);
        Assert.Equal(19300, ports[1].ExternalStart);
        Assert.Equal(19309, ports[1].ExternalEnd);
        Assert.Equal(19400, ports[1].InternalStart);
        Assert.Equal("UDP 19300-19309", ports[1].Label);
    }

    [Fact]
    public void GetRequiredPorts_RakNet_IsUdpServerPort()
    {
        var props = new BedrockServerProperties { Transport = "raknet", ServerPort = 19132 };

        var port = Assert.Single(BedrockNetworkPlanner.GetRequiredPorts(props, "1.26.60.3"));

        Assert.Equal(NetworkProtocol.Udp, port.Protocol);
        Assert.Equal("UDP 19132", port.Label);
    }

    [Theory]
    [InlineData("19300-19319:19300-19319", true)]
    [InlineData("203.0.113.10:19300-19319:19300-19319", true)]
    [InlineData("19300:19300", true)]
    [InlineData("19300-19319", false)]
    [InlineData("19300-19319:19300-19310", false)]
    [InlineData("::1:19300:19300", false)]
    [InlineData("999.1.1.1:19300:19300", false)]
    [InlineData("19320-19300:19320-19300", false)]
    public void TryParseUdpPorts_ValidatesFormat(string value, bool expected)
    {
        Assert.Equal(expected, BedrockNetworkPlanner.TryParseUdpPorts(value, out _, out _));
    }

    [Fact]
    public void GetWarnings_FlagsSinglePortAndMissingIp()
    {
        var props = new BedrockServerProperties { Transport = "nethernet", MaxPlayers = 10, ServerUdpPorts = "19300:19300" };

        var warnings = BedrockNetworkPlanner.GetWarnings(props, "1.26.60.3");

        Assert.Contains(warnings, w => w.Contains("1 人しか"));
        Assert.Contains(warnings, w => w.Contains("グローバル IP"));
    }

    [Fact]
    public void GetWarnings_FlagsRangeSmallerThanMaxPlayers()
    {
        var props = new BedrockServerProperties
        {
            Transport = "nethernet",
            MaxPlayers = 20,
            ServerUdpPorts = "203.0.113.10:19300-19309:19300-19309"
        };

        var warning = Assert.Single(BedrockNetworkPlanner.GetWarnings(props, "1.26.60.3"));
        Assert.Contains("最大人数", warning);
    }

    [Fact]
    public void BuildUdpPortsValue_BuildsRangeWithOptionalIp()
    {
        Assert.Equal("19300-19309:19300-19309", BedrockNetworkPlanner.BuildUdpPortsValue(null, 19300, 10));
        Assert.Equal("203.0.113.10:19300-19309:19300-19309", BedrockNetworkPlanner.BuildUdpPortsValue("203.0.113.10", 19300, 10));
    }

    [Fact]
    public async Task PortForwarding_OpensEveryPortInRangeWithProtocol()
    {
        using var temp = new TemporaryDirectoryScope();
        File.WriteAllText(Path.Combine(temp.Path, "bedrock_server.exe"), "exe");
        File.WriteAllLines(Path.Combine(temp.Path, "server.properties"),
            ["server-port=19132", "transport=nethernet", "server-udp-ports=19300-19302:19300-19302"]);
        var upnp = new StubUpnpService();
        var service = new PortForwardingService(upnp, new BedrockPropertiesService());
        var config = new ServerConfig { Type = "Bedrock", Version = "1.26.60.3", DirectoryPath = temp.Path, Name = "be" };

        var plan = service.GetPlan(config);
        var error = await service.OpenAsync(config, plan.Ports);

        Assert.Null(error);
        Assert.True(plan.UsesNetherNet);
        Assert.Equal(
            [(19132, NetworkProtocol.Tcp, (int?)19132), (19300, NetworkProtocol.Udp, 19300), (19301, NetworkProtocol.Udp, 19301), (19302, NetworkProtocol.Udp, 19302)],
            upnp.Opened);
    }

    [Fact]
    public void PortForwarding_JavaServerIsSingleTcpPort()
    {
        var service = new PortForwardingService(new StubUpnpService(), new BedrockPropertiesService());

        var plan = service.GetPlan(new ServerConfig { Type = "Paper", Port = 25565 });

        Assert.Equal("TCP 25565", Assert.Single(plan.Ports).Label);
        Assert.False(plan.UsesNetherNet);
    }

    // ─── 作成・ワールド ─────────────────────────────────────────

    [Fact]
    public async Task Provisioning_CreatesBedrockServerFromZip()
    {
        using var appData = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        var bedrockProperties = new BedrockPropertiesService();
        var provisioning = new ServerProvisioningService(
            paths,
            new ServerPropertiesService(),
            new ServerConfigService(paths),
            new FakeServerJarService(),
            new StubBedrockServerService(),
            bedrockProperties);
        var zip = CreateBdsZip(appData.AppDataPath, "server-name=Dedicated Server\ntransport=nethernet\nserver-port=19132");

        var config = await provisioning.CreateAsync(new NewServerOptions
        {
            Name = "統合版テスト",
            Type = ServerEditions.BedrockType,
            Version = "1.26.60.3",
            BedrockZipPath = zip,
            Motd = "統合版テスト",
            GameMode = "creative",
            Difficulty = "peaceful",
            AllowCheats = true,
            MaxPlayers = 8,
            Port = 19140,
            PortV6 = 19141,
            EulaAccepted = true
        });

        Assert.True(ServerEditions.IsBedrock(config));
        Assert.True(File.Exists(Path.Combine(config.DirectoryPath, "bedrock_server.exe")));
        Assert.False(File.Exists(Path.Combine(config.DirectoryPath, "eula.txt")));
        var props = bedrockProperties.Load(config.DirectoryPath);
        Assert.Equal("統合版テスト", props.ServerName);
        Assert.Equal("creative", props.GameMode);
        Assert.True(props.AllowCheats);
        Assert.Equal(19140, props.ServerPort);
        // NetherNet は接続不具合の報告が多いため、作成時の既定は RakNet
        Assert.Equal("raknet", props.Transport);
    }

    [Fact]
    public async Task Provisioning_DoesNotAddTransportToOldBedrockServer()
    {
        using var appData = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        var bedrockProperties = new BedrockPropertiesService();
        var provisioning = new ServerProvisioningService(
            paths,
            new ServerPropertiesService(),
            new ServerConfigService(paths),
            new FakeServerJarService(),
            new StubBedrockServerService(),
            bedrockProperties);
        var zip = CreateBdsZip(appData.AppDataPath, "server-name=Dedicated Server\nserver-port=19132");

        var config = await provisioning.CreateAsync(new NewServerOptions
        {
            Name = "旧版",
            Type = ServerEditions.BedrockType,
            Version = "1.21.100.7",
            BedrockZipPath = zip,
            Motd = "旧版",
            EulaAccepted = true
        });

        Assert.DoesNotContain("transport=", File.ReadAllText(Path.Combine(config.DirectoryPath, "server.properties")));
    }

    [Fact]
    public void WorldService_UsesWorldsFolderForBedrock()
    {
        using var temp = new TemporaryDirectoryScope();
        File.WriteAllText(Path.Combine(temp.Path, "bedrock_server.exe"), "exe");
        var world = Directory.CreateDirectory(Path.Combine(temp.Path, "worlds", "Bedrock level")).FullName;
        File.WriteAllText(Path.Combine(world, "level.dat"), "dat");
        var service = new WorldService();

        Assert.Equal(Path.Combine(temp.Path, "worlds"), service.GetWorldsRoot(temp.Path));
        Assert.Equal("Bedrock level", Assert.Single(service.GetWorlds(temp.Path)));

        var backup = service.CreateWorldBackup(temp.Path, "Bedrock level");
        Assert.StartsWith(Path.Combine(temp.Path, "backups"), backup.FullPath);
    }

    [Theory]
    [InlineData("1.26.60.3", "1.26.51.1", 1)]
    [InlineData("1.26.51.1", "1.26.60.3", -1)]
    [InlineData("1.26.60.3", "1.26.60.3", 0)]
    [InlineData("1.26.60.3", "unknown", 1)]
    [InlineData("latest", "1.26.60.3", 0)]
    public void CompareVersions_HandlesFourPartVersions(string left, string right, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(BedrockUpdateViewModel.CompareVersions(left, right)));
    }

    private static string CreateBdsZip(string directory, string properties)
    {
        var zipPath = Path.Combine(directory, $"bedrock-server-1.26.60.3-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        WriteEntry(archive, "bedrock_server.exe", "exe");
        WriteEntry(archive, "server.properties", properties);
        WriteEntry(archive, "allowlist.json", "[]");
        WriteEntry(archive, "behavior_packs/vanilla/manifest.json", "{}");
        return zipPath;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
