# Participant reference-list ZIP core

参加者向け ZIP は、明示的なクライアント定義 JSON を読み込み、内容を確認して保存します。
Free / Pro のどちらも共通の出力権限を持ちます。定義を作成・編集する GUI は別の作業です。
サーバーの MOD 一覧から参加者に必要な MOD を自動で推測することはありません。

The application review flow uses this core API and requires an explicit client
definition. Inspected server artifacts are never used to infer this list. See
`participant-export-ui-milestone.md` for the UI boundary and verification limits.

`ParticipantClientDefinitionService` accepts bounded UTF-8 JSON with this shape:

```json
{
  "schemaVersion": "1.0",
  "definitionKind": "client",
  "name": "友達用クライアント",
  "runtime": {
    "minecraftVersion": "1.21.1",
    "loader": "fabric",
    "loaderVersion": "0.16.9",
    "javaVersion": "21.0.2"
  },
  "mods": [
    {
      "provider": "modrinth",
      "projectId": "AbCd1234",
      "versionId": "EfGh5678",
      "name": "サンプル MOD",
      "version": "1.2.3+fabric",
      "clientSide": "required",
      "note": "管理者に確認してください。"
    }
  ]
}
```

The IDs above are synthetic test data, not recommended or provider-verified MODs.
The parser accepts Minecraft release identifiers, Fabric or Forge, a numeric
loader pin, and a Java release pin with at least three components (optionally a
numeric `+build`). Java vendor, architecture and runtime compatibility are not
established. MOD version IDs are eight-character Modrinth identifiers; display
versions are fixed safe tokens. Client side is explicitly `required` or `optional`.
The optional note is bounded plain single-line text. Unknown fields, duplicate
properties, duplicate project/version IDs, arbitrary URL inputs, paths, newlines,
markup, control characters and unsupported providers are rejected.

Application-owned limits are 256 KiB of input, JSON depth 8, 256 MODs, 120 UTF-16
code units for names, 96 for pinned version tokens, and 240 for an optional note.
The archive has a 2 MiB maximum. Definitions cannot override these limits. Empty
MOD lists are allowed only as an explicit client definition and are visibly marked
as empty; they do not assert that no client MODs are needed.

The successful definition is an opaque immutable object. The exporter cannot
accept a caller-constructed manifest or server inspection result. It checks the
shared `ParticipantPackExport` capability, available in both Free and Pro.

`ParticipantListZipService.CreateAsync` returns complete ZIP bytes in memory.
Its only entries are `README.txt`, `mods.txt` and `manifest.json`, encoded as UTF-8.
It neither accepts nor writes a user filesystem path. Cancellation or failure
returns no partial archive. ZIP entry names and timestamps are fixed; output is
deterministic for the same validated input. No binary, script, automatic installer,
launcher profile or required external launcher is included. Page URLs are derived
only from validated Modrinth project/version IDs.

Validation establishes input shape and local consistency only. All outputs visibly
state that provider metadata, ID/version mapping, client support, dependency
completeness and runtime compatibility remain unverified. No network access,
Minecraft launch, MOD execution or JAR download is performed by these tests.

```sh
dotnet test McServerManager.TemplateTests/McServerManager.TemplateTests.csproj --configuration Release --filter FullyQualifiedName~ParticipantListZipTests
```
