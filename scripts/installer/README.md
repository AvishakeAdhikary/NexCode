# NexCode Installer (Inno Setup)

Spec §38 — packages the self-contained publish output as a single Setup.exe.

## Prerequisites

- Inno Setup 6+ (`iscc.exe` on PATH).
- Visual Studio Build Tools (for `msbuild` and the WAP packaging targets).
- A signed self-contained publish output and (optionally) an MSIX bundle.

## Build steps

1. Run a self-contained publish + MSIX build:

   ```powershell
   pwsh scripts\build\Bundle-Assets.ps1
   pwsh scripts\build\Publish-SelfContained.ps1 -Configuration Release
   ```

2. (Optional) Sign the MSIX:

   ```powershell
   pwsh scripts\build\Sign-Msix.ps1
   ```

3. Compile the installer:

   ```powershell
   $env:NEXCODE_VERSION = "1.0.0"
   iscc scripts\installer\NexCode-Setup.iss
   ```

   The output `NexCode-Setup-<version>.exe` is written next to the script
   under `Output\` by Inno Setup's default behaviour.

## What the installer does

- Copies the self-contained publish output to `%ProgramFiles%\NexCode\`.
- Optionally registers the bundled MSIX (`Add-AppxPackage`) if `dist\msix\*.msix`
  is present at packaging time.
- When the user opts into the Background Service Mode task, registers
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NexCode` so the GUI
  starts hidden in the system tray after logon (spec §29).
- Adds a Start Menu shortcut and (optionally) a desktop shortcut.
- Removes the AppxPackage on uninstall via PowerShell's `Remove-AppxPackage`.

## Notes

- The `[Code] MsixPresent` function only enables the MSIX-related steps if
  `dist\msix` exists during compilation, so dev builds without an MSIX still
  produce a working installer.
- `PrivilegesRequired=admin` is required to write under `%ProgramFiles%`. The
  HKCU run entry is still per-user and survives admin uninstall.
