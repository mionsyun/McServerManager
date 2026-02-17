#define AppExe "..\\bin\\Release\\net8.0-windows\\win-x64\\publish\\McServerManager.exe"
#define AppVersion GetVersionNumbersString(AppExe)

[Setup]
AppId={{D5F6E1C5-2A2D-4E9A-8B0E-9C0C9E6E5A2C}}
AppName=MC Server Manager
AppVersion={#AppVersion}
AppPublisher=MC Server Manager
DefaultDirName={commonpf}\MC Server Manager
DefaultGroupName=MC Server Manager
OutputDir=dist
OutputBaseFilename=BlockPilotSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\icon.ico
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\\bin\\Release\\net8.0-windows\\win-x64\\publish\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MC Server Manager"; Filename: "{app}\McServerManager.exe"; IconFilename: "{app}\icon.ico"; WorkingDir: "{app}"
Name: "{commondesktop}\MC Server Manager"; Filename: "{app}\McServerManager.exe"; IconFilename: "{app}\icon.ico"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Additional tasks";

[Run]
Filename: "{app}\McServerManager.exe"; Description: "Launch MC Server Manager"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
