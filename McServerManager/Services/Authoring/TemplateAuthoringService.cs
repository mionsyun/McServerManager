using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using McServerManager.Models.Authoring;
using McServerManager.Models.Editions;
using McServerManager.Models.Templates;
using McServerManager.Services.Editions;
using McServerManager.Services.Templates;

namespace McServerManager.Services.Authoring;

/// <summary>
/// Pro declaration authoring only, independent of license verification. Strict offline parsing is
/// the sole validation boundary; there is no server capture, provider lookup, install or execution.
/// </summary>
public sealed class TemplateAuthoringService(IEditionPolicy editionPolicy, ITemplateManifestService manifestService)
    : ITemplateAuthoringService
{
    private readonly IEditionPolicy _editionPolicy = editionPolicy ?? throw new ArgumentNullException(nameof(editionPolicy));
    private readonly ITemplateManifestService _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
    private readonly ConditionalWeakTable<TemplateAuthoringSession, SessionState> _sessions = new();
    private readonly ConditionalWeakTable<TemplateAuthoringPreparedExport, PreparedState> _prepared = new();

    public TemplateAuthoringOpenResult CreateNew(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCapability(EditionCapability.TemplateCreate);
        var session = new TemplateAuthoringSession(Guid.NewGuid(), 0, 1, false, null,
            Array.AsReadOnly(Array.Empty<TemplateAddon>()));
        _sessions.Add(session, new(null));
        return new() { IsValid = true, Session = session, Draft = new(), Issues = Array.Empty<TemplateValidationIssue>() };
    }

    public TemplateAuthoringOpenResult OpenForEdit(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCapability(EditionCapability.TemplateEdit);
        var parsed = _manifestService.Parse(utf8Json);
        cancellationToken.ThrowIfCancellationRequested();
        if (!parsed.IsValid || parsed.Manifest is null)
            return new() { IsValid = false, Issues = parsed.Issues };
        var manifest = parsed.Manifest;
        int revision;
        try { revision = checked(manifest.Revision + 1); }
        catch (OverflowException)
        {
            return new() { IsValid = false, Issues = Issue("RevisionOverflow", "$.revision", "リビジョンが上限のため編集版を出力できません。") };
        }
        var session = new TemplateAuthoringSession(manifest.TemplateId, manifest.Revision, revision, true,
            parsed.ManifestSha256, manifest.Addons);
        _sessions.Add(session, new(manifest.Runtime));
        return new()
        {
            IsValid = true, Session = session, Issues = Array.Empty<TemplateValidationIssue>(),
            Draft = new()
            {
                Name = manifest.Name, Description = manifest.Description, Runtime = manifest.Runtime, Settings = manifest.Settings,
                ClientDefinition = manifest.ClientDefinition is not { } client ? null : new()
                {
                    Name = client.Name, MinecraftVersion = client.Runtime.MinecraftVersion, Loader = client.Runtime.Loader,
                    LoaderVersion = client.Runtime.LoaderVersion, JavaVersion = client.Runtime.JavaVersion,
                    Mods = Array.AsReadOnly(client.Mods.Select(mod => new TemplateAuthoringClientModDraft
                    {
                        Provider = mod.Provider, ProjectId = mod.ProjectId, VersionId = mod.VersionId,
                        Name = mod.Name, Version = mod.Version, ClientSide = mod.ClientSide, Note = mod.Note
                    }).ToArray())
                }
            }
        };
    }

    public TemplateAuthoringValidationResult PrepareExport(TemplateAuthoringSession session, TemplateAuthoringDraft draft,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = RequireSession(session);
        ArgumentNullException.ThrowIfNull(draft);
        RequireAuthoringAndExport(session);
        if (session.IsServerRuntimeLocked && draft.Runtime != state.OriginalRuntime)
            return Invalid("ServerRuntimeLocked", "$.runtime", "既存のサーバー追加要素を保持する場合、実行環境の宣言は変更できません。");
        byte[] bytes;
        try { bytes = TemplateAuthoringSerializer.Serialize(session, draft, cancellationToken); }
        catch (TemplateAuthoringSerializationException exception)
        { return Invalid(exception.Code, exception.Path, exception.Message); }
        var validation = _manifestService.Parse(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (!validation.IsValid || validation.Manifest is null || validation.ManifestSha256 is null)
            return new() { IsValid = false, Issues = validation.Issues };
        var token = new TemplateAuthoringPreparedExport(session.TemplateId, session.Revision, validation.ManifestSha256,
            validation.Manifest.ClientDefinition?.InputSha256, bytes.Length);
        _prepared.Add(token, new(session, bytes));
        return new() { IsValid = true, PreparedExport = token, Issues = Array.Empty<TemplateValidationIssue>() };
    }

    public byte[] Export(TemplateAuthoringSession session, TemplateAuthoringDraft currentDraft,
        TemplateAuthoringPreparedExport preparedExport, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireSession(session);
        ArgumentNullException.ThrowIfNull(currentDraft);
        ArgumentNullException.ThrowIfNull(preparedExport);
        RequireAuthoringAndExport(session);
        if (!_prepared.TryGetValue(preparedExport, out var prepared) || !ReferenceEquals(prepared.Session, session))
            throw new InvalidOperationException("この編集セッションで検証した出力を使用してください。");
        byte[] currentBytes;
        try { currentBytes = TemplateAuthoringSerializer.Serialize(session, currentDraft, cancellationToken); }
        catch (TemplateAuthoringSerializationException exception)
        { throw new InvalidOperationException("入力が変更されています。もう一度検証して確認してください。", exception); }
        // Both snapshots are bounded. Compare exact bytes as well as the displayed digest to bind
        // output to precisely the reviewed declaration, including client notes and optional settings.
        if (!currentBytes.AsSpan().SequenceEqual(prepared.Bytes) ||
            !string.Equals(Hash(currentBytes), preparedExport.ManifestSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("入力が変更されています。もう一度検証して確認してください。");
        cancellationToken.ThrowIfCancellationRequested();
        return prepared.Bytes.ToArray();
    }

    private SessionState RequireSession(TemplateAuthoringSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_sessions.TryGetValue(session, out var state))
            throw new InvalidOperationException("このサービスで作成または読み込んだ編集セッションを使用してください。");
        return state;
    }

    private void RequireAuthoringAndExport(TemplateAuthoringSession session)
    {
        RequireCapability(session.IsEditing ? EditionCapability.TemplateEdit : EditionCapability.TemplateCreate);
        RequireCapability(EditionCapability.TemplateExport);
    }

    private void RequireCapability(EditionCapability capability)
    {
        if (!_editionPolicy.Allows(capability))
            throw new InvalidOperationException("テンプレートの作成・編集・出力には MaiPilot Pro が必要です。");
    }

    private static TemplateAuthoringValidationResult Invalid(string code, string path, string message) =>
        new() { IsValid = false, Issues = Issue(code, path, message) };

    private static IReadOnlyList<TemplateValidationIssue> Issue(string code, string path, string message) =>
        Array.AsReadOnly(new[] { new TemplateValidationIssue(code, path, message) });

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed record SessionState(TemplateRuntime? OriginalRuntime);
    private sealed record PreparedState(TemplateAuthoringSession Session, byte[] Bytes);
}
