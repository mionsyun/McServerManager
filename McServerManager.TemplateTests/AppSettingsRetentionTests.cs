using System.Text;
using System.Text.Json;
using McServerManager.Models;
using McServerManager.Services;

namespace McServerManager.TemplateTests;

public sealed class AppSettingsRetentionTests
{
    [Fact]
    public void FreshInstall_CanSaveDefaults_WithoutInventingASchemaMarker()
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        var settings = service.Load();
        Assert.False(File.Exists(fixture.Path));
        service.Save(settings);
        Assert.Equal("Dark", service.Load().Theme);
        Assert.False(File.ReadAllText(fixture.Path).Contains("SchemaVersion", StringComparison.Ordinal));
        Assert.False(File.Exists(fixture.BackupPath));
    }

    [Fact]
    public void UpgradeAndDowngrade_PreserveSharedFieldsAndUnknownNestedData()
    {
        using var fixture = new Fixture();
        const string original = """
            {"Theme":"Light","HasShownFirstRun":true,"HasCompletedTutorial":true,
             "ServerDirectories":["C:\\servers\\alpha","D:\\Minecraft\\beta"],
             "EnableUpnp":true,"PromptUpnp":false,"DeferredAppUpdateVersion":"2.4.0",
             "DeferredAppUpdateUntilUtc":"2026-10-09T10:00:00Z",
             "FutureSharedSetting":{"enabled":true,"items":[1,null,"retained"]},
             "BackupPolicy":{"IntervalMinutes":45,"Destination":"E:\\backups"}}
            """;
        File.WriteAllText(fixture.Path, original);
        IAppSettingsService free = fixture.Service();
        IAppSettingsService pro = fixture.Service();

        pro.Update(settings => settings.Theme = "Dark");
        free.Update(settings => settings.HasCompletedTutorial = false);

        var saved = free.Load();
        Assert.True(saved.HasShownFirstRun);
        Assert.False(saved.HasCompletedTutorial);
        Assert.True(saved.EnableUpnp);
        Assert.False(saved.PromptUpnp);
        Assert.Equal(new[] { @"C:\servers\alpha", @"D:\Minecraft\beta" }, saved.ServerDirectories);
        Assert.Equal("2.4.0", saved.DeferredAppUpdateVersion);
        Assert.Equal(new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc), saved.DeferredAppUpdateUntilUtc);
        using var json = JsonDocument.Parse(File.ReadAllBytes(fixture.Path));
        Assert.Equal("retained", json.RootElement.GetProperty("FutureSharedSetting").GetProperty("items")[2].GetString());
        Assert.Equal(@"E:\backups", json.RootElement.GetProperty("BackupPolicy").GetProperty("Destination").GetString());
    }

    [Fact]
    public void Save_LeavesExactPreviousBytesInBackup_AndNoTemporaryFile()
    {
        using var fixture = new Fixture();
        var before = Encoding.UTF8.GetBytes("{ \"Theme\":\"Light\", \"Future\": 123 }");
        File.WriteAllBytes(fixture.Path, before);
        var service = fixture.Service();
        var settings = service.Load();
        settings.Theme = "Dark";
        service.Save(settings);
        Assert.Equal(before, File.ReadAllBytes(fixture.BackupPath));
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        settings.PromptUpnp = false;
        service.Save(settings); // The successful save refreshes this object's revision.
        Assert.False(service.Load().PromptUpnp);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"Theme\":null}")]
    [InlineData("{\"Theme\":123}")]
    [InlineData("{\"ServerDirectories\":null}")]
    [InlineData("{\"ServerDirectories\":[null]}")]
    [InlineData("{\"Theme\":\"Light\",\"Theme\":\"Dark\"}")]
    [InlineData("{\"Theme\":\"Light\",\"theme\":\"Dark\"}")]
    [InlineData("{\"theme\":\"Light\"}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"Future\":{\"nested\":[\"\\uD800\"]}}")]
    [InlineData("{\"Future\":{\"\\uDC00\":true}}")]
    public void InvalidSettings_CannotLoadUpdateOrBeOverwrittenWithDefaults(string json)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, json);
        var service = fixture.Service();
        AssertError(AppSettingsStorageError.InvalidData, () => service.Load());
        AssertError(AppSettingsStorageError.InvalidData, () => service.Save(new AppSettings()));
        AssertError(AppSettingsStorageError.InvalidData, () => service.Update(settings => settings.Theme = "Dark"));
        Assert.Equal(json, File.ReadAllText(fixture.Path));
        Assert.False(File.Exists(fixture.BackupPath));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("null")]
    [InlineData("\"1\"")]
    public void UnsupportedSchema_IsNeverDowngraded(string version)
    {
        using var fixture = new Fixture();
        var json = "{\"SchemaVersion\":" + version + ",\"Theme\":\"Light\"}";
        File.WriteAllText(fixture.Path, json);
        var service = fixture.Service();
        AssertError(AppSettingsStorageError.UnsupportedVersion, () => service.Load());
        AssertError(AppSettingsStorageError.UnsupportedVersion, () => service.Save(new AppSettings()));
        Assert.Equal(json, File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void SupportedOptionalSchemaMarkerAndUtf8Bom_AreAcceptedAndRetained()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, "{\"SchemaVersion\":1,\"Theme\":\"Light\"}", new UTF8Encoding(true));
        var service = fixture.Service();
        Assert.Equal("Light", service.Load().Theme);
        service.Update(settings => settings.PromptUpnp = false);
        using var json = JsonDocument.Parse(File.ReadAllBytes(fixture.Path));
        Assert.Equal(1, json.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    [Fact]
    public void InvalidUtf8_IsRejectedWithoutNormalization()
    {
        using var fixture = new Fixture();
        var contents = Encoding.UTF8.GetBytes("{\"Future\":\"").Concat(new byte[] { 0xc0, 0xaf })
            .Concat(Encoding.UTF8.GetBytes("\"}")).ToArray();
        File.WriteAllBytes(fixture.Path, contents);
        AssertError(AppSettingsStorageError.InvalidData, () => fixture.Service().Load());
        Assert.Equal(contents, File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public void MissingPrimaryWithBackup_RequiresRecoveryInsteadOfFreshDefaults()
    {
        using var fixture = new Fixture();
        const string backup = "{\"Theme\":\"Light\"}";
        File.WriteAllText(fixture.BackupPath, backup);
        var service = fixture.Service();
        AssertError(AppSettingsStorageError.InvalidData, () => service.Load());
        AssertError(AppSettingsStorageError.InvalidData, () => service.Save(new AppSettings()));
        Assert.False(File.Exists(fixture.Path));
        Assert.Equal(backup, File.ReadAllText(fixture.BackupPath));
    }

    [Fact]
    public void CorruptPrimary_DoesNotSilentlyRestoreOrOverwriteExistingBackup()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, "broken");
        File.WriteAllText(fixture.BackupPath, "{\"Theme\":\"Light\"}");
        AssertError(AppSettingsStorageError.InvalidData, () => fixture.Service().Save(new AppSettings()));
        Assert.Equal("broken", File.ReadAllText(fixture.Path));
        Assert.Equal("{\"Theme\":\"Light\"}", File.ReadAllText(fixture.BackupPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleSave_AcrossScreensOrServiceInstances_CannotLoseNewerFields(bool anotherInstance)
    {
        using var fixture = new Fixture();
        var first = fixture.Service();
        first.Save(new AppSettings());
        var stale = first.Load();
        var second = anotherInstance ? fixture.Service() : first;
        second.Update(settings => settings.ServerDirectories.Add(@"D:\new-server"));
        var latest = File.ReadAllBytes(fixture.Path);
        stale.Theme = "Light";
        AssertError(AppSettingsStorageError.Conflict, () => first.Save(stale));
        Assert.Equal(latest, File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public void MissingFileSnapshot_CannotReplaceSettingsCreatedByOtherInstance()
    {
        using var fixture = new Fixture();
        var first = fixture.Service();
        var stale = first.Load();
        fixture.Service().Save(new AppSettings { Theme = "Light" });
        AssertError(AppSettingsStorageError.Conflict, () => first.Save(stale));
        AssertError(AppSettingsStorageError.Conflict, () => first.Save(new AppSettings()));
        Assert.Equal("Light", first.Load().Theme);
    }

    [Fact]
    public void ChangesByNoncooperatingWriter_AreDetectedBeforeSave()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, "{\"Theme\":\"Light\"}");
        var service = fixture.Service();
        var stale = service.Load();
        const string external = "{\"Theme\":\"Light\",\"NewField\":true}";
        File.WriteAllText(fixture.Path, external);
        AssertError(AppSettingsStorageError.Conflict, () => service.Save(stale));
        Assert.Equal(external, File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void Update_DetectsNoncooperatingChangesDuringCallback()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, "{\"Theme\":\"Light\"}");
        const string external = "{\"Theme\":\"Light\",\"NewField\":true}";
        AssertError(AppSettingsStorageError.Conflict, () => fixture.Service().Update(settings =>
        {
            File.WriteAllText(fixture.Path, external);
            settings.Theme = "Dark";
        }));
        Assert.Equal(external, File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void OrphanedStagingFile_DoesNotReplaceCommittedSettingsOrGetDeleted()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, "{\"Theme\":\"Light\"}");
        var orphan = fixture.Path + ".crashed-process.tmp";
        File.WriteAllText(orphan, "{\"Theme\":\"Dark\"}");
        var service = fixture.Service();
        Assert.Equal("Light", service.Load().Theme);
        service.Update(settings => settings.PromptUpnp = false);
        Assert.Equal("{\"Theme\":\"Dark\"}", File.ReadAllText(orphan));
    }

    [Fact]
    public void ReadAndWriteLimits_AreEnforcedBeforeTouchingExistingData()
    {
        using var fixture = new Fixture();
        var exact = "{\"Future\":\"" + new string('x', AppSettingsFileStore.MaxFileBytes - 13) + "\"}";
        Assert.Equal(AppSettingsFileStore.MaxFileBytes, Encoding.UTF8.GetByteCount(exact));
        File.WriteAllText(fixture.Path, exact);
        var service = fixture.Service();
        var settings = service.Load();
        AssertError(AppSettingsStorageError.TooLarge, () => service.Save(settings)); // Formatting + known fields exceed the limit.
        Assert.Equal(exact, File.ReadAllText(fixture.Path));
        File.AppendAllText(fixture.Path, " ");
        AssertError(AppSettingsStorageError.TooLarge, () => service.Load());
        Assert.False(File.Exists(fixture.BackupPath));
    }

    [Fact]
    public void ExcessiveJsonDepth_IsRejected()
    {
        using var fixture = new Fixture();
        var json = "{\"Future\":" + new string('[', 33) + "0" + new string(']', 33) + "}";
        File.WriteAllText(fixture.Path, json);
        AssertError(AppSettingsStorageError.InvalidData, () => fixture.Service().Load());
        Assert.Equal(json, File.ReadAllText(fixture.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedStaging_LeavesOriginalAndBackupIntact(bool afterFlush)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, "{\"Theme\":\"Light\"}");
        File.WriteAllText(fixture.BackupPath, "{\"Theme\":\"Dark\"}");
        var service = fixture.Service(new InterruptedCommitOperations(afterFlush));
        var settings = service.Load();
        settings.Theme = "Dark";
        AssertError(AppSettingsStorageError.WriteFailed, () => service.Save(settings));
        Assert.Equal("{\"Theme\":\"Light\"}", File.ReadAllText(fixture.Path));
        Assert.Equal("{\"Theme\":\"Dark\"}", File.ReadAllText(fixture.BackupPath));
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public void BackupFailure_DoesNotFallBackToDestructiveOverwrite()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, "{\"Theme\":\"Light\"}");
        Directory.CreateDirectory(fixture.BackupPath);
        File.WriteAllText(System.IO.Path.Combine(fixture.BackupPath, "sentinel"), "retained");
        var service = fixture.Service();
        var settings = service.Load();
        settings.Theme = "Dark";
        AssertError(AppSettingsStorageError.WriteFailed, () => service.Save(settings));
        Assert.Equal("{\"Theme\":\"Light\"}", File.ReadAllText(fixture.Path));
        Assert.Equal("retained", File.ReadAllText(System.IO.Path.Combine(fixture.BackupPath, "sentinel")));
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public void HeldLease_BoundsContentionAndReleasesForNextWriter()
    {
        using var fixture = new Fixture();
        var store = new AppSettingsFileStore(fixture.Path, lockTimeout: TimeSpan.FromMilliseconds(75));
        var service = new AppSettingsService(store);
        using (store.AcquireLock())
        {
            AssertError(AppSettingsStorageError.Busy, () => service.Load());
            AssertError(AppSettingsStorageError.Busy, () => service.Save(new AppSettings()));
        }
        service.Save(new AppSettings());
        Assert.True(File.Exists(fixture.Path + ".lock"));
        Assert.Equal("Dark", service.Load().Theme);
    }

    [Fact]
    public async Task Update_HoldsLeaseAcrossReadApplyAndCommit()
    {
        using var fixture = new Fixture();
        var first = fixture.Service();
        var second = fixture.Service();
        using var applying = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var firstSave = Task.Run(() => first.Update(settings =>
        {
            applying.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            settings.EnableUpnp = true;
        }));
        Assert.True(applying.Wait(TimeSpan.FromSeconds(5)));
        var secondSave = Task.Run(() => second.Update(settings => settings.Theme = "Light"));
        try
        {
            await Task.Delay(75);
            Assert.False(secondSave.IsCompleted);
        }
        finally { release.Set(); }
        await Task.WhenAll(firstSave, secondSave);
        var saved = second.Load();
        Assert.True(saved.EnableUpnp);
        Assert.Equal("Light", saved.Theme);
    }

    [Fact]
    public void FailedUpdateCallback_DoesNotSaveAndReleasesLease()
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        service.Save(new AppSettings());
        var original = File.ReadAllBytes(fixture.Path);
        Assert.Throws<InvalidOperationException>(() => service.Update(settings =>
        {
            settings.Theme = "Light";
            throw new InvalidOperationException("synthetic callback failure");
        }));
        Assert.Equal(original, File.ReadAllBytes(fixture.Path));
        service.Update(settings => settings.PromptUpnp = false);
        Assert.False(service.Load().PromptUpnp);
    }

    private static void AssertError(AppSettingsStorageError expected, Action action) =>
        Assert.Equal(expected, Assert.Throws<AppSettingsStorageException>(action).Reason);

    private sealed class InterruptedCommitOperations(bool afterFlush) : IAppSettingsCommitOperations
    {
        public void WriteTemporaryFile(string path, byte[] contents)
        {
            if (afterFlush) new AppSettingsCommitOperations().WriteTemporaryFile(path, contents);
            else File.WriteAllBytes(path, contents[..Math.Min(contents.Length, 7)]);
            throw new IOException("Synthetic interrupted staging.");
        }
        public void ReplaceFile(string temporaryPath, string settingsPath, string backupPath) => throw new InvalidOperationException();
        public void MoveFile(string temporaryPath, string settingsPath) => throw new InvalidOperationException();
    }

    private sealed class Fixture : IDisposable
    {
        internal string DirectoryPath { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maipilot-settings-tests-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(DirectoryPath, "appsettings.json");
        internal string BackupPath => Path + ".bak";
        internal Fixture() => Directory.CreateDirectory(DirectoryPath);
        internal AppSettingsService Service(IAppSettingsCommitOperations? operations = null) => new(new AppSettingsFileStore(Path, operations));
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
