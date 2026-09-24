; Inno Setup script for SoundMatrix.
; Build:  ISCC.exe /DAppVersion=1.2.3 installer\SoundMatrix.iss   (after `dotnet publish ... -o publish`)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
; Never change AppId: it's how upgrades find the existing install.
AppId={{95D0D212-1702-484C-B83B-8C6577EB58AE}
AppName=SoundMatrix
AppVersion={#AppVersion}
AppVerName=SoundMatrix {#AppVersion}
AppPublisher=Ewan Hardingham
AppPublisherURL=https://github.com/ewanhardingham/SoundMatrix
AppSupportURL=https://github.com/ewanhardingham/SoundMatrix/issues
DefaultDirName={localappdata}\Programs\SoundMatrix
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=SoundMatrix-Setup-{#AppVersion}
SetupIconFile=..\assets\SoundMatrix.ico
UninstallDisplayIcon={app}\SoundMatrix.exe
UninstallDisplayName=SoundMatrix
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "startup"; Description: "Start SoundMatrix when I sign in to Windows"; GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\SoundMatrix"; Filename: "{app}\SoundMatrix.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SoundMatrix"; \
    ValueData: """{app}\SoundMatrix.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\SoundMatrix.exe"; Description: "Launch SoundMatrix"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM SoundMatrix.exe"; Flags: runhidden; RunOnceId: "StopSoundMatrix"

[Code]
// SoundMatrix lives in the tray with no visible window, so close it before replacing its files.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SoundMatrix.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
