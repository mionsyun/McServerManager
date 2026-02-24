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

[Files]
Source: "..\\bin\\Release\\net8.0-windows\\win-x64\\publish\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MaiPilot"; Filename: "{app}\McServerManager.exe"; IconFilename: "{app}\icon.ico"; WorkingDir: "{app}"
Name: "{commondesktop}\MaiPilot"; Filename: "{app}\McServerManager.exe"; IconFilename: "{app}\icon.ico"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}";

[Run]
Filename: "{app}\McServerManager.exe"; Description: "{cm:LaunchProgram,MaiPilot}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
