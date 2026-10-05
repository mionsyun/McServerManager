# Explicit Vanilla runtime-file verification

The template-import screen now has a separate, user-triggered **Vanilla 本体ファイルの確認** action. Selecting a template does not start this download. Before the button, the UI explains that it will fetch an official server JAR of at most 128 MiB plus metadata, without writing or executing it. Metadata stages, the declared JAR size, transferred bytes and cancellation are shown.

This is a real HTTP/streaming implementation behind `IVanillaRuntimeInspectionService`, not a simulated installer or a success-only UI. It remains independent of WPF and production transaction storage. The existing manual server downloader is unchanged. The output is a bounded immutable record of the observed exact version, pinned URL, size, official SHA-1 and locally computed SHA-256; a future creation pipeline must bind its own approved plan and re-fetch/revalidate the exact artifact before writing. This record is not installation, ownership, publication, cleanup, or durable-recovery authority.

## Validation chain

1. Accept only a bounded, exact numeric release ID. No `latest`, snapshots, ranges, inferred alternative or fallback version.
2. Fetch `https://piston-meta.mojang.com/mc/game/version_manifest_v2.json`. Select exactly one matching release entry, requiring its SHA-1 and exact content-addressed official version-manifest URL.
3. Fetch that bounded manifest, verify its SHA-1 against the index, and require its ID/type to match the selected release.
4. Require the server download entry's positive size, SHA-1 and exact HTTPS pinned-object path. Only the explicitly allowed official artifact hosts are accepted. Reject credentials, custom ports, query/fragment, aliases, dot segments, encoded paths and lookalike hosts.
5. Stream the JAR through size and SHA-1 checks while calculating SHA-256. The body is not retained, saved, unpacked or executed. Read at most the declared length plus one sentinel byte; reject short, extra or different bytes.

Each metadata response is limited to 4 MiB. The server JAR is limited to 128 MiB and read in 64 KiB buffers. Relevant JSON structure, duplicate properties/version matches, response status, redirects, content encoding, content range and content length are checked. A request has bounded cancellation/timeout handling. No retry, mirror, authentication, certificate override or alternate-version fallback is attempted.

Metadata requests have a 10-second deadline each; the JAR request/body has a two-minute deadline. Timeout and unavailable results carry no verification evidence.

The transport honors the configured system/environment proxy without adding credentials or changing settings. Redirects, cookies and decompression are disabled. TLS validation remains enabled. Progress callback failure does not produce a verified result.

Official host evidence: Minecraft's [1.21.1 release announcement](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21-1) links its server download on `piston-data.mojang.com`; the [1.18.2 release announcement](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-18-2) links the pinned legacy `launcher.mojang.com` host. A host allowance does not establish that an arbitrary URL is trusted: the exact hash/path checks still apply.

## Selection and UI boundaries

The active operation captures an immutable template SHA-256 and runtime version. Replacing the template, failed replacement, route navigation, DataContext replacement or window closure cancels and invalidates the old operation. Late success, failure and progress cannot restore it. Repeated clicks do not start a second operation while busy; explicit cancellation permits a fresh attempt. No earlier evidence is retained after a failed recheck.

The success label says **size and SHA-1 match official metadata; SHA-256 was calculated locally**. It does not imply an official SHA-256 comparison. Addon inspection, dependency completeness, Java suitability, EULA confirmation, startup and runtime compatibility remain separate. A successful runtime check never enables `ProductionTemplateApply` in Free or Pro and never fills or launches the manual creation wizard.

## Remaining work

Actual template-based creation still needs supported runtime/addon acquisition wired to approved plans, safe native stage publication and recovery, then Windows UI and real launch/connection verification. Existing-server capture remains gated. This slice implements the read-only fixed-runtime verification prerequisite; successful live-download confirmation and the full Pro workflow remain open.

## Verification on 2026-10-05

- Linux/.NET SDK 8.0.415: full Free and Pro suites each passed **999/999**, zero failures/skips; 136 runtime tests were added (113 HTTP/streaming cases and 23 UI-model cases)
- Both editions' application, Windows test project and ownership child fixture cross-built with zero errors and nine existing Open.NAT/nullable warnings each
- Independent review found no remaining blockers; its frozen-copy runs passed all 136 runtime tests plus a real ten-second stalled-transport deadline probe in each edition
- These tests use synthetic HTTP payloads and do not prove a live Mojang JAR download or rendered Windows UI

One actual-service network probe requested Vanilla 1.21.1 at 13:05:03 UTC and returned `Unavailable / RequestTimeout` at 13:05:13 UTC while fetching the version index. It produced no evidence and did not reach version metadata or JAR transfer. That **is not a live verification pass**. The [probe record](vanilla-runtime-live-probe-2026-10-05.json) preserves the observed result; no live JAR success is included in the validation totals. Official release pages were readable and support the allowed artifact hosts independently of this blocked download.

Reproduce offline checks with `dotnet test McServerManager.TemplateTests/McServerManager.TemplateTests.csproj -c Release -p:MaiPilotEdition=Free`, then `Pro`. Cross-build with `dotnet build McServerManager.Tests/McServerManager.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:MaiPilotEdition=Free -m:1`, then `Pro`.
