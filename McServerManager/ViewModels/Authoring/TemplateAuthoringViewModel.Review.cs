using System.Text;
using McServerManager.Models.Authoring;
using McServerManager.Models.Templates;

namespace McServerManager.ViewModels.Authoring;

public sealed partial class TemplateAuthoringViewModel
{
    public bool ValidatePreview()
    {
        if (!CanValidate) return DenyEdition();
        InvalidateReview();
        var draft = BuildDraft(out var error);
        if (draft is null) { Summary = error!; return false; }
        try
        {
            var result = _service.PrepareExport(_session!, draft);
            if (!result.IsValid || result.PreparedExport is null) return ReportIssues(result.Issues);
            _prepared = result.PreparedExport;
            _isReviewing = true;
            Details = DescribeReview(draft, _prepared);
            Summary = "2. 入力形式のみ確認できました。内容と未検証の注意事項を確認してからチェックを入れてください。";
            NotifyActions();
            return true;
        }
        catch (InvalidOperationException) { return DenyEdition(); }
    }

    public byte[]? CreateExportBytes()
    {
        if (!CanExport)
        {
            if (!CanValidate) DenyEdition();
            else Summary = "内容を確認し、未検証の注意事項にチェックを入れてから書き出してください。";
            return null;
        }
        var draft = BuildDraft(out var error);
        if (draft is null) { InvalidateReview(); Summary = error!; return null; }
        try
        {
            var bytes = _service.Export(_session!, draft, _prepared!);
            _exportSnapshot = bytes.ToArray();
            _hasExportBytes = true;
            Summary = "テンプレートの書き出しデータを作成しました。ファイルへの保存はまだ完了していません。";
            return bytes;
        }
        catch (InvalidOperationException)
        {
            InvalidateReview();
            Summary = "現在の内容では書き出せません。内容とエディションを確認し、もう一度確認画面に進んでください。";
            return null;
        }
    }

    public bool IsCurrentExport(long revision) => revision == _revision && CanExport && _hasExportBytes && _exportSnapshot is not null;

    public void ReportSaved(string fileName, long revision)
    {
        if (revision != _revision || !_hasExportBytes || _exportSnapshot is null) return;
        var savedBytes = _exportSnapshot!;
        InvalidateReview();
        try
        {
            // Rebase only after a confirmed disk commit: later changes must receive the next revision.
            var reopened = _service.OpenForEdit(savedBytes);
            if (reopened.IsValid && reopened.Session is not null && reopened.Draft is not null)
            {
                ApplyDraft(reopened.Session, reopened.Draft, FileNameOnly(fileName));
                Summary = $"保存しました: {fileName}。入力形式のみ確認済みです。続けて編集する場合は次の版として保存します。";
                return;
            }
        }
        catch (InvalidOperationException) { }
        // The file really was saved, even if a revision limit or changed capability prevents rebasing.
        _editingCompleted = true;
        IsDirty = false;
        Summary = $"保存しました: {fileName}。続きの編集はできません。新規作成または別のテンプレートを選んでください。";
        NotifyActions();
    }

    public void ReportSaveCanceled(long revision)
    {
        if (revision == _revision && HasDraft) Summary = "保存を取り消しました。未保存の入力内容は残っています。";
    }
    public void ReportSaveFailure(long revision)
    {
        if (revision == _revision && HasDraft) Summary = "ファイルを保存できませんでした。未保存の入力内容は残っています。保存先を確認してください。";
    }

    private TemplateAuthoringDraft? BuildDraft(out string? error)
    {
        var settings = Settings.ToDraft(out error);
        if (settings is null) return null;
        return new TemplateAuthoringDraft
        {
            Name = Name, Description = Description, Runtime = ServerRuntime.ToDraft(), Settings = settings,
            ClientDefinition = !HasClientDefinition ? null : new TemplateAuthoringClientDraft
            {
                Name = ClientName, MinecraftVersion = ClientRuntime.MinecraftVersion, Loader = ClientRuntime.Loader,
                LoaderVersion = ClientRuntime.LoaderVersion, JavaVersion = ClientRuntime.JavaVersion,
                Mods = ClientMods.Select(row => row.ToDraft()).ToArray()
            }
        };
    }

    private bool ReportIssues(IReadOnlyList<TemplateValidationIssue> issues)
    {
        Summary = "入力形式を確認できません。次の項目を直してください。入力内容は残っています。";
        Details = string.Join("\n", issues.Select(issue => $"{DescribeIssue(issue.Code)} [{issue.Code}] {issue.Path}"));
        return false;
    }

