# Explicit live MOD declaration checks

This opt-in console runner uses the same provider, file/hash verification,
bounded JAR reader, and dependency inspection service as the WPF preview.
It makes anonymous requests to Modrinth and reads/downloads real MOD files.
It never executes MODs or starts Minecraft. It is not part of the offline unit
suite, and no live check runs without the explicit `--live` argument.

Run after reviewing the case file and agreeing to its public downloads:

    dotnet run --project McServerManager.LiveChecks -c Release -- --live cases.json report.json

Case JSON is an array of objects with `Name`, `Request`, `ExpectedResolved`, and
optional `ExpectedFindingCodes`, exact `ExpectedFindings` (code + dependency),
`ExpectedVersionIds`, and `ExpectedInstalledCount`. Negative cases should require
the expected finding so an unrelated network error cannot count as success. Request uses the production
`ModrinthInspectionRequest` contract. Use exact version IDs; do not put network
URLs in the input. Installed-file paths refer only to read-only, explicitly
selected local files. A temporary directory of downloaded official files may
simulate installed state; it is not the user's actual server installation.

The output removes local directory names and records pinned IDs, actual file
hashes, byte sizes, metadata declarations, findings, timestamps and expected
outcomes. It does not contain MOD binaries. Keep third-party artifacts out of
the repository and public attachments. API availability can change; a network
failure is a failed/inconclusive check, never evidence of compatibility.

`magic-technology-cases.json` adds 15 pinned magic/technology inputs. The
[2026-10-04 report](../docs/magic-technology-verification-2026-10-04.md) clearly
separates initial live acquisition from final offline replay after corrections.
Running these inputs with `--live` makes new network requests; it is not the
recorded replay. Spectrum remains an API-size rejection, not a downloaded JAR.
