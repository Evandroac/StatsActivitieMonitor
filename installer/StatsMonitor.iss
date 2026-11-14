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
AppVerName=Stats Monitor {#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Stats Monitor
MinVersion=10.0
AppMutex=StatsMonitor.SingleInstance
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
UsePreviousTasks=yes
UninstallDisplayName=Stats Monitor
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
Name: "startup"; Description: "Iniciar com o Windows (como administrador) / Start with Windows (elevated)"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Stats Monitor"; Filename: "{app}\StatsMonitor.exe"
Name: "{autodesktop}\Stats Monitor"; Filename: "{app}\StatsMonitor.exe"; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{app}\*.pdb"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Run]
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /TN ""StatsMonitor"" /XML ""{tmp}\StatsMonitor_task.xml"" /F"; Flags: runhidden waituntilterminated; Tasks: startup; Check: WriteTaskXml
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""StatsMonitor"" /F"; Flags: runhidden waituntilterminated; Tasks: not startup

Filename: "{app}\StatsMonitor.exe"; Description: "{cm:LaunchProgram,Stats Monitor}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""StatsMonitor"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "DelStartupTask"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM StatsMonitor.exe"; Flags: runhidden waituntilterminated; RunOnceId: "KillApp"

[Code]
// The app needs admin rights, so autostart is a Task Scheduler logon task (a Run-key entry would be skipped by Windows).
function WriteTaskXml: Boolean;
var
  User, Xml: String;
begin
  User := GetEnv('USERDOMAIN') + '\' + GetEnv('USERNAME');
  Xml :=
    '<?xml version="1.0" encoding="UTF-16"?>' +
    '<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' +
    '<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>' + User + '</UserId></LogonTrigger></Triggers>' +
    '<Principals><Principal id="Author"><UserId>' + User + '</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>' +
    '<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' +
    '<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><StartWhenAvailable>true</StartWhenAvailable></Settings>' +
    '<Actions Context="Author"><Exec><Command>' + ExpandConstant('{app}\StatsMonitor.exe') + '</Command><WorkingDirectory>' + ExpandConstant('{app}') + '</WorkingDirectory></Exec></Actions>' +
    '</Task>';
  Result := SaveStringToFile(ExpandConstant('{tmp}\StatsMonitor_task.xml'), Xml, False);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if MsgBox('Remover também as configurações e registros do usuário (%AppData%\StatsMonitor)?' + #13#10 +
              'Also remove user settings and logs (%AppData%\StatsMonitor)?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\StatsMonitor'), True, True, True);
end;