    private string DescribeReview(TemplateAuthoringDraft draft, TemplateAuthoringPreparedExport prepared)
    {
        var text = new StringBuilder();
        text.AppendLine("確認範囲: 入力形式のみ。以下はすべて作成者の申告値です。");
        text.AppendLine("取得元・ファイルの実体・依存 MOD・クライアント対応・動作互換性は未検証です。");
        text.AppendLine("実行中のサーバーから取得・推測していません。サーバー適用・自動インストールはしません。\n");
        if (OriginalManifestSha256 is not null)
        {
            text.AppendLine($"編集元ファイル: {SourceFileName}");
            text.AppendLine($"編集元 SHA-256: {OriginalManifestSha256}");
        }
        else text.AppendLine("入力元: フォームで新規作成");
        text.AppendLine($"新しい出力 SHA-256（入力形式のみ確認）: {prepared.ManifestSha256}");
        text.AppendLine($"テンプレート ID: {prepared.TemplateId:D} / 出力の版: {prepared.Revision}");
        text.AppendLine($"出力サイズ: {prepared.ByteCount} バイト\n");
        text.AppendLine($"名前: {draft.Name}\n説明: {draft.Description}");
        text.AppendLine($"サーバー: {draft.Runtime.Type} / Minecraft {draft.Runtime.MinecraftVersion}");
        text.AppendLine($"固定ビルド: {Display(draft.Runtime.Build)} / Loader: {Display(draft.Runtime.LoaderVersion)} / Installer: {Display(draft.Runtime.InstallerVersion)}");
        text.AppendLine($"難易度: {Display(draft.Settings.Difficulty)} / モード: {Display(draft.Settings.Gamemode)}");
        text.AppendLine($"最大人数: {draft.Settings.MaxPlayers?.ToString() ?? "指定なし"} / 描画距離: {draft.Settings.ViewDistance?.ToString() ?? "指定なし"}");
        text.AppendLine($"シミュレーション距離: {draft.Settings.SimulationDistance?.ToString() ?? "指定なし"} / スポーン保護: {draft.Settings.SpawnProtection?.ToString() ?? "指定なし"}");
        text.AppendLine($"PvP: {(draft.Settings.Pvp.HasValue ? draft.Settings.Pvp.Value ? "有効" : "無効" : "指定なし")}\n");
        text.AppendLine(ServerAddonsSummary);
        var client = draft.ClientDefinition;
        if (client is null)
        {
            text.AppendLine("\n参加者用の構成: 未指定。このテンプレートから参加者用の案内は作れません。");
            return text.ToString();
        }
        text.AppendLine($"\n参加者用の構成（サーバーとは別の明示的な申告）: {client.Name}");
        text.AppendLine($"Minecraft {client.MinecraftVersion} / {client.Loader} {client.LoaderVersion} / Java {client.JavaVersion}");
        text.AppendLine($"新しい参加者用定義 SHA-256: {prepared.ClientDefinitionSha256}");
        text.AppendLine("サーバーの MOD 一覧から参加者用 MOD を推測していません。");
        if (client.Mods.Count == 0)
            text.AppendLine("注意: 参加者用 MOD は指定されていません。この空の申告だけですべて揃うかは未検証です。");
        for (var index = 0; index < client.Mods.Count; index++)
        {
            var mod = client.Mods[index];
            text.AppendLine($"{index + 1}. {mod.Name} / {mod.Version} / {(mod.ClientSide == "required" ? "必須" : "任意")}");
            text.AppendLine($"  Modrinth projectId: {mod.ProjectId} / versionId: {mod.VersionId}");
            if (mod.Note is not null) text.AppendLine($"  メモ: {mod.Note}");
        }
        return text.ToString();
    }

    private string DescribeServerAddons()
    {
        if (_session is null || _session.PreservedServerAddonCount == 0)
            return "サーバーの追加 MOD・プラグイン: 0件。サーバーMODの追加は今回未対応です。";
        var text = new StringBuilder($"既存のサーバー MOD・プラグイン: {_session.PreservedServerAddonCount}件（読み取り専用で保持）。\n");
        text.AppendLine("サーバーMODの追加は今回未対応です。既存の申告を保つため、サーバー実行環境の指定も固定されています。");
        foreach (var addon in _session.PreservedServerAddons)
        {
            text.AppendLine($"{addon.EntryId} / {addon.Kind} / {addon.Selection} / {addon.Source.FileName}");
            text.AppendLine($"  {addon.Source.Provider} projectId: {addon.Source.ProjectId} / versionId: {addon.Source.VersionId}");
            text.AppendLine($"  SHA-256（申告値）: {addon.Sha256} / サイズ: {addon.SizeBytes} バイト");
            text.AppendLine($"  必要な項目: {(addon.Requires.Count == 0 ? "指定なし" : string.Join(", ", addon.Requires))}");
        }
        return text.ToString();
    }
    private static string Display(string? value) => value ?? "指定なし";
    private static string DescribeIssue(string code) => code switch
    {
        "MissingRuntimePin" or "InvalidRuntimePin" or "InvalidPin" or "UnpinnedVersion" => "実行環境・MOD の正確な固定バージョンを指定してください。",
        "ClientMinecraftVersionMismatch" => "サーバーと参加者用の Minecraft バージョンを一致させてください。",
        "InvalidLength" => "名前・説明・識別子の文字数を確認してください。",
        "InvalidValue" or "InvalidType" => "項目の値・整数の範囲・選択肢を確認してください。",
        "DuplicateMod" => "参加者用 MOD のプロジェクトまたはバージョンが重複しています。",
        "TooManyMods" => "参加者用 MOD は256件以内にしてください。",
        "ManifestTooLarge" or "DefinitionTooLarge" => "出力する内容がサイズの上限を超えています。",
        "UnknownProperty" or "DuplicateProperty" or "MissingProperty" => "対応していない、重複した、または不足している項目があります。",
        "RevisionOverflow" => "このテンプレートは版番号の上限に達しているため編集できません。",
        "ServerRuntimeLocked" => "既存のサーバー MOD を保持するため、実行環境は変更できません。",
        _ => "対応形式・固定バージョン・依存関係を確認してください。"
    };
}
