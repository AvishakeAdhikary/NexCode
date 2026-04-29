; NexCode-Setup.iss
;
; Spec §38 — Inno Setup installer for the self-contained NexCode build.
; Installs to %ProgramFiles%\NexCode, registers the AppxPackage if present,
; registers the COM auto-start entry for Background Service Mode, and creates
; a Start Menu shortcut. No prerequisites page; the publish output is fully
; self-contained.

#define AppName "NexCode"
#define AppVersion GetEnv("NEXCODE_VERSION")
#if AppVersion == ""
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "NexCode"
#define AppExe "NexCode.Gui.exe"
#define PublishRoot "..\..\src\NexCode.Gui\bin\Release\net9.0-windows10.0.26100.0\win-x64\publish"
#define MsixDir "..\..\dist\msix"

[Setup]
AppId={{8E6E0B4F-7F6E-4F2A-9E0F-2C2E2D9F0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=NexCode-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
DisableWelcomePage=no
DisableReadyPage=no
DisableFinishedPage=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "background"; Description: "Start NexCode in background mode at login"; GroupDescription: "Background service mode:"; Flags: unchecked

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion createallsubdirs
Source: "{#MsixDir}\*.msix"; DestDir: "{app}\Msix"; Flags: external skipifsourcedoesntexist; Check: MsixPresent

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; COM auto-start hook for Background Service Mode (HKCU per-user; spec §29).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NexCode"; ValueData: """{app}\{#AppExe}"" --background"; Flags: uninsdeletevalue; Tasks: background

[Run]
; Optional MSIX registration step after files are copied.
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-ChildItem '{app}\Msix' -Filter '*.msix' | ForEach-Object {{ Add-AppxPackage -Path $_.FullName }}"""; Flags: runhidden waituntilterminated; Check: MsixPresent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-AppxPackage NexCode* | Remove-AppxPackage"""; Flags: runhidden

[Code]
function MsixPresent: Boolean;
begin
  Result := DirExists(ExpandConstant('{src}\..\..\dist\msix'));
end;
