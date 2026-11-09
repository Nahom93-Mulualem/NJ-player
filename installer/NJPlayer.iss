; =====================================================================
; Inno Setup Script for NJ Player
; Per-user installation (no admin elevation required), file associations,
; shortcuts, and clean uninstaller.
; =====================================================================

#define MyAppName "NJ Player"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "NJ Player Contributors"
#define MyAppURL "https://github.com/Nahom93-Mulualem/NJ-player"
#define MyAppExeName "NJPlayer.App.exe"

[Setup]
AppId={{D37E5528-7C1C-4B6A-8A47-E39B07AC3599}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\NJPlayer
DisableProgramGroupPage=yes
; Per-user installation: installs into LocalAppData without requiring Admin privileges
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=NJPlayer-Setup-{#MyAppVersion}-x64
SetupIconFile=..\src\NJPlayer.App\Assets\njplayer.ico
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "assoc_video"; Description: "Associate with common video formats (.mp4, .mkv, .avi, .webm, .mov)"; GroupDescription: "File Associations:"
Name: "assoc_audio"; Description: "Associate with common audio formats (.mp3, .flac, .wav, .m4a, .ogg)"; GroupDescription: "File Associations:"; Flags: unchecked

[Files]
; Main application binaries published by dotnet publish
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Root ProgID registration for NJ Player in HKCU (per-user)
Root: HKCU; Subkey: "Software\Classes\NJPlayer.Media"; ValueType: string; ValueName: ""; ValueData: "NJ Player Media File"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\NJPlayer.Media\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKCU; Subkey: "Software\Classes\NJPlayer.Media\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

; Video file associations
Root: HKCU; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: none; ValueName: "NJPlayer.Media"; Flags: uninsdeletevalue; Tasks: assoc_video
Root: HKCU; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: none; ValueName: "NJPlayer.Media"; Flags: uninsdeletevalue; Tasks: assoc_video
Root: HKCU; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: none; ValueName: "NJPlayer.Media"; Flags: uninsdeletevalue; Tasks: assoc_video
Root: HKCU; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: none; ValueName: "NJPlayer.Media"; Flags: uninsdeletevalue; Tasks: assoc_video
Root: HKCU; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: none; ValueName: "NJPlayer.Media"; Flags: uninsdeletevalue; Tasks: assoc_video

; Audio file associations
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: none; ValueName: "NJPlayer.Media"; Flags: uninsdeletevalue; Tasks: assoc_audio
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: none; ValueName: "NJPlayer.Media"; Flags: uninsdeletevalue; Tasks: assoc_audio
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: none; ValueName: "NJPlayer.Media"; Flags: uninsdeletevalue; Tasks: assoc_audio
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: none; ValueName: "NJPlayer.Media"; Flags: uninsdeletevalue; Tasks: assoc_audio
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: none; ValueName: "NJPlayer.Media"; Flags: uninsdeletevalue; Tasks: assoc_audio

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

