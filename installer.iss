; Inno Setup script for Borderless Window Manager.
;
; Builds Setup.exe: installs the app, adds Start Menu / optional desktop
; shortcuts, and registers an uninstaller so the app shows up in Windows'
; Settings > Apps (and the classic "Programs and Features" list) and can
; be removed cleanly from there.
;
; IMPORTANT: keep AppId below IDENTICAL across every future release.
; It's how Windows recognizes "this is an upgrade of the same app"
; rather than treating each release as a brand new, separately-listed
; program. Generate it once, then never touch it again.

#define MyAppName "Borderless Window Manager"
#define MyAppVersion "__VERSION__"
#define MyAppPublisher "Your Name"
#define MyAppExeName "BorderlessApp.exe"

[Setup]
AppId={{6F2A1E3D-9B4C-4E7A-9F0D-3C8B7A2E5D91}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=installer-output
OutputBaseFilename=BorderlessApp-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Installs to the user's own profile, no admin/UAC prompt required.
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Packages the self-contained build so the installer has no extra
; runtime prerequisite to check for or explain to the user.
Source: "publish\self-contained\BorderlessApp.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
