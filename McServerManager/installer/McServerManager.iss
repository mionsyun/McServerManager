#define AppExe "..\\bin\\Release\\net8.0-windows\\win-x64\\publish\\McServerManager.exe"
#define AppVersion GetVersionNumbersString(AppExe)
#define AppIdRegistryValue "{D5F6E1C5-2A2D-4E9A-8B0E-9C0C9E6E5A2C}"

[Setup]
AppId={{D5F6E1C5-2A2D-4E9A-8B0E-9C0C9E6E5A2C}}
AppName=MaiPilot
AppVersion={#AppVersion}
AppPublisher=MaiPilot
DefaultDirName={commonpf}\MaiPilot
DefaultGroupName=MaiPilot
OutputDir=dist
OutputBaseFilename=MaiPilotSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\McServerManager.exe
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=yes
LanguageDetectionMethod=none

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.UpdateModeInfo=既存インストールを検出したため、アップデートとして実行します。設定とサーバーデータは保持されます。
japanese.UpdatePageTitle=アップデートモード
japanese.UpdatePageDescription=既存のインストールを検出しました
japanese.UpdatePageBodyWithVersion=インストール済みバージョン: %1%n新しいバージョン: %2%n%nアップデートとして実行します。設定とサーバーデータは保持されます。%n%nインストール先:%n%3
japanese.UpdatePageBodyWithoutVersion=既存インストールを検出しました。%n新しいバージョン: %1%n%nアップデートとして実行します。設定とサーバーデータは保持されます。%n%nインストール先:%n%2
japanese.UpdateReadyMemo=更新モード: 既存インストールを同じ場所に上書きします（設定/データは保持）。
japanese.UpdateVersionTransition=バージョン: %1 -> %2
english.UpdateModeInfo=An existing installation was detected. Setup will run in update mode and keep settings/server data.
english.UpdatePageTitle=Update Mode
english.UpdatePageDescription=An existing installation was detected
english.UpdatePageBodyWithVersion=Installed version: %1%nNew version: %2%n%nSetup will run in update mode and keep your settings/server data.%n%nInstall path:%n%3
english.UpdatePageBodyWithoutVersion=Existing installation detected.%nNew version: %1%n%nSetup will run in update mode and keep your settings/server data.%n%nInstall path:%n%2
english.UpdateReadyMemo=Update mode: existing install will be replaced in-place (settings/data kept).
english.UpdateVersionTransition=Version: %1 -> %2

[Files]
Source: "..\\bin\\Release\\net8.0-windows\\win-x64\\publish\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MaiPilot"; Filename: "{app}\McServerManager.exe"; IconFilename: "{app}\McServerManager.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\MaiPilot"; Filename: "{app}\McServerManager.exe"; IconFilename: "{app}\McServerManager.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}";

[Run]
Filename: "{app}\McServerManager.exe"; Description: "{cm:LaunchProgram,MaiPilot}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ExistingInstallPath: string;
  ExistingInstallDetected: Boolean;
  ExistingInstallVersion: string;
  OriginalSelectDirLabelCaption: string;
  UpdateModePage: TWizardPage;
  UpdateModeBodyLabel: TNewStaticText;

function TryGetExistingInstallPath(var InstallPath: string): Boolean;
var
  UninstallKey: string;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppIdRegistryValue}_is1';
  Result :=
    RegQueryStringValue(HKLM64, UninstallKey, 'Inno Setup: App Path', InstallPath) or
    RegQueryStringValue(HKLM32, UninstallKey, 'Inno Setup: App Path', InstallPath) or
    RegQueryStringValue(HKLM, UninstallKey, 'Inno Setup: App Path', InstallPath) or
    RegQueryStringValue(HKCU64, UninstallKey, 'Inno Setup: App Path', InstallPath) or
    RegQueryStringValue(HKCU32, UninstallKey, 'Inno Setup: App Path', InstallPath) or
    RegQueryStringValue(HKCU, UninstallKey, 'Inno Setup: App Path', InstallPath);
end;

function TryGetExistingInstallVersion(const InstallPath: string; var VersionText: string): Boolean;
var
  ExePath: string;
begin
  ExePath := AddBackslash(InstallPath) + 'McServerManager.exe';
  if not FileExists(ExePath) then
  begin
    Result := False;
    exit;
  end;

  Result := GetVersionNumbersString(ExePath, VersionText) and (VersionText <> '');
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  ExistingInstallDetected := TryGetExistingInstallPath(ExistingInstallPath);
  ExistingInstallVersion := '';
  if ExistingInstallDetected then
  begin
    TryGetExistingInstallVersion(ExistingInstallPath, ExistingInstallVersion);
  end;
end;

procedure InitializeWizard();
var
  updateBodyText: string;
begin
  OriginalSelectDirLabelCaption := WizardForm.SelectDirLabel.Caption;

  if ExistingInstallDetected and (ExistingInstallPath <> '') then
  begin
    WizardForm.DirEdit.Text := ExistingInstallPath;
  end;

  UpdateModePage := CreateCustomPage(
    wpWelcome,
    CustomMessage('UpdatePageTitle'),
    CustomMessage('UpdatePageDescription'));

  UpdateModeBodyLabel := TNewStaticText.Create(UpdateModePage);
  UpdateModeBodyLabel.Parent := UpdateModePage.Surface;
  UpdateModeBodyLabel.Left := 0;
  UpdateModeBodyLabel.Top := 0;
  UpdateModeBodyLabel.Width := UpdateModePage.SurfaceWidth;
  UpdateModeBodyLabel.Height := UpdateModePage.SurfaceHeight;
  UpdateModeBodyLabel.AutoSize := False;
  UpdateModeBodyLabel.WordWrap := True;

  if ExistingInstallVersion <> '' then
  begin
    updateBodyText := FmtMessage(CustomMessage('UpdatePageBodyWithVersion'), [ExistingInstallVersion, '{#AppVersion}', ExistingInstallPath]);
  end
  else
  begin
    updateBodyText := FmtMessage(CustomMessage('UpdatePageBodyWithoutVersion'), ['{#AppVersion}', ExistingInstallPath]);
  end;

  UpdateModeBodyLabel.Caption := updateBodyText;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if ExistingInstallDetected then
  begin
    if (PageID = wpSelectDir) or (PageID = wpSelectProgramGroup) then
    begin
      Result := True;
    end;
  end;

  if (UpdateModePage <> nil) and (PageID = UpdateModePage.ID) and (not ExistingInstallDetected) then
  begin
    Result := True;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  updateText: string;
begin
  if (CurPageID = wpSelectDir) and ExistingInstallDetected then
  begin
    WizardForm.SelectDirLabel.Caption :=
      OriginalSelectDirLabelCaption + #13#10#13#10 + CustomMessage('UpdateModeInfo');
  end
  else if (CurPageID = wpReady) and ExistingInstallDetected then
  begin
    updateText := CustomMessage('UpdateReadyMemo');
    if ExistingInstallVersion <> '' then
    begin
      updateText := updateText + #13#10 +
        FmtMessage(CustomMessage('UpdateVersionTransition'), [ExistingInstallVersion, '{#AppVersion}']);
    end;

    if Pos(updateText, WizardForm.ReadyMemo.Text) = 0 then
    begin
      WizardForm.ReadyMemo.Lines.Add('');
      WizardForm.ReadyMemo.Lines.Add(updateText);
    end;
  end
  else
  begin
    WizardForm.SelectDirLabel.Caption := OriginalSelectDirLabelCaption;
  end;
end;
