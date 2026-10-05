# Windows intent journal: bounded prerequisite only

This stage implements one create-only intent record and read-only reconciliation in a newly owned, isolated NTFS directory. It is **not an `ITransactionStorage` implementation**. It does not create an artifact stage, publish a destination, recover a writable owner, or delete any data. Production Capture/Apply and `UnsupportedTransactionStorage` remain unchanged.

## Why this is a separate stage

`ITransactionStorage` requires per-installation serialization, complete-tree verification, a stopped/start-exclusion lease, non-overwriting atomic directory publication, sticky publication evidence and ownership-proven cleanup. The existing cooperating-directory foundation does not establish all of these guarantees against hostile concurrent filesystem mutation. Connecting a partial adapter would falsely advertise capabilities. This journal is a prerequisite that can be tested independently without granting those capabilities.

## Record and lifetime

`WindowsTransactionIntentJournal.CreateNew` validates the receipt before touching the filesystem, then uses `WindowsOwnedDirectory.CreateNew`. Existing roots are always rejected. Its fixed `transaction-intent.bin` is opened with native `CREATE_NEW`, `OPEN_REPARSE_POINT`, non-inherited handles and exclusive sharing. Root, reservation and journal volume/file IDs are bound into a versioned immutable ticket. Reservation and journal must be non-reparse regular files with exactly one link and no pending deletion.

The ticket is available before writing. It is inspection context only, not a secret, authentication token, durable acknowledgement or cleanup capability. A host would need to preserve its trusted association with the intended receipt before using it after a restart; this stage does not implement that host registry. Root/journal creation failure can leave uninspectable remnants; no absence or rollback is inferred and no automatic cleanup occurs.

`WriteAndFlush` writes one bounded canonical record (at most 4096 bytes) with a format marker, exact receipt/identity payload and SHA-256 digest. Successful return acknowledges the file's `Flush(true)` call. Cancellation before flush or a failed attempt never causes rewriting, truncation or automatic retry of uncertain bytes. An acknowledged write is idempotent; cancellation observed after flush does not revoke it. No namespace rename or final target publication occurs.

The bytes are currently split into two writes to expose a deterministic native interruption boundary to tests. Checkpoint callbacks are internal and test-only. Record format and expected bytes are reconstructed from the ticket; recovery does not deserialize untrusted journal content.

## Read-only reconciliation

`Inspect` uses only `OPEN_EXISTING` with read access. It pins the current directory chain, requires the recorded root and reservation identities, and holds the existing exclusive reservation for the entire inspection. It retains the journal handle, requires its recorded identity, performs a bounded exact-byte comparison, then revalidates identities. A live owner, missing object, changed identity/content, incomplete record, unsupported path/volume or sharing denial produces `Unknown`. Nothing is repaired, recreated, published or removed.

`ExactIntentPresent` means only that the current record and identities match. A process killed after its complete write but before `Flush(true)` can leave matching bytes in the OS cache. Therefore this observation **does not prove that flush succeeded before the crash**, that power-loss durability is guaranteed, that any artifact exists, or that an installation was committed. Neither observation maps to `TransactionObservation.NotStarted`, `OwnedStage` or `Published`.

Creation is exclusive only for that directory. The same install ID can be written under a different new root; a durable storage-wide install-ID registry and replay serialization remain unimplemented. Read-only inspection also does not verify the complete directory tree or all NTFS streams and does not protect against arbitrary same-user interference. The original create-to-pin interval and cooperative ownership limits still apply.

The ticket binds the three objects, not their original path or ancestor chain. The same objects moved together can still be observed at a new canonical path. This is not an anchor/baseline attestation for a target and must never be reused as write authority.

## Native evidence and remaining work

Fixtures use synthetic records and harmless .NET processes. Tests cover duplicate roots, live inspection exclusion, cancellation at four write boundaries, a real partial write followed by an injected exception, changed receipt/content, missing records, root/reservation/journal identity replacement, hardlinks, and abrupt process termination before writing, after a partial write, after a complete unacknowledged write, and after flush. Crash recovery checks exact bytes remain unchanged and never adopts the existing directory.

These are process-crash tests, not power-cut or storage-device failure tests. The writer requests [file-buffer flushing](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers); this does not establish a durable parent-directory registry or a final publication protocol. Link count and deletion state are checked through [FILE_STANDARD_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_standard_info).

Remaining adapter work includes handle-relative artifact traversal, exact staged-tree freezing/verification, a durable install-ID registry, non-replacing native directory publication and destination-race recovery, stopped-lease integration, and ownership-proven cleanup across crashes. Duplicate final destinations, publication cancellation, final-target recovery and stage cleanup are **unrun because this stage implements none of those operations**. There is no new UI, live-world access, server/MOD execution or network operation.
