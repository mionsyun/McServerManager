using System.Runtime.InteropServices;
using System.Security.Cryptography;
using McServerManager.Models.Transactions;
using McServerManager.Services.Transactions;

namespace McServerManager.TemplateTests.Transactions;

/// <summary>
/// TEST ONLY. Real temporary directories and Linux renameat2(RENAME_NOREPLACE), with inode checks at operation
/// boundaries. Assumes NO hostile concurrent process, uses an in-memory journal, and holds a simulated lease
/// under one lock. This is deliberately outside the production assembly and is NOT a Windows security adapter
/// or a crash-durable recovery implementation. Its sandbox is never a user/server directory.
/// </summary>
internal sealed class IsolatedDirectoryTransactionStore : ITransactionStorage, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly LinuxIdentity _anchor;
    private readonly LinuxIdentity _stagingAnchor;
    private readonly LinuxIdentity _targetAnchor;
    private bool _stopped = true;
    private string _lease = "lease-1";
    private string _baseline = "absent-1";

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "maipilot-synthetic-" + Guid.NewGuid().ToString("N"));
    public string StorageId { get; } = "test-" + Guid.NewGuid().ToString("N");
    public string StageRoot => Path.Combine(Root, "staging");
    public string TargetsRoot => Path.Combine(Root, "targets");
    public string? LatestStage { get; private set; }
    public int CreateCalls { get; private set; }
    public int CleanupCalls { get; private set; }
    public int PublishCalls { get; private set; }
    public bool ObservationUnknown { get; set; }
    public TransactionObservation? ObservationOverride { get; set; }
    public bool CleanupFails { get; set; }
    public bool ThrowCleanup { get; set; }
    public bool FailDuringWrite { get; set; }
    public Action? AfterDirectoryAllocated { get; set; }
    public Action? AfterCreate { get; set; }
    public Action<string>? AfterStage { get; set; }
    public Action? BeforePublish { get; set; }
    public Action? AfterPublish { get; set; }
    public TransactionStorageCapabilities Capabilities { get; set; } = new(TransactionStorageMode.IsolatedSyntheticTests, true, true, true, true);

    public IsolatedDirectoryTransactionStore()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This isolated test fixture requires Linux.");
        Directory.CreateDirectory(StageRoot);
        Directory.CreateDirectory(TargetsRoot);
        _anchor = LinuxIdentity.Read(Root);
        _stagingAnchor = LinuxIdentity.Read(StageRoot);
        _targetAnchor = LinuxIdentity.Read(TargetsRoot);
    }

    public TransactionTarget Target(string name = "new-server") => new(StorageId, _anchor.Token, name,
        "synthetic-server-1", _baseline, _lease);
    public string TargetPath(TransactionPlan plan) => Path.Combine(TargetsRoot, plan.Target.TargetName);
    public void SetRunning() { lock (_gate) { _stopped = false; _lease = "revoked-lease"; } }
    public void ChangeBaseline() { lock (_gate) _baseline = "absent-2"; }
    public void ReplaceLease() { lock (_gate) _lease = "lease-2"; }

    public Task<TransactionObservation> ObserveAsync(TransactionReceipt receipt, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ObservationUnknown) return Task.FromResult(TransactionObservation.Unknown);
            if (ObservationOverride is not null) return Task.FromResult(ObservationOverride.Value);
            if (receipt.StorageId != StorageId) return Task.FromResult(TransactionObservation.ConflictingPlan);
            if (!_entries.TryGetValue(receipt.InstallId, out var entry)) return Task.FromResult(TransactionObservation.NotStarted);
            if (entry.Receipt.PlanFingerprint != receipt.PlanFingerprint) return Task.FromResult(TransactionObservation.ConflictingPlan);
            if (entry.Published) return Task.FromResult(TransactionObservation.Published);
            if (entry.Removed) return Task.FromResult(TransactionObservation.NotStarted);
            if (!entry.IdentityCaptured) return Task.FromResult(TransactionObservation.Unknown);
            if (entry.Receipt.OwnerToken != receipt.OwnerToken) return Task.FromResult(TransactionObservation.OtherAttempt);
            // A disappearing stage without a proven cleanup/commit is ambiguous, never "rolled back".
            return Task.FromResult(Directory.Exists(entry.Path) ? TransactionObservation.OwnedStage : TransactionObservation.Unknown);
        }
    }

    public Task CreateStageAsync(TransactionReceipt receipt, TransactionPlan plan, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCalls++;
            AssertTarget(plan);
            if (_entries.TryGetValue(receipt.InstallId, out var old) &&
                (!old.Removed || old.Receipt.PlanFingerprint != receipt.PlanFingerprint)) throw new IOException("InstallId is reserved.");
            var path = Path.Combine(StageRoot, "stage-" + receipt.InstallId.ToString("N") + "-" + receipt.OwnerToken.ToString("N"));
            var entry = new Entry(receipt, path);
            _entries[receipt.InstallId] = entry; // Ownership is journaled BEFORE allocating the directory.
            if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Stage already exists.");
            // The journal entry is already uncertain: mkdir/identity capture may fail after allocation.
            LatestStage = path;
            Directory.CreateDirectory(path);
            AfterDirectoryAllocated?.Invoke();
            entry.RootIdentity = LinuxIdentity.Read(path);
            entry.IdentityCaptured = true;
            AfterCreate?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    public Task StageArtifactAsync(TransactionReceipt receipt, TransactionArtifact artifact, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = Owned(receipt);
            if (!TreeMatches(entry)) throw new IOException("Owned tree changed before staging.");
            var parts = artifact.Destination.Split('/');
            var path = entry.Path;
            var relative = "";
            for (var i = 0; i < parts.Length - 1; i++)
            {
                path = Path.Combine(path, parts[i]);
                relative = relative.Length == 0 ? parts[i] : relative + "/" + parts[i];
                if (!entry.Files.ContainsKey(relative))
                {
                    Directory.CreateDirectory(path);
                    entry.Files.Add(relative, new(LinuxIdentity.Read(path), null));
                }
            }
            path = Path.Combine(path, parts[^1]);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var content = FailDuringWrite ? bytes[..Math.Max(1, bytes.Length / 2)] : bytes;
                file.Write(content.Span);
                file.Flush(flushToDisk: true);
                entry.Files.Add(artifact.Destination, new(LinuxIdentity.Read(path), Hash(content.Span), content.Length));
                if (FailDuringWrite) throw new IOException("Injected partial write fault.");
            }
            AfterStage?.Invoke(path);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    public Task<TransactionPublishDecision> PublishAsync(TransactionReceipt receipt, TransactionPlan plan, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            PublishCalls++;
            var entry = Owned(receipt);
            BeforePublish?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            AssertTarget(plan); // Simulated stopped/start-exclusion lock spans verification and rename.
            if (!TreeMatches(entry)) throw new IOException("Staged content or identity changed.");
            var actualFiles = entry.Files.Where(x => x.Value.Hash is not null).ToArray();
            if (actualFiles.Length != plan.Artifacts.Length) throw new IOException("Incomplete or foreign staged tree.");
            foreach (var artifact in plan.Artifacts)
            {
                var path = Path.Combine(entry.Path, artifact.Destination);
                if (!entry.Files.TryGetValue(artifact.Destination, out var file) || file.Hash != artifact.Sha256 ||
                    new FileInfo(path).Length != artifact.SizeBytes || Hash(File.ReadAllBytes(path)) != artifact.Sha256)
                    throw new IOException("Staged bytes do not match the approved manifest.");
            }
            if (!LinuxIdentity.MoveNoReplace(entry.Path, TargetPath(plan))) return Task.FromResult(TransactionPublishDecision.Rejected);
            entry.Published = true; // Sticky publication fact before any injectable post-publication failure/cancellation.
            AfterPublish?.Invoke();
            return Task.FromResult(TransactionPublishDecision.Published);
        }
    }

    public Task<TransactionCleanupDecision> CleanupOwnedStageAsync(TransactionReceipt receipt, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            CleanupCalls++;
            if (ThrowCleanup) throw new IOException("Injected cleanup fault.");
            if (CleanupFails) return Task.FromResult(TransactionCleanupDecision.Failed);
            if (ObservationUnknown || !_entries.TryGetValue(receipt.InstallId, out var entry) || entry.Receipt != receipt || entry.Published)
                return Task.FromResult(TransactionCleanupDecision.RefusedForeignOrChanged);
            if (entry.Removed) return Task.FromResult(TransactionCleanupDecision.AlreadyAbsent);
            if (!entry.IdentityCaptured) return Task.FromResult(TransactionCleanupDecision.RefusedForeignOrChanged);
            if (!AnchorsMatch() || !TreeMatches(entry)) return Task.FromResult(TransactionCleanupDecision.RefusedForeignOrChanged);
            // Explicit journaled files/directories only. No recursive delete and no target path is ever used.
            foreach (var path in entry.Files.Where(x => x.Value.Hash is not null).Select(x => x.Key)) File.Delete(Path.Combine(entry.Path, path));
            foreach (var path in entry.Files.Where(x => x.Value.Hash is null).Select(x => x.Key).OrderByDescending(x => x.Length))
                Directory.Delete(Path.Combine(entry.Path, path));
            Directory.Delete(entry.Path);
            entry.Removed = true;
            return Task.FromResult(TransactionCleanupDecision.Removed);
        }
    }

    private Entry Owned(TransactionReceipt receipt)
    {
        if (!_entries.TryGetValue(receipt.InstallId, out var entry) || entry.Receipt != receipt || entry.Published || entry.Removed)
            throw new IOException("Receipt does not own this stage.");
        return entry;
    }

    private void AssertTarget(TransactionPlan plan)
    {
        if (!AnchorsMatch() || plan.Target.StorageId != StorageId || plan.Target.AnchorIdentity != _anchor.Token ||
            plan.Target.ServerIdentity != "synthetic-server-1" || plan.Target.AbsentBaseline != _baseline ||
            !_stopped || plan.Target.StoppedLease != _lease || File.Exists(TargetPath(plan)) || Directory.Exists(TargetPath(plan)))
            throw new IOException("Target identity, baseline, absence, or exclusive stopped lease changed.");
    }

    private bool AnchorsMatch() => LinuxIdentity.Read(Root).SameObject(_anchor) && LinuxIdentity.Read(StageRoot).SameObject(_stagingAnchor) && LinuxIdentity.Read(TargetsRoot).SameObject(_targetAnchor);

    private static bool TreeMatches(Entry entry)
    {
        if (!Directory.Exists(entry.Path) || !LinuxIdentity.Read(entry.Path).SameObject(entry.RootIdentity)) return false;
        var found = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(string Path, string Relative)>();
        pending.Push((entry.Path, ""));
        while (pending.TryPop(out var folder))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder.Path))
            {
                var relative = folder.Relative.Length == 0 ? Path.GetFileName(path) : folder.Relative + "/" + Path.GetFileName(path);
                if (!entry.Files.TryGetValue(relative, out var expected)) return false;
                var identity = LinuxIdentity.Read(path);
                if (!identity.SameObject(expected.Identity) || !found.Add(relative)) return false;
                if (expected.Hash is null)
                {
                    if (!identity.IsDirectory) return false;
                    pending.Push((path, relative));
                }
                else if (!identity.IsRegular || identity.LinkCount != 1 || new FileInfo(path).Length != expected.SizeBytes || Hash(File.ReadAllBytes(path)) != expected.Hash) return false;
            }
        }
        return found.Count == entry.Files.Count;
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    // Fixture teardown owns the entire temporary sandbox, including foreign *synthetic* entries inserted by tests.
    // This is NOT transaction recovery, and never points outside Root.
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }

    private sealed class Entry(TransactionReceipt receipt, string path)
    {
        public TransactionReceipt Receipt { get; } = receipt;
        public string Path { get; } = path;
        public LinuxIdentity RootIdentity { get; set; }
        public Dictionary<string, OwnedFile> Files { get; } = new(StringComparer.Ordinal);
        public bool IdentityCaptured { get; set; }
        public bool Published { get; set; }
        public bool Removed { get; set; }
    }
    private sealed record OwnedFile(LinuxIdentity Identity, string? Hash, long SizeBytes = 0);
}

