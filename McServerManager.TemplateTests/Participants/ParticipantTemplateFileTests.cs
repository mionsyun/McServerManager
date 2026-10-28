using McServerManager.Services.Participants;
using McServerManager.Services.Templates;

namespace McServerManager.TemplateTests.Participants;

public sealed class ParticipantTemplateFileTests
{
    [Theory]
    [InlineData(256 * 1024 + 1)]
    [InlineData(1024 * 1024)]
    public async Task TemplateReadAcceptsUpToItsOwnLimitWithoutRelaxingManualDefinitionLimit(int length)
    {
        var directory = Path.Combine(Path.GetTempPath(), "participant-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "test.maipilot-template.json");
            var expected = Enumerable.Repeat((byte)' ', length).ToArray();
            await File.WriteAllBytesAsync(path, expected);
            var service = new ParticipantExportFileService();
            Assert.Equal(expected, await service.ReadTemplateAsync(path));
            await Assert.ThrowsAsync<IOException>(() => service.ReadDefinitionAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OversizedTemplateAndObservedLinksAreRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "participant-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "large.maipilot-template.json");
            await File.WriteAllBytesAsync(path, new byte[TemplatePolicy.MaxManifestBytes + 1]);
            var service = new ParticipantExportFileService();
            await Assert.ThrowsAsync<IOException>(() => service.ReadTemplateAsync(path));
            if (OperatingSystem.IsLinux())
            {
                var linked = Path.Combine(directory, "link.maipilot-template.json");
                File.CreateSymbolicLink(linked, path);
                await Assert.ThrowsAsync<IOException>(() => service.ReadTemplateAsync(linked));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("client.json")]
    [InlineData("archive.zip")]
    [InlineData("")]
    public async Task WrongTemplateFileNamesFailBeforeReading(string path) =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new ParticipantExportFileService().ReadTemplateAsync(path));

    [Fact]
    public async Task PreCanceledTemplateReadDoesNotOpenMissingFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ParticipantExportFileService().ReadTemplateAsync("absent.maipilot-template.json", cancellation.Token));
    }
}
