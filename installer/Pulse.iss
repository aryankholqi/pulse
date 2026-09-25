; Pulse installer — build with build.ps1 (or: ISCC /DAppVersion=1.0.0 installer\Pulse.iss)

#define AppName "Pulse"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "Aryan Kholghi"
#define AppExe "Pulse.exe"
#define PublishDir "..\publish"

[Setup]
; Never change AppId after the first release — Windows uses it to recognise upgrades.
AppId={{8C1E5B42-7A9D-4F3B-9E61-2D4C7B0A5F13}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir=..\dist
OutputBaseFilename=Pulse-Setup-{#AppVersion}
SetupIconFile=..\Assets\Pulse.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

WizardStyle=modern
WizardImageFile=..\Assets\WizardImage.bmp
WizardSmallImageFile=..\Assets\WizardSmallImage.bmp

Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Start Pulse automatically when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Register / remove the sign-in task according to the checkbox.
Filename: "{app}\{#AppExe}"; Parameters: "--register-startup"; Tasks: startup; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-startup"; Tasks: not startup; Flags: runhidden waituntilterminated
; "Launch Pulse" checkbox on the last page. Setup is already elevated, so no second UAC prompt.
Filename: "{app}\{#AppExe}"; Description: "Launch Pulse now"; Flags: nowait postinstall skipifsilent runascurrentuser
; In-app update: Pulse runs this setup with /SILENT /RELAUNCH and exits; bring the new version back up.
Filename: "{app}\{#AppExe}"; Flags: nowait runascurrentuser; Check: WizardSilent and ShouldRelaunch

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "StopPulse"
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-startup"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveStartupTask"
Filename: "{sys}\logman.exe"; Parameters: "stop PulseOverlay -ets"; Flags: runhidden; RunOnceId: "StopEtwSession"

[Code]
// /RELAUNCH: passed by Pulse's own updater (Inno ignores switches it doesn't know).
function ShouldRelaunch: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then Result := True;
end;

// Close a running Pulse before files are replaced (upgrades / reinstalls).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
