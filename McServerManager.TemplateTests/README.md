# Offline template core checks

Run with the .NET 8 SDK on Windows, Linux, or macOS:

```sh
dotnet test McServerManager.TemplateTests/McServerManager.TemplateTests.csproj --configuration Release
```

The project links the production template/dependency sources directly, without WPF or a Windows runtime. It also checks the presentation-only inspection ViewModel. No live APIs, Minecraft startup, addon execution, or downloads are involved. This is not an end-to-end server provisioning or compatibility test.

`TemplateManifestTests` includes the supplied Vanilla fixture and all six supplied negative-schema fixtures. Its adversarial assertions cover:

- Exact-byte SHA-256, UUID/schema checks, allowlisted settings and limits
- Unknown and duplicate properties (including escaped keys) at every object level
- Strict UTF-8, invalid Unicode escapes, invalid JSON, comments/trailing commas and depth
- Required Paper/Forge builds and Fabric loader/installer pins; no `latest` or ranges
- Windows-safe `.jar` filenames, reserved devices, paths, alternate streams and case collisions
- Modrinth identity/hash shapes and fail-closed CurseForge disabling
- Addon counts, per-file/aggregate declared sizes, duplicate IDs/projects/artifacts
- Missing references, self/cyclic dependencies and longest-path depth (including shared graph nodes)
- Exact byte-boundary input, bounded nonseekable/partial stream reads, cancellation and I/O failures

The fixtures are design examples, not official, supported-version recommendations or boot-tested recipes. Valid JSON is only locally consistent data; provider entitlement, file identity, runtime support, dependency metadata, and execution safety remain unverified.

Application policy deliberately narrows some schema shapes: JSON integer tokens (rather than decimal/exponent spellings) are required; revision is bounded to positive `Int32`; runtime identifiers are bounded to 96 characters; at most one addon per provider project is accepted; dependency depth counts entries (maximum 16). Filename checks use Windows rules on every host OS. Limits are owned by the application, never by a manifest.
