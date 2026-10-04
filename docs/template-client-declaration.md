# Explicit participant definition inside a template

## Contract

Schema `1.0` now accepts an optional root `clientDefinition` object. Its fields
are exactly the existing [participant client-definition format](participant-list-format.md),
including its own `schemaVersion: "1.0"` and `definitionKind: "client"`.
The server `runtime`, `settings`, and `addons` retain their existing meanings.

- An absent declaration is accepted. It means **not specified**, not an empty
  or automatically complete client pack. Participant export from that template
  remains unavailable with a visible explanation.
- An explicit `null`, malformed declaration, duplicate/unknown field, unsupported
  provider, or exceeded client bound invalidates the template. There is no
  partial fallback to the server MOD list.
- The declared client Minecraft version must exactly equal the server Minecraft
  version. This is a local consistency check, not runtime verification. Loader
  names and versions are not inferred or forced to match: a separately declared
  Fabric client can accompany a vanilla/Paper server. Whether it works remains
  unverified.
- Server and client MOD sets are independent. Neither missing dependencies nor
  client requirements are guessed from server artifacts, filenames, names, or
  provider project IDs.
- The original complete template bytes retain their SHA-256. The exact embedded
  client-object JSON bytes have a separate SHA-256 in the opaque validated client
  definition. The review and ZIP use that definition without rewriting its JSON.
- Existing templates without the optional field still load. Older application
  versions whose strict parser predates this field will reject new templates
  containing it; this is not forward compatibility with those old readers.

The whole template remains limited to 1 MiB and JSON depth 16. The embedded
client object independently retains its 256 KiB, depth 8, 256-MOD and field-size
bounds. Unknown fields remain rejected. No data-provided URLs, commands, files,
download instructions, or relaxed limits are introduced.

## User flow

The usual entry reads a `.maipilot-template.json`, checks its local format, and
opens the declared participant requirements for review. The user still reviews
the fixed versions and acknowledges that provider identity, dependency
completeness, client support, and runtime compatibility are unverified before
choosing a new ZIP destination. Manual standalone client JSON import remains an
advanced option.

Changing, canceling, or failing a template inspection clears the previous client
declaration and its authorization to proceed. A late result cannot restore an
older definition. The direct handoff retains the original template and client
definition digests for review. Neither template validation nor the handoff
enables production template Apply, a MOD download, execution, or sharing.

This slice does not add a maintained preset catalog or a verified playable
example. Authors must still explicitly declare the client requirements. The
advanced authoring GUI is a separate next stage.

## Native Windows continuation

Run both editions and check both entry routes: the participant action in the main
window and the participant action after template inspection in the server wizard.
Check missing declarations, malformed embedded definitions, explicit empty lists,
manual advanced import, switching files after acknowledgment, picker cancellation,
closing during read or save, repeated clicks, and persistent cleanup warnings.
Then repeat the existing [ZIP save, DPI, keyboard, and filesystem gates](participant-export-ui-milestone.md).
Cloud tests and cross-compilation do not establish native Windows interaction.

## Automated checks (2026-10-04)

- Free and Pro each pass the complete **594-case** cross-platform suite with
  zero failures and skips. These are the same cases under two build identities;
  this slice adds 71 cases to the previous 523.
- Tests cover old templates, strict embedded fields and limits, server/client
  list separation, exact whole/subtree digests, independent loader declarations,
  Minecraft mismatch, opaque handoff, stale results, cancellation, malformed
  provenance digests, and acknowledgment reset after changing inputs.
- A real-service headless flow reads a synthetic template from an isolated
  directory, inspects it, hands off the opaque declaration, acknowledges the
  limitations, saves the new ZIP, and reopens its metadata. Original template
  bytes are unchanged. No real MOD or server is downloaded or executed.
- Both editions of the WPF app and existing Windows test project cross-compile
  with zero errors. Existing Open.NAT compatibility and ServerViewModel nullable
  warnings remain; Windows tests and native windows were not executed.
- XML/resource source checks pass for both changed windows. Actual DPI,
  accessibility, file-picker behavior and Windows filesystem semantics remain
  the native continuation gates above.
- An independent harness linked the actual two window code-behinds and review
  ViewModels to fake WPF/dialog surfaces and a minimal wizard shell. All **28
  headless lifecycle probes passed**, including pre-parser cancellation,
  leave/re-enter navigation, changed data context, stale completion after close,
  dismissed primary selection after previous approval, absent/invalid definitions,
  direct handoff, exact digests, and the prior ZIP publication/cleanup scenarios.
  This is source-handler verification, not native Windows UI execution.

Use the same .NET 8 test/build commands in the preceding
[participant UI report](participant-export-ui-milestone.md#automated-verification-2026-10-04).
