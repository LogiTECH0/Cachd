
#define MyAppName "Cachd"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Cachd"
#define MyAppExeName "Cachd.Browser.exe"

[Setup]
AppId={{A71C7D31-9E2B-4D7F-8C32-1B6A0E5F9A41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

DefaultDirName={autopf}\Cachd
DefaultGroupName=Cachd

OutputDir=..\dist
OutputBaseFilename=Cachd-Setup-{#MyAppVersion}

ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64

PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Створити ярлик на робочому столі"; GroupDescription: "Додаткові параметри:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Cachd"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Cachd"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустити Cachd"; Flags: postinstall nowait skipifsilent
