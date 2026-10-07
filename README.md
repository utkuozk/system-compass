# System Compass

Windows health diagnostics, maintenance and selected update management. Built with .NET 8 WPF, with Turkish and English interfaces.

[Download the latest release](https://github.com/utkuozk/system-compass/releases/latest)

## Run

Download **System-Compass-Setup-1.10.2.exe** from Releases and follow the Turkish/English installation wizard. The .NET runtime is included: no ZIP extraction or separate runtime installation is required. Open **System Compass** from the desktop or Start menu and remove it through Windows Installed apps. Reports/settings are preserved. Finish maintenance and close the app before setup/removal; setup never forcibly closes workers or restarts Windows. Requires 64-bit Windows 10/11. The installer is unsigned.

The ZIP is retained only for older in-app updaters and advanced portable use; it requires .NET 8 Desktop Runtime. New users should choose **Setup.exe**. From 1.8, the updater also prefers the verified Setup EXE. The pipeline compiles a self-contained Windows installer; interactive install/upgrade/uninstall remain separate acceptance tests.

## Features

- Quick/deep diagnostics with DISM, SFC, read-only CHKDSK, available storage health and event history.
- Compact health dashboard with grouped result tabs, bounded tables and a full-detail viewer; scan progress records actual stage transitions and partial findings without invented percentages.
- Report-driven Windows repair with fresh corruption verification and power/servicing guards.
- Independent software and driver inventories, progress, elapsed time and per-query cancellation.
- Checkbox selection and a sequential installation queue for exact Windows Update items and supported WinGet packages, with a separate progress window.
- Microsoft Defender status and supported actions, scheduled maintenance and saved reports.
- Turkish/English UI that adopts the installer language on the next launch, and verified GitHub release downloads.
- Movable/resizable native window with minimize, maximize/restore and close controls; initial dimensions fit the work area.

## Limitations

This is not a hardware voltage tester or replacement antivirus. RAM hardware testing requires a separate Windows Memory Diagnostic run. Windows Update offers only part of the vendor-driver catalog. Unknown results never count as healthy.

Direct software installation supports verified **MSIX, MSI, WiX, Burn and Inno** installers from WinGet. The installer type is checked before execution and enforced on the exact package/version; supported conventional installers receive explicit no-restart switches. Other installer types and BIOS/UEFI/firmware updates are not installed. The app never requests an automatic reboot; required restarts are reported and stop the remaining queue. Installer behavior is provided by the software publisher. Active repair/install workers are not killed on a timeout or when the UI closes. Conflicting servicing is serialized, while unrelated software and driver inventory queries may run concurrently.

Windows Update/WinGet manages download caches; the app does not delete system-managed files. Reports/settings remain local under `%LOCALAPPDATA%\SistemPusulasi`. Do not commit personal diagnostic reports.

## Build

The repository pins SDK 9.0.203 and targets .NET 8 Windows Desktop. On Windows:

```powershell
dotnet restore
dotnet publish -c Release --no-restore -o artifacts/app
$p = Start-Process artifacts/app/SistemPusulasi.exe -ArgumentList --self-test -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'Self-tests failed' }
```

Tests use fixtures and WPF controls, including an isolated PowerShell harness that evaluates only selected helper definitions (never the installation body). They do not repair Windows, install updates or reboot, and do not replace real-machine acceptance testing. The Windows release workflow builds, tests and packages before publication.

Version 1.7 defaults to `utkuozk/system-compass` for release checks. Older versions can enter that repository in Maintenance settings. Downloads are verified against GitHub's SHA-256 digest before extraction.
