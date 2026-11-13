; Inno Setup script for Stats Monitor. Build with installer\build.ps1.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

[Setup]
AppId={{B7C2F0A4-5C1E-4C57-9E58-5D3B5A7E6A11}
AppName=Stats Monitor
AppVersion={#AppVersion}
AppPublisher=Stats Monitor
DefaultDirName={autopf}\Stats Monitor
DefaultGroupName=Stats Monitor
UninstallDisplayIcon={app}\StatsMonitor.exe
SetupIconFile=..\src\StatsMonitor\StatsMonitor.ico
OutputDir=..\artifacts
OutputBaseFilename=StatsMonitor-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
#ifdef SignTool
SignTool=signtool
SignedUninstaller=yes
#endif

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "startup"; Description: "Iniciar com o Windows / Start with Windows"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Stats Monitor"; Filename: "{app}\StatsMonitor.exe"
Name: "{autodesktop}\Stats Monitor"; Filename: "{app}\StatsMonitor.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "StatsMonitor"; ValueData: """{app}\StatsMonitor.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\StatsMonitor.exe"; Description: "{cm:LaunchProgram,Stats Monitor}"; Flags: nowait postinstall skipifsilent runascurrentuser
