; Magic Mouse Enhancer — Inno Setup script
; Build:  ISCC.exe setup.iss
; Per-user install: no admin rights needed, installs to %LOCALAPPDATA%\Programs\MagicMouseEnhancer
; (Inno 6.2+: {autopf} resolves to the per-user Program Files when PrivilegesRequired=lowest)

#define MyAppName "Magic Mouse Enhancer"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "Saulat Baig"
#define MyAppExeName "MagicMouseEnhancer.exe"

[Setup]
AppId={{B7C0A9E1-4D2F-4E8A-9C3B-6F5D8A1E2B44}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\MagicMouseEnhancer
DefaultGroupName=Magic Mouse Enhancer
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=MagicMouseEnhancer-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Enables full Apple Magic Mouse functionality on Windows
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\MagicMouseEnhancer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\install-apple-bt-driver.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion isreadme

[Icons]
Name: "{group}\Magic Mouse Enhancer"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Settings"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--settings"
Name: "{group}\Run self-test"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--selftest"
Name: "{group}\Uninstall Magic Mouse Enhancer"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Magic Mouse Enhancer"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Magic Mouse Enhancer}"; Flags: nowait postinstall skipifsilent
