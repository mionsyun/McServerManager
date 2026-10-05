using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McServerManager.Models.Editions;
using McServerManager.Services.Editions;
using McServerManager.Services.Participants;
using McServerManager.Services.Templates;
using McServerManager.ViewModels;

namespace McServerManager.TemplateTests.Participants;

/// <summary>Actual offline services and view models; no WPF dialogs or gameplay are exercised.</summary>
public sealed class TemplateParticipantExportFlowTests
{
    [Theory]
    [InlineData(AppEdition.Free)]
    [InlineData(AppEdition.Pro)]
    public async Task TemplateSelectionDirectReviewAndNewZipSaveKeepOriginalDeclarations(AppEdition edition)
    {
        var directory = Path.Combine(Path.GetTempPath(), "template-client-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "fixture.maipilot-template.json");
            var bytes = Encoding.UTF8.GetBytes(Template);
            await File.WriteAllBytesAsync(path, bytes);
            var files = new ParticipantExportFileService();
            var inspection = new TemplateInspectionViewModel(new TemplateManifestService());
            using var stream = new MemoryStream(await files.ReadTemplateAsync(path));
            await inspection.InspectAsync(stream);
            Assert.True(inspection.CanReviewParticipantDefinition);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), inspection.OriginalManifestSha256);
            var policy = new EditionPolicy(edition);
            var participant = new ParticipantExportViewModel(new ParticipantClientDefinitionService(),
                new ParticipantListZipService(policy), policy);
            participant.LoadValidatedDefinition(inspection.ClientDefinition!, inspection.OriginalManifestSha256!);
            Assert.False(participant.CanExport);
            Assert.True(participant.IsTemplateDefinition);
            Assert.Equal(inspection.ClientDefinition!.InputSha256, participant.DefinitionSha256);
            Assert.Contains(inspection.OriginalManifestSha256!, participant.Details);
            participant.AcknowledgedLimitations = true;
            var zip = await participant.CreateZipAsync();
            Assert.NotNull(zip);
            var output = await files.SaveNewZipAsync(Path.Combine(directory, "participants.zip"), zip!);
            using var archive = ZipFile.OpenRead(output);
            Assert.Equal(3, archive.Entries.Count);
            using var manifestStream = archive.GetEntry("manifest.json")!.Open();
            using var manifest = await JsonDocument.ParseAsync(manifestStream);
            Assert.Equal(participant.DefinitionSha256, manifest.RootElement.GetProperty("inputSha256").GetString());
            Assert.Empty(manifest.RootElement.GetProperty("mods").EnumerateArray());
            Assert.False(manifest.RootElement.GetProperty("dependencyCompletenessVerified").GetBoolean());
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    private const string Template = """
        { "schemaVersion":"1.0", "templateId":"1e0c5d1b-f521-4681-90d4-a0f6274f9871",
          "revision":1, "name":"合成データの試験", "description":"", "edition":"java",
          "runtime":{"type":"vanilla","minecraftVersion":"1.21.1"}, "settings":{}, "addons":[],
          "clientDefinition":{
            "schemaVersion":"1.0","definitionKind":"client","name":"明示的な空の参加者一覧",
            "runtime":{"minecraftVersion":"1.21.1","loader":"fabric","loaderVersion":"0.16.9","javaVersion":"21.0.2"},
            "mods":[]
          }
        }
        """;
}
