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
  OriginalSelectDirLabelCaption: string;

function TryGetExistingInstallPath(var InstallPath: string): Boolean;
var
  UninstallKey: string;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1';
  Result :=
    RegQueryStringValue(HKLM64, UninstallKey, 'Inno Setup: App Path', InstallPath) or
    RegQueryStringValue(HKLM, UninstallKey, 'Inno Setup: App Path', InstallPath);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  ExistingInstallDetected := TryGetExistingInstallPath(ExistingInstallPath);
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
  if ExistingInstallDetected and (ExistingInstallPath <> '') then
  begin
    WizardForm.DirEdit.Text := ExistingInstallPath;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectDir) and ExistingInstallDetected then
  begin
    WizardForm.SelectDirLabel.Caption :=
      OriginalSelectDirLabelCaption + #13#10#13#10 + CustomMessage('UpdateModeInfo');
  end
  else
  begin
    WizardForm.SelectDirLabel.Caption := OriginalSelectDirLabelCaption;
  end;
end;
