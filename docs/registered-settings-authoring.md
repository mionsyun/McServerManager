# Registered Vanilla settings → Pro template draft

This slice connects normal server creation and the selected registered server to the existing Pro template editor. It does **not** enable current-server capture, template application, installation from a template, or automatic launch.

## User flow

1. Normal creation of a fixed numeric-release **Vanilla** server saves a versioned creation-time runtime declaration in its local registration. The selected type/version are copied before the asynchronous download and used consistently for the download, registration, and declaration.
2. In Pro, select that registered server and choose **選択サーバーの登録設定から下書き（Pro）**. Unsaved server settings must first be saved or discarded. The action copies the selected registration once, without reading its directory or stopping/starting anything.
3. The existing editor receives a new template ID, revision 1, the declared Vanilla version, name, and six portable settings: difficulty, game mode, player limit, view distance, PvP, and spawn protection. Simulation distance is unspecified. Addons are empty and client definition is absent.
4. The draft is marked unsaved. Review, acknowledgement, create-only file save, cancellation handling, and subsequent revision editing use the existing authoring flow. Editing the runtime changes the author's declaration; it does not inherit any stronger creation-time assurance.

Free still uses/imports templates and performs dependency inspection. Creating this draft and exporting it require the existing Pro capabilities. Production Capture/Apply remains disabled in both editions.

## What the creation record means

`CreationRuntimeDeclaration` is a schema-versioned local record bound to the originating registration's canonical server ID, exact `Vanilla` type and exact Minecraft version. It records the selection used during normal provisioning, not installed JAR identity, official-provider attestation, hashes, current runtime state, or crash durability. Config files are editable; this marker is not a signature or security authority. It cannot authorize filesystem access, publication, cleanup, or application.

Legacy registrations without the record are not upgraded or guessed. Missing/malformed/unsupported records, nonmatching identities or versions, aliases, snapshots, `latest`, and non-Vanilla families fail closed with a manual-input explanation. A copied registration with a different server ID does not inherit the original declaration. Paper/Fabric/Forge need their resolved build/loader/installer provenance before a similar bridge is possible; those pins are not inferred here.

## Data and UI boundaries

The immutable projection contains only the allowlisted registration values and creation declaration. It does not expose the mutable `ServerConfig` to the authoring editor. It does not copy paths, Java arguments, ports, MOTD, world name/seed, connection information, backups, accounts or filesystem contents. The originating server ID is used for local consistency and is not written into the template.

Registration values may differ from `server.properties` or the installed JAR. The window and review explicitly describe the source as registered settings and leave current files, mods, worlds, provider metadata, dependency completeness and execution unverified. No filesystem stop/exclusion lease is claimed for this data-only projection.

Template schema, immutable template ID/revision semantics and existing validation remain unchanged. No local visibility flag, account, entitlement backend, database or publishing capability is added. Source metadata is not an ownership model for a future sharing platform.

## Verification and remaining completion work

Offline tests exercise real provisioning with a harmless synthetic downloader, record persistence/roundtrip, mutable-option changes during download, supported and rejected declarations, Pro/Free boundaries, field allowlisting, draft dirty/review lifecycle, and real isolated template file save/reopen. They do not download or execute Minecraft, test Windows UI behavior, or establish native filesystem guarantees.

On 2026-10-05, .NET SDK 8.0.415 on Linux passed all **863 tests in each of Free and Pro** (102 added, zero failures/skips on the final source). The application, Windows test project and ownership child fixture cross-built in both editions with zero errors and nine existing Open.NAT/nullable warnings each. Independent review found no remaining blockers; focused independent-copy runs passed 104 cases per edition, including three additional probes.

The first full run exposed an existing runtime-test helper race: `WaitAsync(token)` could resume and dispose an older cancellation-observation registration before that callback ran. The test-only helper now records actual cancellation in its filtered catch and rethrows it. Production runtime code is unchanged. A 200-iteration repository regression passes; an independent 10,000-iteration comparison reproduced missed signals in the old instrumentation and none in the corrected version. The original failed run was investigated, not skipped or counted as passing.

Reproduce with `dotnet test McServerManager.TemplateTests/McServerManager.TemplateTests.csproj -c Release -p:MaiPilotEdition=Free`, then `Pro`. Cross-build with `dotnet build McServerManager.Tests/McServerManager.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:MaiPilotEdition=Free -m:1`, then `Pro`. Cross-building does not run Windows native tests or prove the rendered UI.

The requested full workflow still requires safe current-server inspection, complete runtime/addon provenance, production transaction storage and template application, followed by Windows UI and real server launch/connection verification. This bridge is a completed portion of that workflow, not completion of Pro.
