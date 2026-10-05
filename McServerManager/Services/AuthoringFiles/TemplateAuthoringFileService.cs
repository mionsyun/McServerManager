using System.IO;
using System.Threading;
using McServerManager.Models.Editions;
using McServerManager.Services.Editions;
using McServerManager.Services.Participants;
using McServerManager.Services.Templates;

namespace McServerManager.Services.AuthoringFiles;

/// <summary>Pro declaration-file I/O only. No server capture, artifact download or installation.</summary>
public sealed class TemplateAuthoringFileService : ITemplateAuthoringFileService
{
    private readonly IEditionPolicy _edition;
    private readonly ParticipantExportFileService _files;

    public TemplateAuthoringFileService(IEditionPolicy edition) : this(edition, new ParticipantExportFileService()) { }

    internal TemplateAuthoringFileService(IEditionPolicy edition, ParticipantExportFileService files)
    {
        _edition = edition ?? throw new ArgumentNullException(nameof(edition));
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    public async Task<byte[]> ReadTemplateAsync(string path, CancellationToken cancellationToken = default)
    {
        Demand(EditionCapability.TemplateEdit);
        var bytes = await _files.ReadTemplateAsync(path, cancellationToken).ConfigureAwait(false);
        Demand(EditionCapability.TemplateEdit);
        return bytes;
    }

    public Task<string> SaveNewTemplateAsync(string path, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        Demand(EditionCapability.TemplateExport);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.EndsWith(".maipilot-template.json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(".maipilot-template.json の新しいファイル名を選んでください。", nameof(path));
        if (bytes.Length > TemplatePolicy.MaxManifestBytes)
            throw new InvalidDataException("テンプレートは 1 MiB 以下にしてください。");
        // Freeze the bytes before parsing and publication, so validation never covers an
        // older view of caller-owned mutable memory than the file written on disk.
        var snapshot = bytes.ToArray();
        var result = new TemplateManifestService().Parse(snapshot);
        if (!result.IsValid)
            throw new InvalidDataException("入力形式の確認に失敗したため、テンプレートを保存できません。");
        return _files.SaveNewTemplateAsync(path, snapshot,
            () => Demand(EditionCapability.TemplateExport), cancellationToken);
    }

    private void Demand(EditionCapability capability)
    {
        if (!_edition.Allows(capability))
            throw new InvalidOperationException("テンプレートの作成・編集・書き出しは Pro の機能です。");
    }
}
