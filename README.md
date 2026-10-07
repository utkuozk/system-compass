# System Compass

Windows health diagnostics, maintenance and selected update management. Built with .NET 8 WPF, with Turkish and English interfaces.

[Download the latest release](https://github.com/utkuozk/system-compass/releases/latest)

## Run

Requires the **.NET 8 Desktop Runtime for Windows**. Extract the ZIP and run `SistemPusulasi.exe` for portable use, or `SistemPusulasi-Kur.exe` to install. Names are retained for updater compatibility. The application is unsigned. Administrator permission is requested only for operations that require it.

## Features

- Quick/deep diagnostics with DISM, SFC, read-only CHKDSK, available storage health and event history.
- Report-driven Windows repair with fresh corruption verification and power/servicing guards.
- Independent software and driver inventories, progress, elapsed time and per-query cancellation.
- Confirmed installation of one exact Windows Update item or an eligible WinGet MSIX update.
- Microsoft Defender status and supported actions, scheduled maintenance and saved reports.
- Turkish/English UI and verified GitHub release downloads.
- Movable/resizable native window with minimize, maximize/restore and close controls; initial dimensions fit the work area.

## Limitations

This is not a hardware voltage tester or replacement antivirus. RAM hardware testing requires a separate Windows Memory Diagnostic run. Windows Update offers only part of the vendor-driver catalog. Unknown results never count as healthy.

Direct software installation supports eligible **MSIX** packages from WinGet only. EXE/MSI and BIOS/UEFI/firmware installations are unsupported. The app never issues an automatic reboot; required restarts are reported. Active repair/install workers are not killed on a timeout or when the UI closes. Conflicting servicing is serialized, while unrelated software and driver inventory queries may run concurrently.

Windows Update/WinGet manages download caches; the app does not delete system-managed files. Reports/settings remain local under `%LOCALAPPDATA%\SistemPusulasi`. Do not commit personal diagnostic reports.

## Build

The repository pins SDK 9.0.203 and targets .NET 8 Windows Desktop. On Windows:

```powershell
dotnet restore
dotnet publish -c Release --no-restore -o artifacts/app
$p = Start-Process artifacts/app/SistemPusulasi.exe -ArgumentList --self-test -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'Self-tests failed' }
```

Tests use fixtures and WPF controls. They do not repair Windows, install updates or reboot, and do not replace real-machine acceptance testing. The Windows release workflow builds, tests and packages before publication.

Version 1.7 defaults to `utkuozk/system-compass` for release checks. Older versions can enter that repository in Maintenance settings. Downloads are verified against GitHub's SHA-256 digest before extraction.
