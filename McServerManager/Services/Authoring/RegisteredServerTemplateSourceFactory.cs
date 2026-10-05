using McServerManager.Models;
using McServerManager.Models.Authoring;

namespace McServerManager.Services.Authoring;

/// <summary>Copies registration values synchronously on the owning UI thread. Never reads a server directory.</summary>
public static class RegisteredServerTemplateSourceFactory
{
    public static RegisteredServerTemplateSource Create(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new(config.ServerId, config.Type, config.Version, config.Name, config.Difficulty,
            config.GameMode, config.MaxPlayers, config.ViewDistance, config.Pvp, config.SpawnProtection,
            config.CreationRuntimeDeclaration);
    }
}
