; Inno Setup installer script
; Build output must be in ..\dist\publish (see build.ps1)

#define MyAppName "Межпланетный маневр"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "VladAndMasha"
#define MyAppExeName "InterplanetaryManeuver.App.exe"
#define MyAppIconName "InterplanetaryManeuver.ico"

[Setup]
AppId={{7F3F1A8C-4BFA-49C5-9A43-8B0C9C61D5D3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=setup_{#MyAppVersion}_win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppIconName}
SetupLogging=yes
SetupIconFile=..\assets\branding\setup.ico
WizardImageFile=..\assets\branding\wizard.bmp
WizardSmallImageFile=..\assets\branding\wizard_small.bmp

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки"; Flags: unchecked

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\assets\branding\setup.ico"; DestDir: "{app}"; DestName: "{#MyAppIconName}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIconName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppIconName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent
