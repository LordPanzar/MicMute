; MicMute installer (Inno Setup 6). Built by build.ps1, which passes /DAppVersion=x.y.z.
; Installs per user (no administrator needed), so the in-app updater can run the next setup silently.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "MicMute"
#define AppExe "MicMute.exe"
#define RepoUrl "https://github.com/LordPanzar/MicMute"

[Setup]
AppId={{25638A25-5BAB-43ED-BF08-205F61B2E0E1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=LordPanzar
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Languages]
Name: "nb"; MessagesFile: "compiler:Languages\Norwegian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
nb.Autostart=Start MicMute når Windows starter
en.Autostart=Start MicMute when Windows starts

[Tasks]
Name: "autostart"; Description: "{cm:Autostart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Only on a normal install: an update must not turn autostart back on if it was switched off in the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; \
  ValueData: """{app}\{#AppExe}"""; Tasks: autostart; Check: not IsUpdate

[Run]
; Also runs after a silent update, so MicMute comes back by itself.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall

[Code]
const
  AppMutex = 'MicMute_SingleInstance_5E2A1C';

function IsUpdate: Boolean;
begin
  Result := Pos('/UPDATE', Uppercase(GetCmdTail)) > 0;
end;

{ Asks a running MicMute to quit (MicMute.exe --exit), waits, and force-closes it as a last resort. }
procedure CloseMicMute;
var
  Exe: String;
  Rc, I: Integer;
begin
  if not CheckForMutexes(AppMutex) then Exit;
  Exe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Exe) then
    Exec(Exe, '--exit', '', SW_HIDE, ewNoWait, Rc);
  for I := 1 to 50 do
  begin
    if not CheckForMutexes(AppMutex) then Exit;
    Sleep(200);
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Rc);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseMicMute;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseMicMute;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Remove autostart only if it points at this installation. }
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}', Value) and
       (Pos(Uppercase(ExpandConstant('{app}')), Uppercase(Value)) > 0) then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');
  end;
end;
