# Experimental Windows runtime ownership

`WindowsOwnedRuntimeSession` is an opt-in foundation for newly created, cooperating local runtimes. It is not registered with `ServerRuntimeManager`, template capture, or production Apply. `UnsupportedStoppedRuntimeCaptureLeaseProvider` remains the default. This change does not enable server creation/import, run a server JAR, or change existing start/stop behavior.

## Supported boundary

- Windows 10+, local fixed NTFS, canonical absolute DOS paths only. UNC/device paths, aliases, reparse components, alternate streams and ambiguous components are rejected.
- Creation requires a new directory. All existing directories, including clean or crashed previous sessions, are rejected. There is no adoption or automatic crash recovery.
- Directory handles from drive root to leaf retain volume/file IDs and deny write/delete sharing. The leaf contains a flushed, exclusive, non-inherited reservation file created with `CREATE_NEW` and `OPEN_REPARSE_POINT`. The file and directory remain after disposal; they are never treated as permission to adopt.
- A private unnamed Job has `KILL_ON_JOB_CLOSE` and no breakaway flags. `PROC_THREAD_ATTRIBUTE_JOB_LIST` associates the process with the Job during `CreateProcessW`, eliminating a separately created, unassigned suspended interval. It is created suspended without inherited handles; cancellation is checked before resume. No shell is used. Executable/arguments are trusted application input, never template input.
- Retained process and Job handles supply evidence, rather than reopening a PID. A successful stop requires both Job active-process count zero and a signalled root handle. Root exit alone is insufficient. Only an explicit successful stop allows a stopped lease.
- Start, stop, creation, disposal and leases use `RuntimeOperationCoordinator.Shared`. A stopped lease strongly retains its owner and pins. Disposal rejects an active operation/lease promptly. Cancellation, timeout, failed termination or status query retain an unknown state and the directory reservation until a successful explicit stop/disposal retry.

## Deliberate limits

This is cooperative runtime ownership, not a security sandbox or a filesystem snapshot. It does not exclude arbitrary external programs from modifying descendants, protect against malicious same-user handle duplication, or contain launches delegated to services/WMI/other brokers. `CreateDirectoryW` and opening its pin are separate calls; the interval is not safe against a hostile directory replacement. Denial of ancestor sharing may reject otherwise usable directories; there is no weaker fallback.

The foundation does not implement safe recursive capture, staged-file identity, durable Windows transaction journals, atomic no-replace tree publication, or crash recovery. Those boundaries must be implemented and validated before capture/Apply can be enabled. Lost owner handles request Job cleanup but do not constitute proof of completed cleanup; rejecting every pre-existing directory prevents another owner from treating lock release as that proof.

## Validation

`WindowsOwnedRuntimeSessionTests` uses the dedicated `McServerManager.OwnershipTestChild` executable and fresh temporary directories. It exercises a native child that outlives its root; repeated stops; native cross-process reservation exclusion; leaf/ancestor rename and reservation replacement refusal; queued starts and disposal during leases; double lease disposal; GC owner retention; argument quoting; cancellation before launch, during the native suspended/pre-resume phase, after launch and during stop; injected termination/status failures; timeout and explicit retry. Fault injection wraps the real native Job. Six path-rejection theory cases run without creating a runtime.

These tests require permission to open read-attribute handles to every ancestor. An OS/sandbox denial is a failed precondition, not a passing or silently skipped native test. The test executable never contacts the network or runs Minecraft/Java. The helper project is test-only and is not referenced by the product project.

Two further native cases exercise abrupt owner-process death and an explicit breakaway request. The crash helper loads the product assembly, retains its session through self-termination without `Stop`/`Dispose`, and leaves a live child. The test retains an outer containment Job without terminating or closing it until the owner and descendants have all exited. The released reservation still does not permit adoption (`ERROR_ALREADY_EXISTS`, 183).

The breakaway helper first creates an ordinary suspended child as a control, then requests `CREATE_BREAKAWAY_FROM_JOB`; the latter is rejected with `ERROR_ACCESS_DENIED` (5). Both requests atomically assign a separate kill-on-close cleanup Job through `JOB_LIST`, and neither child is resumed. This proves rejection in the tested Windows Job hierarchy, including any enclosing restrictions; it does not isolate the production Job's flags from all possible outer Job policy. Unexpected successful creation is terminated through retained handles, with the cleanup Job as a final fallback.

Privileged symbolic-link creation remains unverified. Native tests are distinct from WPF screen interaction; this change adds no UI and no screen test evidence.

## API contracts

- [Microsoft: process attribute lists / JOB_LIST](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)
- [Microsoft: Job objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
- [Microsoft: CreateFile sharing and reparse flags](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
- [Microsoft: FILE_ID_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info)
