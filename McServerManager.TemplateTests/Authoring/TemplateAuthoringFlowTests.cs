using System.IO.Compression;
using System.Text.Json;
using McServerManager.Models.Editions;
using McServerManager.Services.Authoring;
using McServerManager.Services.AuthoringFiles;
using McServerManager.Services.Editions;
using McServerManager.Services.Participants;
using McServerManager.Services.Templates;
using McServerManager.ViewModels;
using McServerManager.ViewModels.Authoring;

namespace McServerManager.TemplateTests.Authoring;

/// <summary>Real offline services in isolated directories; no native windows or real MODs.</summary>
public sealed class TemplateAuthoringFlowTests
{
    [Fact]
    public async Task ProAuthorsNewDeclarationThenFreeReviewsAndExportsParticipantGuide()
    {
        var root = Path.Combine(Path.GetTempPath(), "authoring-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pro = new EditionPolicy(AppEdition.Pro);
            var parser = new TemplateManifestService();
            var author = new TemplateAuthoringViewModel(new TemplateAuthoringService(pro, parser), pro);
            Assert.True(author.CreateNew());
            author.Name = "架空の構成を使う試験";
            author.ServerRuntime.MinecraftVersion = "1.21.1";
            author.HasClientDefinition = true;
            author.ClientName = "明示的な試験用クライアント";
            author.ClientRuntime.MinecraftVersion = "1.21.1";
            author.ClientRuntime.LoaderVersion = "0.16.9";
            author.ClientRuntime.JavaVersion = "21.0.2";
            Assert.True(author.AddClientMod());
            var mod = author.ClientMods.Single();
            mod.Name = "架空 MOD";
            mod.Version = "1.2.3";
            mod.ProjectId = "AbCd1234";
            mod.VersionId = "EfGh5678";
            Assert.True(author.ValidatePreview());
            Assert.False(author.CanExport);
            author.AcknowledgedLimitations = true;
            var revision = author.ReviewRevision;
            var first = author.CreateExportBytes();
            Assert.NotNull(first);
            var path = Path.Combine(root, "first.maipilot-template.json");
            var files = new TemplateAuthoringFileService(pro);
            await files.SaveNewTemplateAsync(path, first!);
            author.ReportSaved(path, revision);
            Assert.False(author.IsDirty);
            Assert.False(author.CanExport);
            var firstManifest = parser.Parse(first!).Manifest!;
            Assert.Equal(1, firstManifest.Revision);
            Assert.Empty(firstManifest.Addons);

            author.Description = "保存後の明示的な変更";
            Assert.True(author.ValidatePreview());
            author.AcknowledgedLimitations = true;
            var second = author.CreateExportBytes();
            var secondManifest = parser.Parse(second!).Manifest!;
            Assert.Equal(2, secondManifest.Revision);
            Assert.Equal(firstManifest.TemplateId, secondManifest.TemplateId);
            Assert.Equal(first, await File.ReadAllBytesAsync(path));

            var free = new EditionPolicy(AppEdition.Free);
            var inspection = new TemplateInspectionViewModel(parser);
            using var input = new MemoryStream(await new ParticipantExportFileService().ReadTemplateAsync(path));
            await inspection.InspectAsync(input);
            Assert.True(inspection.CanReviewParticipantDefinition);
            var guide = new ParticipantExportViewModel(new ParticipantClientDefinitionService(),
                new ParticipantListZipService(free), free);
            guide.LoadValidatedDefinition(inspection.ClientDefinition!, inspection.OriginalManifestSha256!);
            guide.AcknowledgedLimitations = true;
            var bytes = await guide.CreateZipAsync();
            using var archive = new ZipArchive(new MemoryStream(bytes!), ZipArchiveMode.Read);
            Assert.Equal(new[] { "README.txt", "mods.txt", "manifest.json" }, archive.Entries.Select(entry => entry.FullName));
            using var metadata = archive.GetEntry("manifest.json")!.Open();
            using var json = await JsonDocument.ParseAsync(metadata);
            Assert.False(json.RootElement.GetProperty("providerMetadataVerified").GetBoolean());
            Assert.Single(json.RootElement.GetProperty("mods").EnumerateArray());
        }
        finally { Directory.Delete(root, true); }
    }
}
