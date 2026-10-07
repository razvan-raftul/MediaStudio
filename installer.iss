; Program de instalare pentru Media Studio (Inno Setup 6)
; Se instalează doar pentru utilizatorul curent, fără drepturi de administrator,
; ca folderul „unelte” de lângă program să poată fi scris (FFmpeg și yt-dlp se descarcă acolo).

#define AppName "Media Studio"
#define AppVersion "0.1.1"

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

[UninstallDelete]
Type: filesandordirs; Name: "{app}\unelte"
