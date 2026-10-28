# Pro template declaration authoring

## Bounded scope

This stage adds a form-based editor for portable declarations. It does not
capture an existing server, inspect a running process, scan server directories,
copy worlds, collect settings/secrets, fetch a MOD catalog, or install anything.
Creating a declaration is not creating a runnable server.

Server runtime/settings and participant requirements are separate form sections.
The existing strict manifest and client-definition validators remain the final
authority. Runtime versions/builds and MOD references are exact declarations,
not verified recommendations. There is no guessed latest version or generated
real-MOD preset.

New templates start with no server addons. Imported server addon declarations
are preserved read-only, including fixed sources, hashes, sizes and dependency
references. Adding/removing server addons and retargeting the server runtime of
a template containing addons are outside this slice. The latter is rejected
rather than silently reusing old MOD pins against a different runtime.

An optional participant definition is explicitly authored independently of the
server addon list. Disabling it means absent, not a complete empty pack. An
explicit empty client list remains visibly incomplete/unverified. Client MOD
references do not become server addons.

## Edition and validation boundary

Create, edit and template export use the central Pro capabilities in the service
as well as the interface. Direct calls must fail for Free. Free template use,
import, inspection, and participant-reference ZIP export remain available.
This is a build-time edition boundary, not billing or license authentication.

The editor loads a strict validated source into a session. Validation prepares
an immutable output tied to that session and the exact current draft. Changes
to a field, nested client row, or list invalidate the prior preview and
acknowledgment. Export rechecks the session and draft instead of trusting a
visible button state. Output bytes are copied defensively.

New declarations receive a new template ID and revision 1. Editing increments
the source revision, preserving its identity and leaving the original file
untouched. Overflow or invalid input blocks export. The whole manifest retains
the 1 MiB bound; its optional client declaration retains its own 256 KiB/depth/
row limits. Strict unknown-field rejection is unchanged.

Successful validation establishes input format and local consistency only.
Provider identity, available artifacts, dependency completeness, loader/client
compatibility and actual gameplay are unverified. The editor does not enable
production template Apply for either edition.

## File and interruption behavior

Saving uses a new `.maipilot-template.json` path only. The file boundary checks
Pro export authority, snapshots and strictly revalidates bytes, stages the
complete file, rechecks authority before no-overwrite commit, and preserves any
preexisting destination. Observed links/reparse paths are rejected. This is the
same tested cooperative-filesystem boundary as participant ZIP export, not a
hostile-process-proof directory lease.

The UI handles unsaved edits when starting over, loading another file, or
closing. During I/O it guards repeated actions and editing. Cancellation before
commit removes only owned staging; successful commit remains successful if
cancellation arrives afterward. A failed cleanup shows the remaining temporary
path rather than silently claiming a clean rollback.

## Native Windows continuation

1. Run Free and Pro with isolated application data. Check Free direct capability
   denial and existing free template/participant flows; no settings migration is
   required for the authoring window.
2. In Pro, create a synthetic declaration for each supported runtime family,
   enter exact pins, validate, acknowledge, and save to a new file. Reimport the
   written declaration using ordinary Free inspection.
3. Edit a source with server addons. Verify addon values are preserved and server
   retargeting is blocked. Change ordinary settings and explicit client rows,
   including duplicate IDs and mismatched Minecraft versions, and check errors.
4. Change every field/row after validation. Previous approval must stop authorizing
   export. Exercise empty/absent client sets and the participant-guide flow.
5. Test New/Load/Close with unsaved changes, cancel both dialogs, repeated clicks,
   read/save cancellation, read-only destinations, existing filenames, links,
   cleanup failures and close near the commit point.
6. Verify keyboard navigation, screen-reader names, dark/light themes, narrow
   windows, long lists, and 100/150/200% DPI. Cloud tests and cross-compilation
   are not native Windows visual or filesystem verification.

## Automated verification (2026-10-04)

- The complete cross-platform suite passes **665 tests, 0 failures, 0 skips**
  in both Free and Pro builds. These are the same 665 cases under two identities,
  with 71 cases added in this authoring stage.
- Service tests cover direct Free denial, changed capabilities, opaque ownership,
  all four declared server families, exact addon preservation, locked runtime
  pins, explicit client separation, stale receipts, defensive copies, revision
  overflow, invalid Unicode, unknown fields, bounded output and row enumeration.
- ViewModel tests cover all field/row invalidation, acknowledgments, dirty
  replacement guards, fixed runtime locks, missing/empty client definitions,
  cancellation/error messages, stale save reports and post-commit capability
  changes. A successful save rebases from the exact committed bytes; a later
  edit exports a new revision instead of reusing a revision with changed content.
- A synthetic real-service flow authors through the Pro model, writes a new
  template, reimports it through Free inspection, and produces a Free participant
  reference ZIP. It checks that server addons are not inferred from client rows
  and that original files remain unchanged.
- File tests cover direct Free denial before I/O, strict final revalidation,
  extension/size boundaries, existing-file preservation, mutable caller bytes,
  cancellation and authority loss during staging, and cancellation after commit.
  The prior participant ZIP regression suite also passes against the shared
  no-overwrite file boundary.
- Free and Pro WPF app plus existing Windows test project cross-builds pass with
  **0 errors**. Existing Open.NAT compatibility and ServerViewModel nullable
  warnings remain. XML/resource/handler source checks pass. Native Windows tests
  and visual interactions were not executed.
- An independent isolated harness used the actual authoring window code-behind,
  production ViewModel and core services with fake WPF/dialog surfaces. All
  **35 headless lifecycle probes passed**, including dirty replacement/close
  choices, canceled/invalid imports, pre/post-commit cancellation and close,
  cleanup warnings, stale saves, revision rebasing/overflow, post-commit
  capability loss, Free direct handlers, locked addons and nested edits.
  These are handler/state checks, not native Windows interaction tests.

Run with .NET 8:

    dotnet test McServerManager.TemplateTests/McServerManager.TemplateTests.csproj -c Release -p:MaiPilotEdition=Free
    dotnet test McServerManager.TemplateTests/McServerManager.TemplateTests.csproj -c Release -p:MaiPilotEdition=Pro
    dotnet build McServerManager.Tests/McServerManager.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:MaiPilotEdition=Free
    dotnet build McServerManager.Tests/McServerManager.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:MaiPilotEdition=Pro
