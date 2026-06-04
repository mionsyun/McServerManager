Release Checklist
=================

Version: 2.0
Release Date: 2026-06-04

1) Update version
   - McServerManager version updated to 2.0.
   - Update CHANGELOG.md if needed.

2) Build and package
   - Run `installer\\build.ps1` (or `dotnet publish` + ISCC manually).
   - Confirm both installers exist:
     - `installer\\dist\\MaiPilotSetup.exe`
     - `installer\\dist\\MaiPilotSetup-2.0.exe`
   - Confirm `installer\\dist\\update.json` is generated with version 2.0.

3) Code signing (optional but recommended for distribution)
   - Install Windows SDK signtool.
   - Configure signing environment variables.

4) Verify package contents
   - `THIRD_PARTY_NOTICES.txt` is present in the install directory.
   - `icon.ico` is present in the publish output.

5) Smoke test
   - Install the new package.
   - Launch from Start Menu and taskbar.
   - Start and stop a sample server.

6) Publish update artifacts
   - Upload `MaiPilotSetup-2.0.exe`.
   - Publish matching update metadata (version, URL, sha256).
