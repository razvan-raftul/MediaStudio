; Program de instalare pentru Media Studio (Inno Setup 6)
; Se instalează doar pentru utilizatorul curent, fără drepturi de administrator,
; ca folderul „unelte” de lângă program să poată fi scris (FFmpeg și yt-dlp se descarcă acolo).

#define AppName "Media Studio"
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif

[Setup]
AppId={{6C1E0A52-7B4D-4E0B-9C1D-5A3E2F1B7D40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Răzvan
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=.
OutputBaseFilename=MediaStudio-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\MediaStudio.exe
UninstallDisplayName={#AppName}
CloseApplications=yes

[Languages]
#if FileExists(AddBackslash(SourcePath) + "Romanian.isl")
Name: "ro"; MessagesFile: "Romanian.isl"
#else
Name: "en"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "Pune o scurtătură pe desktop"; GroupDescription: "Scurtături:"

[Files]
Source: "out\MediaStudio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "out\MediaStudio.exe.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\MediaStudio.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\MediaStudio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MediaStudio.exe"; Description: "Pornește Media Studio acum"; Flags: nowait postinstall skipifsilent
; la actualizarea automată (instalare fără ferestre) aplicația se redeschide singură
Filename: "{app}\MediaStudio.exe"; Flags: nowait; Check: IsAutoUpdate

[UninstallDelete]
Type: filesandordirs; Name: "{app}\unelte"

[Code]
function IsAutoUpdate: Boolean;
begin
  Result := Pos('/AUTOUPDATE', Uppercase(GetCmdTail)) > 0;
end;
