# Confirmed-plan transaction core (experimental)

This stage is a **synthetic-only, new-target transaction protocol**, not an enabled installer.
The WPF import route remains read-only. Normal creation, backups and server
controls are unchanged. No production filesystem store is registered, and no
review result is automatically converted into install authority.

The preceding [live MOD inspection](live-verification-2026-10-03.md) established
bounded Modrinth/Fabric declaration detection using real public artifacts.
Transaction tests use **synthetic bytes in isolated temporary directories**;
they establish different properties and do not establish that a real Minecraft
server can start or that a Windows filesystem adapter is safe.

## Implemented boundary and numeric limits

The core currently accepts only the `synthetic` runtime and provider and the
explicit `IsolatedSyntheticTests` storage capability. `config.json` destinations
are rejected, preventing synthetic targets from becoming discoverable servers.
Review settings, memory and port are fingerprinted test metadata, not generated
server configuration. The only supplied production storage implementation is
unsupported and refuses all writes. The adapter from verified Modrinth evidence
and exact runtime/settings into a production install plan is a remaining gate.

- 1–64 artifacts, each 1 byte–1 MiB, at most 16 MiB in total
- At most 32 settings; ASCII keys at most 96 characters and values at most 256
- Relative destinations at most 240 characters, 8 segments, 80 characters per segment
- Logical identities at most 96 ASCII characters; no caller-supplied source paths or URLs
- SHA-256 must be exactly 64 lowercase hexadecimal characters
- Memory metadata: 512–32768 MiB; port metadata: 1–65535; neither is applied

## Scope and safety boundary

A transaction creates an entirely new destination. Updating an existing server,
merging a mods directory, removing a conflict, replacing a file and downgrading
are unsupported. A complete reviewed plan must bind the target, baseline,
stopped-target requirement and exclusion identity, exact provider/version identities, destination
names, byte lengths and hashes. Changes require a new review and approval.

The storage abstraction is a security boundary, not a claim that checking a path
string makes ordinary `System.IO` operations race-free. Its implementation must
hold the directory identities and runtime exclusion lease while writing and
publishing. Unsupported storage must refuse before it creates anything.

The test-only filesystem implementation operates on private synthetic folders
under the test process's control. It is not a supported production backend and
is not evidence against hostile concurrent path replacement on Windows.

## Publication and cancellation

A complete verified directory is staged outside every server-discovery root,
on the same volume as the destination. The commit point is a no-replace atomic
directory publication. A production store must durably preserve the transaction
identity for restart reconciliation; this core does not implement that journal. The store
must revalidate target absence and the stopped lease within that publication
operation. Cancellation before publication may clean only owned staging data;
cancellation after publication must report that the destination was committed.
An uncertain commit outcome must stay uncertain until reconciled; it must never
be described as a successful rollback or cause automatic target deletion.

Cleanup and recovery must preserve foreign or modified data. Leftovers need an
explicit recoverable state rather than an exception that silently loses their
identity. A transaction identifier and plan fingerprint are required to identify
replays; retrying must not create another server or reuse changed approval.

## Windows continuation gates, in order

1. Implement and review a Windows-native store that holds trusted directory
   handles and rejects reparse points, junctions, symlinks, alternate data
   streams, device names, trailing-dot/space aliases and case collisions.
   Revalidate identity at operations, not only before path traversal. Reject
   unsupported volumes or atomic no-replace semantics. Test actual junction
   replacement and rename races, rather than relying on Linux behavior.
2. Introduce an exclusive stopped-target lease integrated with start, restart,
   shutdown and application disposal. A sampled `IsRunning == false` is not a
   lease. Test a start racing with commit and a lease lost during staging.
3. Add a durable journal keyed by installation ID and the reviewed fingerprint.
   Inject termination before/after every durable write and before/after rename.
   Reconcile the destination on restart and register exactly once. Leave an
   ambiguous or modified target untouched and present a recovery explanation.
4. Build the full plan from the strict template, supported exact runtime,
   allowlisted generated settings and resolved provider evidence. Acquire each
   payload through the existing permitted provider adapter, recheck the bytes
   against the approved hashes, and keep paths/URLs outside user authority.
   Inspection success alone does not authorize application or an EULA decision.
5. Bind the confirmation dialog to the exact immutable plan. Display the new
   target, runtime versions, files and prerequisites, required agreements and
   warnings. Editing any input invalidates approval. Test double-click,
   navigation, closing, stale async results and cancellation at each phase.
6. Generate final configuration only in staging. Keep staging outside all roots
   searched by `ServerConfigService.LoadAll`. Atomically publish the complete
   folder, then reconcile registration and show **created, stopped**. A UI
   notification failure must not be turned into a filesystem rollback.
7. Run native Windows unit/integration tests for access denied, disk full,
   antivirus/file-sharing contention, long paths and process interruption.
   Exercise the actual WPF flow at 100/150/200% DPI with keyboard navigation.
   Regression-test normal free creation, backup and start/stop controls.
8. Only after those gates pass may the production apply control be connected.
   Real server startup and joining remain a separate explicitly authorized
   verification step; this milestone executes no MOD, class or server JAR.

CurseForge, billing, entitlement checks, arbitrary scripts, automatic networking
and account services are outside this milestone.

## Verification on 2026-10-03

- Aggregate .NET 8 `McServerManager.TemplateTests`: **277 passed, 0 failed,
  0 skipped** on Linux; 66 cases were added for this transaction stage
- The production-source core and test-only real-directory backend are tested
  together. Linux tests use `statx` identities and `renameat2(RENAME_NOREPLACE)`
  in private temporary folders, a simulated start-exclusion lock, and an
  **in-memory** journal. They do not prove process-crash durability
- Tests cover source sizes/hash mismatch, partial streams/writes, immutable
  approval changes, repeated and concurrent attempts, pre/post-commit cancel,
  ambiguous publication, changed leases/baselines/anchors, destination races,
  symlink/hardlink/foreign entries, stage identity-capture failure, and cleanup
  failures. Committed destinations are never deleted by recovery
- WPF application and existing Windows test project: Release cross-builds
  succeeded with 0 errors. Existing Open.NAT compatibility and nullable warnings
  remain. These are compile checks, not Windows test execution
- Native Linux directory cases are explicitly skipped on non-Linux hosts;
  passing the remaining Windows-hosted tests cannot substitute for gate 1

Reproduce the offline suite:

    dotnet test McServerManager.TemplateTests/McServerManager.TemplateTests.csproj -c Release

Cross-compile the application and existing Windows tests from Linux:

    dotnet build McServerManager.Tests/McServerManager.Tests.csproj -c Release -p:EnableWindowsTargeting=true

The prior real API/JAR inspection report is unchanged; this stage did not run
MODs, install a server, or repeat its live downloads.
