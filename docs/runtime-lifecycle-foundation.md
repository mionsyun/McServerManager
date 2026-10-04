# Runtime lifecycle foundation

This increment improves ordinary server lifecycle reporting and process-local coordination. It does not enable existing-server template capture or template application.

## Why operational state and ownership are separate

Microsoft documents that waiting for a process after `Kill(entireProcessTree: true)` does not prove its descendants have exited ([Process.Kill reference](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0), applies to .NET 8).

A newly registered folder does not prove that another MaiPilot instance or an external process is not using it. A direct child process exiting also does not prove that all descendants have exited. The runtime therefore records launch ownership separately from the existing start/stop display.

- A failed termination request or unsuccessful exit wait must leave the runtime in `Unknown`, rather than displaying a successful stop.
- A confirmed direct launch-root exit may be operationally `Stopped`, but its lifetime remains `OwnedRootExitedUnverified`.
- A shell wrapper such as Forge `run.bat` may exit while its server continues. Its root exit is insufficient even for operational stop confirmation and must not trigger another automatic launch.
- None of these observations authorizes stopped-server capture, installation, or filesystem mutation under an exclusive ownership claim.

## Coordination boundary

The lifecycle coordinator is process-local. A lease prevents cooperating lifecycle operations from running simultaneously; it does not establish cross-instance exclusion, process-tree ownership, stable native directory identity, or protection against external filesystem writers. Manual and automatic starts must use the same coordinator as future inspection/capture callers. Production capture remains unavailable until a native adapter supplies the missing evidence.

Automatic restart requests must be tied to the originating launch and invalidated by manual stop, replacement, or release. Late output and exit callbacks from an older launch must not change a newer launch. Stop intent survives an uncertain stop, so a subsequent exit cannot turn that requested stop into an automatic restart.

## User-visible failure handling

An uncertain stop displays a stop-unconfirmed state and offers another stop check. It does not enable Start or Restart. A failed runtime release aborts server deletion before touching its folder. Successful-start timestamps are only updated for `Starting` or `Running` outcomes.

## Resource limits

- Stop timeout input is clamped to 0–300 seconds. The normal default remains 10 seconds; stdin send, graceful wait, and forced-root wait are separately bounded. Forced-root wait has a minimum of 1 second.
- Ordinary command writes are limited to a 10-second wait.
- Automatic restart delay is clamped to 1–300 seconds.
- Output processing retains at most 256 pending lines per runtime; UI logging retains at most 512 pending lines and 5,000 displayed lines. A line is truncated at 16,384 UTF-16 code units. Excess lines are counted and a dropped-lines notice is shown. These are display limits, not proof of complete log capture.
- The normalized registered directory and server ID are pinned for a runtime. Different IDs with the same normalized directory cannot launch concurrently within that manager when another runtime is live or uncertain. Native aliases and separate application instances still require the missing native adapter.

## Verification boundary and remaining gates

Synthetic process handles and isolated temporary files exercise control flow without launching a server, Java, a batch file, or a MOD. Linux tests and Windows cross-compilation cannot establish native Windows process or UI behavior.

Before enabling production capture/application:

1. Establish cross-instance exclusion tied to stable directory identity and durable ownership of the complete launched process tree, including wrappers.
2. Validate Windows handle/process-tree exit, abandoned/externally launched processes, PID reuse, stop failure, and directory replacement.
3. Integrate exclusive stopped-operation leases into the actual source-reading and mutation operations. Existing world backup/restore/import/delete, world switching, duplication, and version replacement still include status-snapshot or uncoordinated paths. Scheduled backups do not currently claim a stopped source. Server deletion still releases its runtime before later path validation/filesystem deletion; if that later step fails, the retained view can be bound to a removed runtime. A future deletion transaction must preserve or restore that binding and span the actual mutation with an appropriate lease. This increment only fixes proceeding when release itself fails.
4. Validate native UI notification ordering, actual dialogs and retry behavior, and application close behavior with live or uncertain owned processes. Closing the application is not evidence that those processes stopped.
5. Keep exact Paper build/Fabric loader identities and explicit client declarations independent from inferred registration settings. Existing registration alone cannot supply a verified template recipe.

No real server or MOD execution, actual process termination, network/security change, or production capture/application is part of this test milestone.

### Windows continuation

First run the focused STA regressions in both editions: `dotnet test McServerManager.Tests/McServerManager.Tests.csproj -c Release -p:MaiPilotEdition=Free --filter FullyQualifiedName~RuntimeUnknownViewModelTests` and repeat with `MaiPilotEdition=Pro`. They use isolated synthetic state/files and do not start Minecraft. Then check the actual status badge, stop retry control, deletion warning, and dispatcher ordering in the native app.

A later native ownership adapter needs separate controlled-process fixtures for direct child exit, a wrapper whose child remains alive, stop timeout/failure, late exit, and multiple app instances. Complete its stable NTFS identity/reparse-point and lease-abandonment tests before connecting registered-server capture or enabling application. The existing unsupported provider must remain the default until that work is implemented and reviewed; passing synthetic tests alone does not remove the gate.

## Verified on 2026-10-04

- Cross-platform Release suite: Free **720/720**, Pro **720/720**, zero failures/skips. These are the same 720 cases under each build-time edition.
- This increment adds **55** cross-platform cases: 33 actual-manager orchestration cases using synthetic processes/timers, 13 termination/coordinator cases, and 9 resource-limit/notification-lock cases.
- Six independent source-linked synthetic probes additionally covered flood readiness, observer isolation, cancellation after kill, release-before-queued-start, pinned absolute launch paths, and collection-notification lock ordering.
- Free and Pro Windows/WPF application plus Windows test-project cross-builds: zero errors. The existing five warnings remain (Open.NAT compatibility and the pre-existing nullable warning in ServerViewModel).
- Two new Windows STA regressions for Unknown-state controls and failed-release deletion preservation compile, but were **not run**. Actual Windows process-tree behavior, native screen interaction, and native stopped-capture leases were **not verified**.

No fixture test is evidence of actual Minecraft/MOD execution or native process ownership.
