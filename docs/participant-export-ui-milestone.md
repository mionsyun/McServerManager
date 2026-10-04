# Participant reference-list review and ZIP export

## Scope

This slice connects the existing free participant-list core to a WPF review flow.
It reads a user-selected explicit client-definition JSON, displays the declared
runtime and MOD list, requires acknowledgment of the verification limits, and
saves the complete reference ZIP to a new file selected by the user.

The ZIP contains only `README.txt`, `mods.txt`, and `manifest.json`: names, exact
declared versions, derived official Modrinth version-page URLs, and instructions.
There is no download, MOD binary, execution, launcher requirement, automatic
installation, or automatic sharing. The definition is not inferred from any
server. Both Free and Pro expose this same flow.

Input-format validation is not provider verification. Neither ID/version mapping,
dependency completeness, client support, nor runtime compatibility is established
by importing a file. The screen and archive keep those limitations visible.
An empty explicit list does not establish that participants need no MODs.

This is an import/review/export slice, not a beginner authoring wizard or a
verified recommendation catalog. Administrators still need to prepare an explicit
definition using the documented format. The example in that format document uses
synthetic IDs and is not a playable recommendation.

## File boundary

Inputs remain bounded to 256 KiB, and ZIP outputs to 2 MiB. Export writes a private
same-directory staging file and commits to a new destination without overwrite.
An existing filename must be changed rather than replacing another file. No
server folders or application settings are changed.

Filesystem-link checks reject observed symlink/reparse paths. Path checks are
not a hostile-process-proof filesystem lease: a different local process racing
directory replacement is outside this file-dialog export contract. Native
Windows filesystem behavior still needs the checks below.

## Windows continuation gates

1. Build and run both Free and Pro on Windows using an isolated application-data
   root. Verify the participant action can be reached without a server selected.
2. Use keyboard-only navigation and screen-reader names. Check dark/light themes,
   narrow supported window sizes, and 100%, 150%, and 200% DPI. Read the entire
   warning and a long 256-row review without clipped controls.
3. Cancel file selection, reject invalid/oversized JSON, import an empty explicit
   definition, and replace a reviewed definition. Old acknowledgment must not
   authorize a different definition.
4. Review a synthetic valid definition, acknowledge its limits, cancel the save
   dialog, retry, and save to a fresh temporary path. Inspect the three ZIP entries
   without launching their contents. Verify a preexisting destination is unchanged.
5. Exercise denied/read-only destinations, observed links/reparse paths, repeated
   clicks, cancellation, and close while work is active. Confirm status matches
   the committed file and no partial destination appears.

Cloud ViewModel/service tests and WPF cross-compilation do not replace these
native Windows interaction checks. No real-server start or gameplay is part of
this milestone. Production template Apply remains disabled.

## Automated verification (2026-10-04)

- The complete cross-platform suite passes **523 tests, 0 failures, 0 skips**
  for both Free and Pro build identities. These are the same 523 cases under
  two identities, not 1,046 distinct scenarios; 57 cases were added in this slice.
- Actual parser/export/file services are connected in a headless flow test using
  synthetic client definitions and isolated temporary directories. The written
  ZIP is opened and its exact entry set and unverified metadata are checked.
- Dedicated file tests exercise size boundaries, mutable input snapshots,
  cancellation before and after commit, raced destinations, partial writes,
  cleanup errors, and observed target/ancestor/dangling links.
- ViewModel tests cover invalid/empty input, explicit acknowledgment, changed
  selections, late results, repeated actions, cancellation, capability checks,
  and accurate save/cancel/failure reporting.
- Free and Pro WPF application plus existing Windows test project cross-builds
  pass with **0 errors**. The existing Open.NAT compatibility and
  ServerViewModel nullable warnings remain. The Windows test project was
  compiled, not executed.
- XAML parses and resource references resolve by source inspection. Rendering,
  native dialogs, accessibility interaction, DPI, and actual Windows filesystem
  behavior were not executed in this cloud environment.
- An independent headless harness exercised the actual window code-behind with
  fake WPF/dialog surfaces: **9 lifecycle probes passed**, including repeated
  save, picker cancellation, pre/post-commit cancel and close, cleanup failure
  during close, replacement read failure, and canceling a retry after a prior
  successful save. This establishes handler logic only, not native UI behavior.

Reproduce with .NET 8:

    dotnet test McServerManager.TemplateTests/McServerManager.TemplateTests.csproj -c Release -p:MaiPilotEdition=Free
    dotnet test McServerManager.TemplateTests/McServerManager.TemplateTests.csproj -c Release -p:MaiPilotEdition=Pro
    dotnet build McServerManager.Tests/McServerManager.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:MaiPilotEdition=Free
    dotnet build McServerManager.Tests/McServerManager.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:MaiPilotEdition=Pro
