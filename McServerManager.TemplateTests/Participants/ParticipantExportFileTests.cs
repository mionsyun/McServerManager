using System.IO.Compression;
using System.Text;
using McServerManager.Services.Participants;

namespace McServerManager.TemplateTests.Participants;

public sealed class ParticipantExportFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "maipilot-participant-files-" + Guid.NewGuid().ToString("N"));
    private readonly ParticipantExportFileService _service = new();

    public ParticipantExportFileTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ReadsOnlyTheExplicitJsonFileAndNormalizesItsPath()
    {
        var selected = Path.Combine(_root, "client.JSON");
        var contents = Encoding.UTF8.GetBytes("{\"selected\":true}");
        await File.WriteAllBytesAsync(selected, contents);
        await File.WriteAllTextAsync(Path.Combine(_root, "unselected.json"), "other");

        Assert.Equal(contents, await _service.ReadDefinitionAsync(Path.Combine(_root, "unused", "..", "client.JSON")));
        Assert.Equal(contents, await File.ReadAllBytesAsync(selected));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(ParticipantDefinitionPolicy.MaxDefinitionBytes)]
    public async Task ReadsEmptyAndBoundarySizedFilesWithoutParsing(int length)
    {
        var path = Path.Combine(_root, "definition.json");
        var contents = Enumerable.Repeat((byte)' ', length).ToArray();
        await File.WriteAllBytesAsync(path, contents);
        Assert.Equal(contents, await _service.ReadDefinitionAsync(path));
    }

    [Fact]
    public async Task RejectsOversizedDefinitionBeforeReturningBytes()
    {
        var path = Path.Combine(_root, "oversized.json");
        await File.WriteAllBytesAsync(path, new byte[ParticipantDefinitionPolicy.MaxDefinitionBytes + 1]);
        await Assert.ThrowsAsync<IOException>(() => _service.ReadDefinitionAsync(path));
        Assert.Equal(ParticipantDefinitionPolicy.MaxDefinitionBytes + 1, new FileInfo(path).Length);
    }

    [Fact]
    public async Task CommitsCompleteArchiveToNewNormalizedPathAndRemovesStage()
    {
        var contents = CreateZip();
        var path = Path.Combine(_root, "unused", "..", "participants.ZIP");
        var operations = new TestOperations
        {
            BeforeMove = (stage, destination) =>
            {
                Assert.Equal(_root, Path.GetDirectoryName(stage));
                Assert.False(File.Exists(destination));
                Assert.Equal(contents, File.ReadAllBytes(stage));
            }
        };
        var service = new ParticipantExportFileService(operations);

        var result = await service.SaveNewZipAsync(path, contents);

        Assert.Equal(Path.Combine(_root, "participants.ZIP"), result);
        Assert.Equal(contents, await File.ReadAllBytesAsync(result));
        Assert.Equal(new[] { result }, Directory.GetFileSystemEntries(_root));
        using var archive = new ZipArchive(File.OpenRead(result));
        Assert.Equal("README.txt", Assert.Single(archive.Entries).FullName);
        Assert.Equal(1, operations.Moves);
    }

    [Fact]
    public async Task SnapshotsCallerBytesBeforeAwaitingWrites()
    {
        var contents = CreateZip();
        var expected = contents.ToArray();
        var operations = new TestOperations { BeforeWrite = () => Array.Fill(contents, (byte)0) };
        var result = await new ParticipantExportFileService(operations)
            .SaveNewZipAsync(Path.Combine(_root, "snapshot.zip"), contents);
        Assert.Equal(expected, await File.ReadAllBytesAsync(result));
    }

    [Fact]
    public async Task AllowsExactArchiveByteLimit()
    {
        var bytes = new byte[ParticipantDefinitionPolicy.MaxZipBytes];
        var result = await _service.SaveNewZipAsync(Path.Combine(_root, "boundary.zip"), bytes);
        Assert.Equal(bytes.Length, new FileInfo(result).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ParticipantDefinitionPolicy.MaxZipBytes + 1)]
    public async Task RejectsEmptyOrOversizedArchiveWithoutStaging(int length)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveNewZipAsync(
            Path.Combine(_root, "rejected.zip"), new byte[length]));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("archive.jar")]
    [InlineData("archive.zip.txt")]
    [InlineData("archive")]
    public async Task RejectsNonZipDestinations(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveNewZipAsync(Path.Combine(_root, name), CreateZip()));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task RejectsNonJsonInputsAndBlankPaths()
    {
        var text = Path.Combine(_root, "input.txt");
        await File.WriteAllTextAsync(text, "{}");
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ReadDefinitionAsync(text));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ReadDefinitionAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveNewZipAsync(" ", CreateZip()));
    }

    [Fact]
    public async Task ExistingFileIsNeverOverwritten()
    {
        var destination = Path.Combine(_root, "existing.zip");
        var sentinel = Encoding.UTF8.GetBytes("owned by the user");
        await File.WriteAllBytesAsync(destination, sentinel);
        await Assert.ThrowsAsync<IOException>(() => _service.SaveNewZipAsync(destination, CreateZip()));
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(destination));
        Assert.Equal(new[] { destination }, Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task RejectsDirectoryTargetsAndDoesNotCreateMissingParents()
    {
        var directory = Path.Combine(_root, "directory.zip");
        Directory.CreateDirectory(directory);
        await Assert.ThrowsAsync<IOException>(() => _service.SaveNewZipAsync(directory, CreateZip()));
        var jsonDirectory = Path.Combine(_root, "directory.json");
        Directory.CreateDirectory(jsonDirectory);
        await Assert.ThrowsAsync<IOException>(() => _service.ReadDefinitionAsync(jsonDirectory));
        await Assert.ThrowsAnyAsync<IOException>(() => _service.SaveNewZipAsync(Path.Combine(_root, "missing", "new.zip"), CreateZip()));
        Assert.False(Directory.Exists(Path.Combine(_root, "missing")));
    }

    [Fact]
    public async Task PreCancelledReadAndSaveDoNotTouchFiles()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ReadDefinitionAsync(Path.Combine(_root, "missing.json"), source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.SaveNewZipAsync(Path.Combine(_root, "new.zip"), CreateZip(), source.Token));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task CancellationAfterStageWriteRemovesOnlyOwnedStageAndKeepsDestinationAbsent()
    {
        using var source = new CancellationTokenSource();
        var destination = Path.Combine(_root, "cancelled.zip");
        var foreignStage = Path.Combine(_root, ".maipilot-participant-foreign.tmp");
        await File.WriteAllTextAsync(foreignStage, "keep");
        var operations = new TestOperations { AfterWrite = source.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ParticipantExportFileService(operations)
            .SaveNewZipAsync(destination, CreateZip(), source.Token));

        Assert.False(File.Exists(destination));
        Assert.Equal(new[] { foreignStage }, Directory.GetFileSystemEntries(_root));
        Assert.Equal("keep", await File.ReadAllTextAsync(foreignStage));
        Assert.Equal(0, operations.Moves);
    }

    [Fact]
    public async Task CancellationAfterCommitStillReturnsSavedPath()
    {
        using var source = new CancellationTokenSource();
        var destination = Path.Combine(_root, "committed.zip");
        var operations = new TestOperations { AfterMove = source.Cancel };
        var result = await new ParticipantExportFileService(operations).SaveNewZipAsync(destination, CreateZip(), source.Token);
        Assert.True(source.IsCancellationRequested);
        Assert.Equal(destination, result);
        Assert.True(File.Exists(destination));
        Assert.Equal(new[] { destination }, Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task DestinationAppearingAfterStagingIsPreservedAndStageRemoved()
    {
        var destination = Path.Combine(_root, "conflict.zip");
        var operations = new TestOperations { AfterWrite = () => File.WriteAllText(destination, "winner") };
        await Assert.ThrowsAsync<IOException>(() => new ParticipantExportFileService(operations)
            .SaveNewZipAsync(destination, CreateZip()));
        Assert.Equal("winner", await File.ReadAllTextAsync(destination));
        Assert.Equal(new[] { destination }, Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task NativeNoOverwriteAlsoProtectsDestinationAppearingAtCommit()
    {
        var destination = Path.Combine(_root, "race.zip");
        var operations = new TestOperations { BeforeMove = (_, target) => File.WriteAllText(target, "winner") };
        await Assert.ThrowsAsync<IOException>(() => new ParticipantExportFileService(operations)
            .SaveNewZipAsync(destination, CreateZip()));
        Assert.Equal("winner", await File.ReadAllTextAsync(destination));
        Assert.Equal(new[] { destination }, Directory.GetFileSystemEntries(_root));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteAndMoveFailuresPropagateAndCleanStage(bool failWrite)
    {
        var failure = new IOException("synthetic I/O failure");
        var operations = new TestOperations
        {
            AfterWrite = failWrite ? () => throw failure : null,
            BeforeMove = failWrite ? null : (_, _) => throw failure
        };
        var actual = await Assert.ThrowsAsync<IOException>(() => new ParticipantExportFileService(operations)
            .SaveNewZipAsync(Path.Combine(_root, "failed.zip"), CreateZip()));
        Assert.Same(failure, actual);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task CleanupFailureIsReportedWithoutDeletingDestination()
    {
        using var source = new CancellationTokenSource();
        var failure = new IOException("synthetic cleanup failure");
        var operations = new TestOperations { AfterWrite = source.Cancel, DeleteFailure = failure };
        var destination = Path.Combine(_root, "failed.zip");
        var actual = await Assert.ThrowsAsync<ParticipantExportCleanupException>(() => new ParticipantExportFileService(operations)
            .SaveNewZipAsync(destination, CreateZip(), source.Token));
        var failures = Assert.IsType<AggregateException>(actual.InnerException).InnerExceptions;
        Assert.IsAssignableFrom<OperationCanceledException>(failures[0]);
        Assert.Same(failure, failures[1]);
        Assert.False(File.Exists(destination));
        Assert.Equal(actual.StagePath, Assert.Single(Directory.GetFiles(_root)));
        Assert.EndsWith(".tmp", actual.StagePath);
    }

    [Fact]
    public async Task RejectsInputLinksAndLinkedAncestors()
    {
        var actual = Path.Combine(_root, "actual.json");
        await File.WriteAllTextAsync(actual, "{}");
        var link = Path.Combine(_root, "link.json");
        File.CreateSymbolicLink(link, actual);
        await Assert.ThrowsAsync<IOException>(() => _service.ReadDefinitionAsync(link));
        var directory = Path.Combine(_root, "linked");
        Directory.CreateSymbolicLink(directory, _root);
        await Assert.ThrowsAsync<IOException>(() => _service.ReadDefinitionAsync(Path.Combine(directory, "actual.json")));
        await Assert.ThrowsAsync<IOException>(() => _service.SaveNewZipAsync(Path.Combine(directory, "new.zip"), CreateZip()));
        Assert.False(File.Exists(Path.Combine(_root, "new.zip")));
        Assert.Equal("{}", await File.ReadAllTextAsync(actual));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsExistingAndDanglingDestinationLinks(bool targetExists)
    {
        var target = Path.Combine(_root, "target.zip");
        if (targetExists) await File.WriteAllTextAsync(target, "keep");
        var link = Path.Combine(_root, "link.zip");
        File.CreateSymbolicLink(link, target);
        await Assert.ThrowsAsync<IOException>(() => _service.SaveNewZipAsync(link, CreateZip()));
        Assert.Equal(target, new FileInfo(link).LinkTarget);
        Assert.Equal(targetExists, File.Exists(target));
        if (targetExists) Assert.Equal("keep", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    private static byte[] CreateZip()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry("README.txt").Open()))
            writer.Write("Synthetic participant reference list");
        return buffer.ToArray();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class TestOperations : IParticipantExportFileOperations
    {
        public Action? BeforeWrite { get; init; }
        public Action? AfterWrite { get; init; }
        public Action<string, string>? BeforeMove { get; init; }
        public Action? AfterMove { get; init; }
        public Exception? DeleteFailure { get; init; }
        public int Moves { get; private set; }

        public async Task WriteAsync(Stream stage, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            BeforeWrite?.Invoke();
            await stage.WriteAsync(bytes, cancellationToken);
            AfterWrite?.Invoke();
        }

        public void MoveNew(string stagePath, string destinationPath)
        {
            Moves++;
            BeforeMove?.Invoke(stagePath, destinationPath);
            File.Move(stagePath, destinationPath, overwrite: false);
            AfterMove?.Invoke();
        }

        public void DeleteStage(string stagePath)
        {
            if (DeleteFailure is not null) throw DeleteFailure;
            File.Delete(stagePath);
        }
    }
}
