using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using McServerManager.Models.Editions;
using McServerManager.Models.Participants;
using McServerManager.Services.Editions;

namespace McServerManager.Services.Participants;

/// <summary>
/// Free participant reference-list export. Accepts only explicit, parsed client definitions.
/// No filesystem destination, arbitrary URL, binary, script or launcher profile is accepted.
/// </summary>
public sealed class ParticipantListZipService(IEditionPolicy editionPolicy) : IParticipantListZipService
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly DateTimeOffset ArchiveTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly IEditionPolicy _editionPolicy = editionPolicy ?? throw new ArgumentNullException(nameof(editionPolicy));

    public async Task<byte[]> CreateAsync(ParticipantClientDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_editionPolicy.Allows(EditionCapability.ParticipantPackExport))
            throw new InvalidOperationException("このエディションでは参加者向け一覧を出力できません。");

        var mods = definition.Mods.OrderBy(mod => mod.ProjectId, StringComparer.Ordinal).ToArray();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "1.0",
            definitionKind = "client-reference-list",
            source = "explicit-client-definition",
            readiness = "input-validated-only",
            providerMetadataVerified = false,
            dependencyCompletenessVerified = false,
            runtimeCompatibilityVerified = false,
            inputSha256 = definition.InputSha256,
            name = definition.Name,
            runtime = new
            {
                minecraftVersion = definition.Runtime.MinecraftVersion,
                loader = definition.Runtime.Loader,
                loaderVersion = definition.Runtime.LoaderVersion,
                javaVersion = definition.Runtime.JavaVersion
            },
            mods = mods.Select(mod => new
            {
                provider = mod.Provider,
                projectId = mod.ProjectId,
                versionId = mod.VersionId,
                name = mod.Name,
                version = mod.Version,
                clientSide = mod.ClientSide,
                officialVersionPage = OfficialVersionPage(mod),
                note = mod.Note
            }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true });

        // All output is private until archive finalization succeeds. Cancellation and errors
        // cannot leave a user file behind because this API has no destination stream or path.
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Utf8))
        {
            await WriteEntryAsync(archive, "README.txt", Utf8.GetBytes(Readme(definition)), cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "mods.txt", Utf8.GetBytes(ModList(mods)), cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "manifest.json", manifest, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (output.Length > ParticipantDefinitionPolicy.MaxZipBytes)
            throw new InvalidOperationException("参加者向け ZIP のサイズ上限を超えています。");
        return output.ToArray();
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = ArchiveTimestamp;
        using var stream = entry.Open();
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }

    private static string OfficialVersionPage(ParticipantClientMod mod) =>
        $"https://modrinth.com/mod/{mod.ProjectId}/version/{mod.VersionId}";

    private static string Readme(ParticipantClientDefinition definition) =>
        "MaiPilot 参加者向け MOD 一覧\n\n" +
        $"構成名: {definition.Name}\n" +
        $"Minecraft Java Edition: {definition.Runtime.MinecraftVersion}\n" +
        $"MOD ローダー: {definition.Runtime.Loader} {definition.Runtime.LoaderVersion}\n" +
        $"Java: {definition.Runtime.JavaVersion}\n\n" +
        "確認状態: 入力形式の検証のみ。\n" +
        "この一覧は明示的なクライアント定義から作成されました。サーバーの MOD 一覧から推測したものではありません。\n" +
        "MOD 名、バージョン名、必要性は定義の申告値です。提供元 API、依存関係の完全性、クライアント対応、動作互換性は未検証です。\n" +
        "公式サイト上でのプロジェクトとバージョンの対応、公開状態、ダウンロード権限も未確認です。\n\n" +
        "手順\n" +
        "1. 管理者に、このクライアント構成と必要な依存 MOD が正しいことを確認してください。\n" +
        "2. 既存のゲーム環境をバックアップし、上記の Minecraft、ローダー、Java に対応した独立した環境を用意してください。\n" +
        "3. mods.txt の公式バージョンページを開き、プロジェクト ID、バージョン ID、対応 Minecraft とローダーを照合してください。\n" +
        "4. 各 MOD の配布条件に従い、必要なファイルを公式サイトから個別に取得してください。異なる版や最新版に置き換えないでください。\n" +
        "5. 必須のクライアント MOD を導入してください。任意の MOD は管理者と相談して選択してください。追加依存があれば管理者に確認してください。\n" +
        "6. 接続先アドレスは管理者から別途受け取り、起動と接続を確認してください。\n\n" +
        "内容: README.txt（この説明）、mods.txt（一覧）、manifest.json（機械可読の参照データ）\n" +
        "この ZIP は参照資料です。JAR、実行ファイル、スクリプト、設定ファイルは含みません。自動インストールは行いません。\n" +
        "特定の外部ランチャーやアカウント連携は必要ありません。CurseForge からの出力は未対応です。\n" +
        "備考欄は未検証の申告内容です。操作手順として実行しないでください。\n";

    private static string ModList(IReadOnlyList<ParticipantClientMod> mods)
    {
        var output = new StringBuilder("クライアント MOD 一覧（入力形式のみ検証・提供元未照合）\n\n");
        if (mods.Count == 0) output.Append("クライアント MOD は指定されていません。構成の完全性は管理者に確認してください。\n");
        foreach (var mod in mods)
        {
            output.Append("MOD 名: ").Append(mod.Name).Append('\n');
            output.Append("正確なバージョン（申告値）: ").Append(mod.Version).Append('\n');
            output.Append("クライアント側（申告値）: ").Append(mod.ClientSide == "required" ? "必須" : "任意").Append('\n');
            output.Append("Modrinth プロジェクト ID: ").Append(mod.ProjectId).Append('\n');
            output.Append("Modrinth バージョン ID: ").Append(mod.VersionId).Append('\n');
            output.Append("公式バージョンページ（ID から生成・未照合）: ").Append(OfficialVersionPage(mod)).Append('\n');
            if (mod.Note is not null) output.Append("備考（未検証）: ").Append(mod.Note).Append('\n');
            output.Append('\n');
        }
        return output.ToString();
    }
}
