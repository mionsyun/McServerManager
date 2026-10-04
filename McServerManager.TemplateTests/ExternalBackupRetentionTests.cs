using System.Collections;
using System.Reflection;
using McServerManager.Models;
using McServerManager.Services;
namespace McServerManager.TemplateTests;

public sealed class ExternalBackupRetentionTests
{
    [Fact]
    public void RestorationIncludesExplicitExternalRootsAndEnabledSchedulesOnly()
    {
        var configs = new Configurations();
        configs.Items = [new() { ServerId = "enabled", ScheduledBackupEnabled = true }, new() { ServerId = "disabled" }];
        var scheduler = new Scheduler();
        new BackupScheduleRestorer(configs, scheduler).Restore(new() { ServerDirectories = ["external-one", "external-two"] });
        Assert.Equal(new[] { "external-one", "external-two" }, configs.Roots);
        Assert.Equal("enabled", Assert.Single(scheduler.Applied).ServerId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimerReloadUsesOriginalExternalDirectoryAndRejectsChangedIdentity(bool changedIdentity)
    {
        var root = Path.Combine(Path.GetTempPath(), "maipilot-backup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "world"));
        try
        {
            var config = new ServerConfig { ServerId = "server-1", DirectoryPath = root, ScheduledBackupEnabled = true,
                ScheduledBackupIntervalMinutes = 60, ScheduledBackupMaxRetention = 3 };
            var configs = new Configurations { Current = config };
            var worlds = new Worlds();
            using var scheduler = new BackupSchedulerService(worlds, configs);
            scheduler.ApplyConfiguration(config);
            var entries = (IDictionary)typeof(BackupSchedulerService).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scheduler)!;
            var entry = entries["server-1"]!;
            if (changedIdentity) configs.Current = new() { ServerId = "different", DirectoryPath = root, ScheduledBackupEnabled = true };
            typeof(BackupSchedulerService).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(scheduler, [entry]);
            Assert.Equal(root, configs.LastDirectory);
            Assert.Equal(0, configs.DefaultLoads);
            Assert.Equal(changedIdentity ? 0 : 1, worlds.Backups);
            Assert.Equal(changedIdentity ? 0 : 1, configs.Saves);
            if (!changedIdentity) Assert.Equal(root, configs.Saved!.DirectoryPath);
            else Assert.Null(scheduler.GetNextRunAt("server-1"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private sealed class Configurations : IServerConfigService
    {
        public IReadOnlyList<ServerConfig> Items { get; set; } = [];
        public string[] Roots { get; private set; } = [];
        public ServerConfig? Current { get; set; }
        public string? LastDirectory { get; private set; }
        public int DefaultLoads { get; private set; }
        public int Saves { get; private set; }
        public ServerConfig? Saved { get; private set; }
        public IReadOnlyList<ServerConfig> LoadAll(IEnumerable<string>? additionalDirectories = null) { Roots = additionalDirectories?.ToArray() ?? []; return Items; }
        public ServerConfig? Load(string serverId) { DefaultLoads++; return null; }
        public ServerConfig? LoadFromDirectory(string serverDirectory) { LastDirectory = serverDirectory; return Current; }
        public void Save(ServerConfig config) { Saves++; Saved = config; }
        public void SaveToDirectory(ServerConfig config, string serverDirectory) => throw new NotSupportedException();
    }
    private sealed class Scheduler : IBackupSchedulerService
    {
        public List<ServerConfig> Applied { get; } = [];
        public void ApplyConfiguration(ServerConfig config) => Applied.Add(config);
        public void Unschedule(string id) { }
        public void UnscheduleAll() { }
        public DateTime? GetNextRunAt(string id) => null;
    }
    private sealed class Worlds : IWorldService
    {
        public int Backups { get; private set; }
        public string GetWorldsRoot(string directory) => directory;
        public WorldBackupEntry CreateAutomaticWorldBackup(string directory, string world, int retention) { Backups++; return new(); }
        public IReadOnlyList<string> GetWorlds(string directory) => throw new NotSupportedException();
        public string GetBackupsDirectory(string directory) => throw new NotSupportedException();
        public IReadOnlyList<WorldBackupEntry> GetWorldBackups(string directory) => throw new NotSupportedException();
        public WorldBackupEntry CreateWorldBackup(string directory, string world) => throw new NotSupportedException();
        public string RestoreWorldBackup(string directory, string zip, string world, bool overwrite, bool before) => throw new NotSupportedException();
        public void DeleteWorldBackup(string zip) => throw new NotSupportedException();
        public void CreateWorldFolder(string directory, string world) => throw new NotSupportedException();
        public void DeleteWorld(string directory, string world) => throw new NotSupportedException();
        public IReadOnlyList<ArchiveWorldCandidate> GetArchiveWorldCandidates(string path) => throw new NotSupportedException();
        public string ImportWorldArchive(string directory, string path, string world, bool overwrite, string? relative = null) => throw new NotSupportedException();
    }
}
