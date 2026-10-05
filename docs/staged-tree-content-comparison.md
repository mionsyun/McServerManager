# Staged-tree content comparison prerequisite

This slice adds `StagedTreeManifestVerifier`, a **pure content-snapshot comparison**, not a Windows storage adapter or a crash-recovery implementation. Production Capture/Apply, `UnsupportedTransactionStorage`, and the native intent journal are unchanged.

## Contract and bounds

`Compare` first validates the existing synthetic transaction plan. The expected namespace consists of every approved artifact and every implicit parent directory, excluding the stage root. Directories must be explicitly present in the reported snapshot with zero size and no hash. Files must have their approved exact byte length and lowercase SHA-256. Entry ordering is irrelevant; spelling is ordinal and exact.

Snapshots are immutable arrays and contain at most 512 entries (64 artifacts times eight path segments). A default array, null entry, unsafe path, unknown kind, invalid size/hash, duplicate entry, missing entry, changed kind, extra file, or extra empty directory cannot match. Duplicate detection is case-insensitive. The validator does not normalize or silently merge names.

Transaction plan validation now rejects inconsistent parent-directory casing, including `Mods/a.bin` alongside `mods/b.bin`, in either artifact order. Previously those distinct file paths could pass the plan's file-only collision checks. Existing consistent plans retain their fingerprint encoding; ambiguous plans now fail before source reads or staging.

## Evidence is not authority

`ContentMatches` means only that **the supplied records** match the plan. The comparator performs no filesystem I/O and does not establish that a caller reported a complete or genuine tree. It does not read or hash file bytes, pin handles, inspect alternate data streams, detect native aliases/reparse points/hardlinks, prove ownership, freeze content, reserve an install ID, hold a stopped lease, or acknowledge durable writes. Its result is not a receipt and must never authorize publication, cleanup, adoption of an existing directory, or recovery by itself.

A future native adapter must obtain fresh hashes and the complete namespace from the same identity-pinned, safely traversed objects that it will publish, and must keep its independent ownership, start-exclusion, no-replacement, and journal guarantees. The comparator does not bridge the current journal observation to `Published` or `OwnedStage`.

## Test integration

The existing isolated Linux test store invokes the comparator before its no-replace rename. It still checks the complete journaled tree and native identities with `TreeMatches`, then independently rereads actual file sizes and hashes rather than copying approved hashes into the snapshot. The simulated stopped lease and publication remain under the fixture's lock. This is a cooperative test fixture with an in-memory journal, not an adversarial race-safe implementation.

Tests cover missing implicit directories, extra empty directories, exact casing, invalid metadata and paths, duplicate entries, wrong size/hash/type, invalid plans, and the 512-entry maximum. Linux integration tests cover nested/root files, rejection before I/O, and journal-consistent but unapproved staged bytes/files that pass the fixture's ownership check but must fail plan comparison. Rejected unpublished fixture stages are cleaned only with their original receipt; no final destination is removed.

Cloud verification on 2026-10-05 used .NET SDK 8.0.415 on Linux: the full offline suite passed 761/761 for each of Free and Pro (41 added, zero failures or skips). The WPF application, Windows test project, and ownership child fixture cross-built for both editions with zero errors. Existing Open.NAT compatibility and ServerViewModel nullable warnings remain. An independent source review found no blockers; an isolated package-free probe passed 76,263 assertions across 150 randomized trees and mutation/boundary cases. These are not Windows/native execution results.

Reproduce the offline suite with `dotnet test McServerManager.TemplateTests/McServerManager.TemplateTests.csproj -c Release -p:MaiPilotEdition=Free`, then repeat with `Pro`. On a Linux SDK, cross-build the Windows project with `dotnet build McServerManager.Tests/McServerManager.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:MaiPilotEdition=Free -m:1`, then repeat with `Pro`. A cross-build does not run that project's native tests.

## Still required

Durable storage-wide install-ID reservation, handle-relative traversal, staged-tree freezing, non-overwriting native publication and uncertain-outcome reconciliation, stopped-lease integration, and ownership-proven crash cleanup remain unimplemented. This slice does not establish Windows/NTFS durability, power-loss behavior, native cleanup, real server or MOD execution, or production readiness.