internal readonly record struct LinuxIdentity(uint DeviceMajor, uint DeviceMinor, ulong Inode, long BirthSeconds, uint BirthNanos, ushort Mode, uint LinkCount)
{
    public bool SameObject(LinuxIdentity other) => DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor &&
        Inode == other.Inode && BirthSeconds == other.BirthSeconds && BirthNanos == other.BirthNanos && Mode == other.Mode;
    public bool IsDirectory => (Mode & 0xf000) == 0x4000;
    public bool IsRegular => (Mode & 0xf000) == 0x8000;
    public string Token => $"a-{DeviceMajor}-{DeviceMinor}-{Inode}-{BirthSeconds}-{BirthNanos}";
    public static LinuxIdentity Read(string path)
    {
        if (Statx(-100, path, 0x100, 0xfff, out var stat) != 0) throw new IOException("statx failed.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        return new(stat.DeviceMajor, stat.DeviceMinor, stat.Inode, stat.BirthSeconds, stat.BirthNanos, stat.Mode, stat.LinkCount);
    }
    public static bool MoveNoReplace(string source, string destination)
    {
        if (RenameAt2(-100, source, -100, destination, 1) == 0) return true;
        var error = Marshal.GetLastPInvokeError();
        if (error == 17) return false; // EEXIST, including an empty directory or symlink.
        throw new IOException("renameat2(RENAME_NOREPLACE) failed.", new System.ComponentModel.Win32Exception(error));
    }
    public static void CreateHardLink(string existing, string alias)
    {
        if (Link(existing, alias) != 0) throw new IOException("link failed.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
    }
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existing, string alias);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int dirfd, string pathname, int flags, uint mask, out StatxData stat);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameAt2(int olddirfd, string oldpath, int newdirfd, string newpath, uint flags);
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxData
    {
        [FieldOffset(16)] public uint LinkCount;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(80)] public long BirthSeconds;
        [FieldOffset(88)] public uint BirthNanos;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
}

public sealed class LinuxTransactionFactAttribute : FactAttribute
{
    public LinuxTransactionFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Isolated test-only Linux filesystem backend; no production Windows backend exists."; }
}
public sealed class LinuxTransactionTheoryAttribute : TheoryAttribute
{
    public LinuxTransactionTheoryAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Isolated test-only Linux filesystem backend; no production Windows backend exists."; }
}
