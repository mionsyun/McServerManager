Release Checklist
=================

1) Update version
   - Edit `McServerManager.csproj` `<Version>` and related fields.
   - Update `CHANGELOG.md`.

2) Build and package
   - Run `installer\\build.ps1` (or `dotnet publish` + ISCC manually).

3) Code signing (optional but recommended for distribution)
   - Install Windows SDK signtool.
   - Set environment variables:
     - `SIGNTOOL_PATH` (e.g. `C:\Program Files (x86)\Windows Kits\10\bin\10.0.x.x\x64\signtool.exe`)
     - `CODESIGN_PFX` (path to your .pfx)
     - `CODESIGN_PASSWORD`
     - `CODESIGN_TIMESTAMP_URL` (optional)

4) Verify package contents
   - `THIRD_PARTY_NOTICES.txt` is present in the install directory.
   - `icon.ico` is present in the publish output.

5) Smoke test
   - Install the new `installer\\dist\\MaiPilotSetup.exe`
   - Launch from Start Menu and taskbar.
   - Start/stop a sample server.
