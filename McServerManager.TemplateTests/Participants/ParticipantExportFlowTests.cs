using System.IO.Compression;
using System.Text;
using System.Text.Json;
using McServerManager.Models.Editions;
using McServerManager.Services.Editions;
using McServerManager.Services.Participants;
using McServerManager.ViewModels;

namespace McServerManager.TemplateTests.Participants;

/// <summary>Headless real-service flow; this does not drive WPF dialogs or Windows UI.</summary>
public sealed class ParticipantExportFlowTests
{
    [Theory]
    [InlineData(AppEdition.Free)]
    [InlineData(AppEdition.Pro)]
    public async Task ExplicitDefinitionReviewAndSaveProducesOnlyReferenceFiles(AppEdition edition)
    {
        var directory = Path.Combine(Path.GetTempPath(), "participant-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "client.json");
            var output = Path.Combine(directory, "participants.zip");
            await File.WriteAllTextAsync(input, Definition, new UTF8Encoding(false));
            var files = new ParticipantExportFileService();
            var policy = new EditionPolicy(edition);
            var viewModel = new ParticipantExportViewModel(new ParticipantClientDefinitionService(),
                new ParticipantListZipService(policy), policy);
            await viewModel.LoadAsync(await files.ReadDefinitionAsync(input));
            Assert.True(viewModel.HasDefinition);
            Assert.False(viewModel.CanExport);
            Assert.Null(await viewModel.CreateZipAsync());
            viewModel.AcknowledgedLimitations = true;
            var zipBytes = await viewModel.CreateZipAsync();
            Assert.NotNull(zipBytes);
            var revision = viewModel.ReviewRevision;
            var saved = await files.SaveNewZipAsync(output, zipBytes!);
            viewModel.ReportSaved(saved, revision);
            Assert.Contains("保存しました", viewModel.Summary);
            Assert.Equal(zipBytes, await File.ReadAllBytesAsync(output));
            using var archive = ZipFile.OpenRead(output);
            Assert.Equal(new[] { "README.txt", "mods.txt", "manifest.json" }, archive.Entries.Select(x => x.FullName));
            using var manifestStream = archive.GetEntry("manifest.json")!.Open();
            using var manifest = await JsonDocument.ParseAsync(manifestStream);
            Assert.False(manifest.RootElement.GetProperty("providerMetadataVerified").GetBoolean());
            Assert.Equal("explicit-client-definition", manifest.RootElement.GetProperty("source").GetString());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private const string Definition = """
        {"schemaVersion":"1.0","definitionKind":"client","name":"試験用の明示的な構成",
         "runtime":{"minecraftVersion":"1.21.1","loader":"fabric","loaderVersion":"0.16.9","javaVersion":"21.0.2"},
         "mods":[{"provider":"modrinth","projectId":"AbCd1234","versionId":"EfGh5678",
           "name":"架空の試験 MOD","version":"1.2.3","clientSide":"required"}]}
        """;
}
