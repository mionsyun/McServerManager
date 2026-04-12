# QA Summary (2026-04-12)

## Scope
- Security hardening validation and regression checks
- Unit tests, E2E flow tests, version matrix tests
- Live upstream availability tests
- Live server jar download smoke tests (including Forge)

## Test Runs
1. Non-live unit/smoke group
   - Command: `dotnet test McServerManager.Tests/McServerManager.Tests.csproj --no-build --filter "...unit/smoke classes..."`
   - Result: Passed 42 / Failed 0 / Skipped 0

2. Non-live E2E group (major flow + mod/plugin + version matrix)
   - Command: `dotnet test McServerManager.Tests/McServerManager.Tests.csproj --no-build --filter "...AddonManagementE2ETests|ServerAddonViewModelE2ETests|ServerFlowE2ETests|ServerVersionMatrixE2ETests"`
   - Result: Passed 46 / Failed 0 / Skipped 0

3. Live upstream version availability matrix
   - Env: `MCSM_ENABLE_LIVE_TESTS=1`
   - Command: `dotnet test ... --no-build --filter "FullyQualifiedName~LiveVersionAvailabilityTests"`
   - Result: Passed 41 / Failed 0 / Skipped 0

4. Live server-jar download smoke (Vanilla/Paper/Purpur/Fabric/Spigot/Forge)
   - Env: `MCSM_ENABLE_LIVE_TESTS=1`, `MCSM_ENABLE_LIVE_DOWNLOAD_TESTS=1`, `MCSM_ENABLE_LIVE_FORGE_DOWNLOAD=1`
   - Command: `dotnet test ... --no-build --filter "FullyQualifiedName~LiveServerJarDownloadSmokeTests"`
   - Result: Passed 17 / Failed 0 / Skipped 0

## Total
- Passed: 146
- Failed: 0
- Skipped: 0

## Notes
- `dotnet test` full one-shot on solution was unstable in this environment, so verification was executed in deterministic grouped runs.
- NuGet warning `NU1701` for `Open.NAT 2.1.0` remains (existing warning).

## Cleanup
- Removed generated artifacts:
  - `McServerManager.Tests/TestResults/*`
  - `%TEMP%/McServerManagerTests/*`
- Confirmed no residual temporary test directories.
