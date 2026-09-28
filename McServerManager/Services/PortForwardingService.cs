using McServerManager.Models;

namespace McServerManager.Services;

public sealed class PortForwardingService : IPortForwardingService
{
    // 誤設定で大量のポートを UPnP 登録しないための上限
    private const int MaxPortsPerRequirement = 100;

    private readonly IUpnpService _upnp;
    private readonly IBedrockPropertiesService _bedrockProperties;

    public PortForwardingService(IUpnpService upnp, IBedrockPropertiesService bedrockProperties)
    {
        _upnp = upnp;
        _bedrockProperties = bedrockProperties;
    }

    public PortPlan GetPlan(ServerConfig config)
    {
        if (!ServerEditions.IsBedrock(config))
        {
            return new PortPlan(
                [new PortRequirement(NetworkProtocol.Tcp, config.Port, config.Port, config.Port, "サーバー")],
                UsesNetherNet: false,
                Warnings: []);
        }

        var props = LoadBedrockPropertiesOrDefault(config);
        return new PortPlan(
            BedrockNetworkPlanner.GetRequiredPorts(props, config.Version),
            BedrockNetworkPlanner.UsesNetherNet(props, config.Version),
            BedrockNetworkPlanner.GetWarnings(props, config.Version));
    }

    public async Task<string?> OpenAsync(ServerConfig config, IReadOnlyList<PortRequirement> ports)
    {
        var description = $"McServerManager_{config.Name}";
        foreach (var port in ports)
        {
            for (var offset = 0; offset < Math.Min(port.Count, MaxPortsPerRequirement); offset++)
            {
                var (ok, error) = await _upnp.TryOpenPortAsync(
                    port.ExternalStart + offset,
                    description,
                    port.Protocol,
                    port.InternalStart + offset).ConfigureAwait(false);
                if (!ok)
                    return error ?? "ポート開放に失敗しました。";
            }
        }

        return null;
    }

    public async Task CloseAsync(IReadOnlyList<PortRequirement> ports)
    {
        foreach (var port in ports)
        {
            for (var offset = 0; offset < Math.Min(port.Count, MaxPortsPerRequirement); offset++)
                await _upnp.TryClosePortAsync(port.ExternalStart + offset, port.Protocol).ConfigureAwait(false);
        }
    }

    private BedrockServerProperties LoadBedrockPropertiesOrDefault(ServerConfig config)
    {
        try
        {
            return _bedrockProperties.Load(config.DirectoryPath);
        }
        catch (IOException)
        {
            // 起動中の BDS がファイルを掴んでいる場合などは作成時の設定から推定する（表示・開放用）
            return new BedrockServerProperties
            {
                ServerPort = config.Port,
                ServerPortV6 = config.PortV6,
                MaxPlayers = config.MaxPlayers
            };
        }
    }
}
