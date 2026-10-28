# Live Modrinth / Fabric inspection

The Java import page now has an explicit read-only MOD inspection panel.
Selecting local JARs sends their SHA-512 hashes to Modrinth to establish exact
file origin. Entering fixed Modrinth version IDs fetches the official version
and project metadata, then the exact API-selected file. Requests do not accept
user-provided download URLs. SHA-512, supplied SHA-1, actual size, and optional
manifest/pin assertions must agree; SHA-256 is computed from the same bytes
that the metadata reader inspects.

The result combines both evidence sources: API dependency edges and actual
`fabric.mod.json` declarations. MOD IDs are not treated as project IDs. A
project-only API edge needs an explicit version pin or an identified selected/
installed file. A JAR-only MOD ID that the inspected set cannot supply remains
unresolved; file names, display names and search ordering are never guesses.
An explicit additional selected version can provide independently inspected
metadata, but an unused dependency pin is not automatically installed or made
active just because it was entered.

## What is checked

- Required API dependencies, optional suggestions, incompatible and embedded
  declarations; pinned transitive acquisition without an implicit latest choice
- Real local files identified through Modrinth's file-hash endpoint; simulated
  installed sets are read-only and are distinguished from newly selected files
- Fabric `depends`, `recommends`, `suggests`, `breaks`, `conflicts`, `provides`,
  environment and declared nested JARs
- Default Fabric directory-discovery rules: hidden/dot-prefixed files and non-literal lowercase `.jar` root names cannot supply installed dependencies
- Dedicated-server applicability, including exclusion of descendants beneath a
  client-only nested parent
- Fabric-specific exact/comparator/wildcard/caret/tilde predicates, AND terms and
  OR arrays; unknown syntax blocks the relevant compatibility conclusion
- Duplicate identities, unresolved bindings, changed pins, unsupported nested
  alternatives and conflicting platform requirements

Only confirmed declarations are checked. No MOD is executed, installed, or
loaded into Java, and no server is started. Matching metadata does not establish
runtime binary compatibility, absence of malware, correctness of author
metadata, or successful multiplayer behavior. Existing installation files are
not removed, updated or overwritten. Normal creation and backup/control flows
are not changed.

## Deliberate unsupported cases

This phase supports Modrinth and Fabric schema-1 archives. CurseForge remains
disabled. Forge/NeoForge/Quilt and plugin dependencies do not use Fabric rules.
Full Fabric candidate/SAT selection is not implemented. A bundle with multiple
alternative nested providers of the same ID is explicitly unsupported rather
than silently selecting one. Minecraft snapshots and nonstandard target-version
normalization are not guessed. ZIP64, encrypted, multidisk or ambiguous ZIP
structures, including STORED entries with data descriptors, are rejected even
if a different runtime might accept them. Target versions are explicit inputs
compared against declarations; this service does not install runtimes or certify
that every user-entered numeric runtime version exists.

## Resource and network policy

| Layer | Application-owned limit |
|---|---:|
| API JSON response | 4 MiB |
| One acquired/local file in live pipeline | 64 MiB |
| Acquired bytes per live inspection | 256 MiB |
| Artifacts / API depth / declarations | 32 / 12 / 4096 |
| Request timeout / overall inspection | 10 seconds / 3 minutes |
| Standalone archive reader | 512 MiB |
| Cumulative ZIP entries | 16384 |
| Metadata entry / cumulative metadata | 512 KiB / 4 MiB |
| Nested depth / cumulative nested bytes | 2 / 64 MiB |
| Compression ratio | 200:1 maximum |
| Metadata JSON nesting | 16 |

The live acquisition limits deliberately narrow the earlier portable manifest
limits. A template or API response cannot raise these limits. Anonymous requests
use official HTTPS endpoints, no redirects, cookies, default credentials,
mirrors or embedded metadata URLs. No persistent file cache is created by the
inspection service. Unsupported or failed acquisition produces a blocker.

## Tests and real-file checks

Offline deterministic tests cover transport errors, malformed API data,
identity/hash failures, archive adversarial cases, predicate semantics,
cancellation and UI stale-result suppression. The opt-in
`McServerManager.LiveChecks` runner uses the same production services for real
API/CDN/JAR checks and records expected findings, exact artifact IDs, sizes and
hashes. The public example input includes only explicit public version IDs;
local installed-file comparisons are supplied separately by the operator.

The independent expectations were derived from separate official API requests
and Python standard-library archive reads, not by invoking the production
parser. Original third-party binaries are temporary test inputs only and are
not included in source, reports or attachments. See the dated verification
report for the exact tested versions and remaining limitations.

## Primary references

- https://docs.modrinth.com/api/operations/getversion/
- https://docs.modrinth.com/api/operations/getproject/
- https://docs.modrinth.com/api/operations/versionfromhash/
- https://docs.fabricmc.net/develop/loader/fabric-mod-json
- https://wiki.fabricmc.net/documentation:fabric_mod_json_spec
- FabricMC/fabric-loader: VersionPredicateParser, VersionComparisonOperator and
  SemanticVersionImpl, reviewed against the published loader implementation
