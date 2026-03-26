namespace McServerManager.Services;

/// <summary>
/// 外部APIのURLを一元管理する定数クラス。
/// サービス内に直接書かず、ここを参照すること。
/// </summary>
internal static class ExternalApiUrls
{
    // Minecraft
    public const string MinecraftVersionManifest =
        "https://launchermeta.mojang.com/mc/game/version_manifest.json";

    // サーバーJAR
    public const string PaperApiBase = "https://api.papermc.io/v2/projects";
    public const string FabricApiBase = "https://meta.fabricmc.net/v2/versions";
    public const string PurpurApiBase = "https://api.purpurmc.org/v2/purpur";
    public const string ForgeMavenMetadata =
        "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    public const string ForgeMavenBase = "https://maven.minecraftforge.net";
    public const string SpigotBuildTools =
        "https://hub.spigotmc.org/jenkins/job/BuildTools/lastSuccessfulBuild/artifact/target/BuildTools.jar";
    public const string SpigotDirectJarTemplate =
        "https://download.getbukkit.org/spigot/spigot-{0}.jar";

    // アドオンカタログ
    public const string ModrinthSearch = "https://api.modrinth.com/v2/search";

    // ネットワーク
    public const string PublicIpApi = "https://api.ipify.org";

    // アプリ更新
    public const string AppUpdateManifest =
        "https://www.maipilot.jp/updates/win-x64/update.json";
}
