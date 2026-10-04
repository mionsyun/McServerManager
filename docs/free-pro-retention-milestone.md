# Free / Pro identity and shared-settings retention

This is a bounded implementation milestone, not a released Pro product. It adds
build identity, a central capability policy, guarded store/update navigation,
safer shared settings, and a participant-reference ZIP service. It does not add
billing, license keys, advanced authoring UI, a product listing, or a price.

## Edition and update safety

- An unspecified build is **Free**. `-p:MaiPilotEdition=Pro` selects **Pro**;
  any other value fails the build. Assembly metadata and the footer identify
  the distribution. App settings and imported JSON cannot change the edition
- `IEditionPolicy` is the common capability boundary. Basic template use/import,
  dependency inspection and participant-list export are available to both
  editions. Template authoring/editing/export is reserved for Pro; these
  advanced features themselves are not implemented by this milestone
- Production template application remains disabled in **both** editions. A
  purchase or Pro build cannot override an uncompleted runtime-safety gate
- Pro refuses both public updater entry points before network access, directory
  creation, installer download or launch. Direct calls cannot bypass the guard
- Free keeps compatibility with legacy update manifests without an edition
  field. Explicit incompatible, null, invalid or ambiguous edition metadata is
  rejected. Existing Free installer verification behavior is otherwise unchanged

The footer contains the edition label. The Free purchase button is hidden until
an actual Pro product URL is verified and compiled into the release. Pro uses a
separate purchased-library update action; it is disabled until that exact route
is verified. Neither action runs automatically. URLs are not taken from user
settings, a downloaded manifest, or a template. No future product URL is invented
and the existing same-feature support item cannot be used as Pro.

`ProDistributionLinks` has separate empty verified/configured values for the Pro
product and BOOTH purchased library. Configuring them is a later reviewed
release change; this milestone does not verify a real destination or complete a
purchase. No price is compiled into the UI.

## Retaining existing settings

Both builds continue using the same existing `%APPDATA%\MaiPilot` directory for
the same Windows user. The existing test-only app-data override remains
unchanged. Switching editions does not copy, relocate, reset or convert the
server folders. Theme, registered external roots, onboarding state and update
preferences remain common settings. Future Pro editor state should use its own
sidecar rather than changing the meaning of shared fields.

`AppSettingsService` now:

- Reads at most 1 MiB of strict UTF-8 JSON with depth at most 32
- Preserves unknown fields when updating known settings
- Accepts legacy unversioned files and optional `SchemaVersion: 1`; refuses
  unsupported schema versions, duplicate/case-aliased known fields, invalid
  values and corrupt data instead of manufacturing writable defaults
- Treats a missing primary with an existing backup as a recovery case
- Holds a persistent `.lock` file lease across read/validate/update/commit;
  acquisition has a two-second bound
- Detects stale or untracked objects before overwriting existing settings
- Stages in the same directory, flushes, then atomically replaces with a `.bak`
  containing the previous exact file bytes. First creation is no-overwrite
- Does not fall back to direct writes or automatically restore an ambiguous
  failure. Typed failures retain a safe user-facing explanation

This lock coordinates **these new builds**. Older installed builds and external
editors do not honor it, so close old instances before switching editions.
Linux fixture checks do not establish Windows network-volume/AV/file-sharing
semantics or durability after power loss. Keep the previous valid backup; do not
blindly copy it over an unknown or concurrently changed primary.

On a settings read failure, startup stops without resetting the data. A settings
save failure aborts the operation without deliberately shutting down running
servers. UI cache fields are changed only after the corresponding durable save
succeeds. The first-run dialog uses a fresh partial update rather than writing a
stale snapshot after the dialog closes.

### External scheduled backups

Startup schedule restoration now passes the user's registered external roots to
`LoadAll`. A scheduled entry retains its original server directory and reloads
from that directory on each tick, rejecting a changed server identity instead
of accidentally using a same-ID configuration under the default root. Fixture
regressions exercise restoration and a tick against an isolated temporary world.
This does not change backup archive formats or claim that a server is stopped.

## Participant reference ZIP

The free service accepts only an explicit parsed **client** definition; it does
not infer a client pack from a server inspection. It creates `README.txt`,
`mods.txt` and `manifest.json`, with names, exact versions and official-domain
version-page links. No MOD JAR, script or launcher profile is included, and no
special launcher is required. Provider identity, dependency completeness and
runtime compatibility are explicitly **not verified** by this offline parser.

The participant parser/export core is tested but is not yet connected to a WPF
client-definition review/export flow. It must remain free when that flow is
added. See [participant-list-format.md](participant-list-format.md) for the
strict format, limits and readiness labels.

## Reproduction

Run the same source tests sequentially for both build editions:

    dotnet test McServerManager.TemplateTests -c Release -p:MaiPilotEdition=Free
    dotnet test McServerManager.TemplateTests -c Release -p:MaiPilotEdition=Pro

Cross-build the app and existing Windows tests:

    dotnet build McServerManager.Tests -c Release -p:EnableWindowsTargeting=true -p:MaiPilotEdition=Free
    dotnet build McServerManager.Tests -c Release -p:EnableWindowsTargeting=true -p:MaiPilotEdition=Pro

Do not run simultaneous Free/Pro builds into the same output directory. A future
packaging pipeline must verify assembly edition metadata and artifact labels
before distributing anything; this milestone does not generate or publish a
Pro installer.

## Windows continuation gates

1. Close old Free/Pro instances and verify real server processes are stopped
   using the normal controls. In-memory manager state alone is not proof
2. Back up the existing common settings and server configuration/world data.
   Test same-user Free-to-Pro installation with a separately labeled setup.exe;
   do not assume a lone exe is the complete current application distribution
3. Verify theme, onboarding, external server roots and scheduled backups remain
   visible, with no migration or unexpected server start
4. Exercise native same-process and cross-process lock contention, stale writes,
   access denial, disk full, backup/replace failure and interruption on supported
   local Windows filesystems. Preserve ambiguous recovery evidence
5. Check the Free/Pro footer, disabled/unconfigured navigation, keyboard access,
   scaling, and real browser handoff only after actual BOOTH routes are verified
6. Finish the explicit client-definition preview/export UI, showing input-only
   readiness and obtaining a destination before writing a reference ZIP
7. Test installer identity, upgrade/uninstall data retention, and edition-safe
   update flows separately. No purchase, publication, release, signing-policy
   change or new credentials were performed here

## Verified on 2026-10-04

- Free build: **466 tests passed, 0 failed, 0 skipped**
- Pro build: **466 tests passed, 0 failed, 0 skipped**; these are the same 466
  cases under a second build identity, not 932 different tests
- Free and Pro app plus existing Windows-test project Release cross-builds:
  **0 errors** each. Existing Open.NAT compatibility and nullable warnings remain
- The invalid-edition build rejection was checked separately
- Synthetic Linux settings probes exercised a separate process holding the lock,
  bounded Busy result, process termination releasing the lock, interruption after
  staged-file flush, exact primary/backup retention and a subsequent replacement
- No native Windows tests/UI, real edition-switch installation, installer
  generation, real BOOTH navigation or purchase were run

The previous live MOD declaration evidence and synthetic transaction milestone
remain unchanged. No MOD, server or downloaded installer was executed for these
checks.
