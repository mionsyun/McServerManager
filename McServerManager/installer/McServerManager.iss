#define AppExe "..\\bin\\Release\\net8.0-windows\\win-x64\\publish\\McServerManager.exe"
#define AppVersion GetVersionNumbersString(AppExe)

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
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=yes
LanguageDetectionMethod=none

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.AlreadyInstalledBody=MaiPilot は既にインストールされています。%n%nインストール先:%n%1%n%nアップデート（上書き）を続行しますか？
japanese.UpdateModeInfo=既存インストールを検出したため、アップデートとして実行します。設定とサーバーデータは保持されます。
english.AlreadyInstalledBody=MaiPilot is already installed.%n%nInstall path:%n%1%n%nDo you want to continue with an in-place update?
english.UpdateModeInfo=An existing installation was detected. Setup will run in update mode and keep settings/server data.

[Files]
Source: "..\\bin\\Release\\net8.0-windows\\win-x64\\publish\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MaiPilot"; Filename: "{app}\McServerManager.exe"; IconFilename: "{app}\icon.ico"; WorkingDir: "{app}"
Name: "{commondesktop}\MaiPilot"; Filename: "{app}\McServerManager.exe"; IconFilename: "{app}\icon.ico"; WorkingDir: "{app}"; Tasks: desktopicon

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
  OriginalWelcomeLabel1Caption: string;
  OriginalWelcomeLabel2Caption: string;

function TryGetExistingInstallPath(var InstallPath: string): Boolean;
var
  UninstallKey: string;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1';
  Result :=
    RegQueryStringValue(HKLM64, UninstallKey, 'Inno Setup: App Path', InstallPath) or
    RegQueryStringValue(HKLM, UninstallKey, 'Inno Setup: App Path', InstallPath) or
    RegQueryStringValue(HKCU64, UninstallKey, 'Inno Setup: App Path', InstallPath) or
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

  if ExistingInstallDetected and (not WizardSilent()) then
  begin
    Result := MsgBox(
      FmtMessage(CustomMessage('AlreadyInstalledBody'), [ExistingInstallPath]),
      mbConfirmation,
      MB_YESNO) = IDYES;
  end;
end;

procedure InitializeWizard();
begin
  OriginalSelectDirLabelCaption := WizardForm.SelectDirLabel.Caption;
  OriginalWelcomeLabel1Caption := WizardForm.WelcomeLabel1.Caption;
  OriginalWelcomeLabel2Caption := WizardForm.WelcomeLabel2.Caption;

  if ExistingInstallDetected and (ExistingInstallPath <> '') then
  begin
    WizardForm.DirEdit.Text := ExistingInstallPath;
  end;
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
end;

procedure CurPageChanged(CurPageID: Integer);
var
  updateText: string;
begin
  if (CurPageID = wpWelcome) then
  begin
    if ExistingInstallDetected then
    begin
      WizardForm.WelcomeLabel1.Caption := 'MaiPilot Update';
      if ExistingInstallVersion <> '' then
      begin
        WizardForm.WelcomeLabel2.Caption :=
          'Installed version: ' + ExistingInstallVersion + #13#10 +
          'New version: {#AppVersion}' + #13#10#13#10 +
          'Setup will run in update mode and keep your settings/server data.';
      end
      else
      begin
        WizardForm.WelcomeLabel2.Caption :=
          'Existing installation detected.' + #13#10 +
          'New version: {#AppVersion}' + #13#10#13#10 +
          'Setup will run in update mode and keep your settings/server data.';
      end;
    end
    else
    begin
      WizardForm.WelcomeLabel1.Caption := OriginalWelcomeLabel1Caption;
      WizardForm.WelcomeLabel2.Caption := OriginalWelcomeLabel2Caption;
    end;
  end;

  if (CurPageID = wpSelectDir) and ExistingInstallDetected then
  begin
    WizardForm.SelectDirLabel.Caption :=
      OriginalSelectDirLabelCaption + #13#10#13#10 + CustomMessage('UpdateModeInfo');
  end
  else if (CurPageID = wpReady) and ExistingInstallDetected then
  begin
    updateText := 'Update mode: existing install will be replaced in-place (settings/data kept).';
    if ExistingInstallVersion <> '' then
    begin
      updateText := updateText + #13#10 + 'Version: ' + ExistingInstallVersion + ' -> {#AppVersion}';
    end;

    WizardForm.ReadyMemo.Lines.Add('');
    WizardForm.ReadyMemo.Lines.Add(updateText);
  end
  else
  begin
    WizardForm.SelectDirLabel.Caption := OriginalSelectDirLabelCaption;
  end;
end;
