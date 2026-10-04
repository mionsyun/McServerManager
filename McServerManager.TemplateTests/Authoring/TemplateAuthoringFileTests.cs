using System.Text;
using McServerManager.Models.Editions;
using McServerManager.Services.AuthoringFiles;
using McServerManager.Services.Editions;
using McServerManager.Services.Participants;

namespace McServerManager.TemplateTests.Authoring;

public sealed class TemplateAuthoringFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "authoring-files-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _valid = Encoding.UTF8.GetBytes("""
        {"schemaVersion":"1.0","templateId":"e4be37d4-4847-45f8-99b7-411b469f08d5","revision":1,
         "name":"試験用の宣言","description":"","edition":"java",
         "runtime":{"type":"vanilla","minecraftVersion":"1.21.1"},"settings":{},"addons":[]}
        """);
    public TemplateAuthoringFileTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private string Target => Path.Combine(_root, "new.maipilot-template.json");

    [Fact]
    public async Task FreeDirectReadAndSaveAreDeniedBeforeAnyFileWork()
    {
        var files = new TemplateAuthoringFileService(new EditionPolicy(AppEdition.Free));
        await Assert.ThrowsAsync<InvalidOperationException>(() => files.ReadTemplateAsync(Target));
        await Assert.ThrowsAsync<InvalidOperationException>(() => files.SaveNewTemplateAsync(Target, _valid));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ProPublishesExactValidatedBytesAndReadsThemWithoutOverwrite()
    {
        var files = new TemplateAuthoringFileService(new EditionPolicy(AppEdition.Pro));
        Assert.Equal(Target, await files.SaveNewTemplateAsync(Target, _valid));
        Assert.Equal(_valid, await files.ReadTemplateAsync(Target));
        await Assert.ThrowsAsync<IOException>(() => files.SaveNewTemplateAsync(Target, _valid));
        Assert.Equal(_valid, await File.ReadAllBytesAsync(Target));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"password\":\"synthetic\"}")]
    public async Task InvalidDeclarationsCannotCreateFiles(string json)
    {
        var files = new TemplateAuthoringFileService(new EditionPolicy(AppEdition.Pro));
        await Assert.ThrowsAsync<InvalidDataException>(() => files.SaveNewTemplateAsync(Target, Encoding.UTF8.GetBytes(json)));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("output.json")]
    [InlineData("output.zip")]
    [InlineData("output.maipilot-template.json.exe")]
    public async Task WrongExtensionsAreRejected(string name)
    {
        var files = new TemplateAuthoringFileService(new EditionPolicy(AppEdition.Pro));
        await Assert.ThrowsAsync<ArgumentException>(() => files.SaveNewTemplateAsync(Path.Combine(_root, name), _valid));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task TooLargeAndPreCanceledWritesNeverStage()
    {
        var files = new TemplateAuthoringFileService(new EditionPolicy(AppEdition.Pro));
        await Assert.ThrowsAsync<InvalidDataException>(() => files.SaveNewTemplateAsync(Target, new byte[1024 * 1024 + 1]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.SaveNewTemplateAsync(Target, _valid, cancellation.Token));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task AuthorityRevokedDuringStagingPreventsCommitAndCleansOwnedFile()
    {
        var policy = new MutablePolicy();
        var operations = new Operations { AfterWrite = () => policy.Allowed = false };
        var files = new TemplateAuthoringFileService(policy, new ParticipantExportFileService(operations));
        await Assert.ThrowsAsync<InvalidOperationException>(() => files.SaveNewTemplateAsync(Target, _valid));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task MutableCallerBytesCannotChangeValidatedPublishedSnapshot()
    {
        var original = _valid.ToArray();
        var operations = new Operations { AfterWrite = () => Array.Fill(_valid, (byte)'!') };
        var files = new TemplateAuthoringFileService(new EditionPolicy(AppEdition.Pro), new ParticipantExportFileService(operations));
        await files.SaveNewTemplateAsync(Target, _valid);
        Assert.Equal(original, await File.ReadAllBytesAsync(Target));
    }

    [Fact]
    public async Task CancellationAfterCommitStillReturnsSavedPath()
    {
        using var cancellation = new CancellationTokenSource();
        var operations = new Operations { AfterMove = cancellation.Cancel };
        var files = new TemplateAuthoringFileService(new EditionPolicy(AppEdition.Pro), new ParticipantExportFileService(operations));
        Assert.Equal(Target, await files.SaveNewTemplateAsync(Target, _valid, cancellation.Token));
        Assert.Equal(_valid, await File.ReadAllBytesAsync(Target));
    }

    private sealed class MutablePolicy : IEditionPolicy
    {
        public bool Allowed = true;
        public AppEdition Edition => AppEdition.Pro;
        public string DisplayName => "Test";
        public bool Allows(EditionCapability capability) => Allowed;
    }

    private sealed class Operations : IParticipantExportFileOperations
    {
        public Action? AfterWrite;
        public Action? AfterMove;
        public async Task WriteAsync(Stream stage, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            await stage.WriteAsync(bytes, cancellationToken);
            AfterWrite?.Invoke();
        }
        public void MoveNew(string stagePath, string destinationPath)
        {
            File.Move(stagePath, destinationPath, false);
            AfterMove?.Invoke();
        }
        public void DeleteStage(string stagePath) => File.Delete(stagePath);
    }
}
