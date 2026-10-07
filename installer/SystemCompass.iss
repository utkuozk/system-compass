; Build with Inno Setup 6.3+ after publishing the self-contained win-x64 app.
; ISCC.exe /DAppVersion=1.9.0 /DPublishDir="C:\path\setup-app" /DOutputDir="C:\path\artifacts" SystemCompass.iss
; No runtime downloads, background maintenance, or forced process termination.
#ifndef AppVersion
  #define AppVersion "1.9.0"
#endif
#ifndef PublishDir
  #define PublishDir SourcePath + "..\artifacts\setup-app"
#endif
#ifndef OutputDir
  #define OutputDir SourcePath + "..\artifacts"
#endif
#if !FileExists(PublishDir + "\SistemPusulasi.exe")
  #error "Publish the app to artifacts/setup-app before compiling the installer."
#endif
#if !FileExists(PublishDir + "\coreclr.dll")
  #error "The installer requires a self-contained win-x64 publish including coreclr.dll."
#endif

[Setup]
; Keep this ID stable across versions so upgrades retain their uninstall entry.
AppId={{C6DF0DF1-C111-470E-BE8B-CC481B83F15F}
AppName=System Compass
AppVersion={#AppVersion}
AppVerName=System Compass {#AppVersion}
AppPublisher=Hakan
AppComments=Windows health diagnostics and maintenance
DefaultDirName={autopf}\SistemPusulasi
DefaultGroupName=System Compass
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=System-Compass-Setup-{#AppVersion}
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=System Compass
UninstallDisplayIcon={app}\SistemPusulasi.exe
Uninstallable=yes
CreateUninstallRegKey=yes
SetupLogging=yes
SetupMutex=Global\SistemPusulasi-Setup
AppMutex=Global\SystemCompassRunning
CloseApplications=no
RestartApplications=no
AlwaysRestart=no
RestartIfNeededByRun=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
turkish.DesktopShortcut=Masaüstü kısayolu oluştur
english.LaunchApp=Open System Compass
turkish.LaunchApp=Sistem Pusulası'nı aç
english.CloseApp=System Compass or a maintenance worker is running. Wait for maintenance to finish, close all System Compass windows, and try again. Setup will not stop active work.
turkish.CloseApp=Sistem Pusulası veya bakım işlemi çalışıyor. Bakımın tamamlanmasını bekleyin, tüm Sistem Pusulası pencerelerini kapatın ve yeniden deneyin. Kurulum devam eden işlemleri durdurmaz.
english.ProcessCheckFailed=Running processes could not be checked. No application files have been changed. Make sure the Windows Management Instrumentation service is available, then try again.
turkish.ProcessCheckFailed=Çalışan işlemler doğrulanamadı. Uygulama dosyaları değiştirilmedi. Windows Yönetim Araçları hizmetinin kullanılabilir olduğundan emin olun ve yeniden deneyin.
english.ScheduleCleanupFailed=The application's scheduled maintenance task could not be safely removed. Uninstallation was stopped before removing application files. Try again after checking Windows Task Scheduler.
turkish.ScheduleCleanupFailed=Uygulamanın planlı bakım görevi güvenle kaldırılamadı. Uygulama dosyaları kaldırılmadan işlem durduruldu. Windows Görev Zamanlayıcı'yı kontrol edip yeniden deneyin.
english.InstallerBusy=Another System Compass installation or removal is running. Wait for it to finish and try again.
turkish.InstallerBusy=Başka bir Sistem Pusulası kurulum veya kaldırma işlemi çalışıyor. Tamamlanmasını bekleyin ve yeniden deneyin.
english.SafeInstallInfo=Setup includes the required runtime. Scheduled maintenance is enabled only when you choose to enable it in the app. Your saved reports and settings are preserved.
turkish.SafeInstallInfo=Kurulum gerekli çalışma ortamını içerir. Planlı bakım yalnızca uygulamada etkinleştirmeyi seçtiğinizde açılır. Kayıtlı raporlarınız ve ayarlarınız korunur.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Exclude development/test output even if present in the publish directory.
; No restartreplace flag: an in-use file must never be deferred to a reboot.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,self-test-results.txt,SistemPusulasi-Kur.exe,ui-previews\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{commonprograms}\System Compass\System Compass"; Filename: "{app}\SistemPusulasi.exe"; WorkingDir: "{app}"
Name: "{commonprograms}\System Compass\{cm:UninstallProgram,System Compass}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\System Compass"; Filename: "{app}\SistemPusulasi.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[UninstallDelete]
; This setup-owned handoff marker contains no user data.
Type: files; Name: "{app}\installer-language.txt"

[Run]
; A normal interactive launch: it does not enable a schedule or start maintenance.
Filename: "{app}\SistemPusulasi.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function ProcessSafetyError: String;
var
  Locator, Services, Processes: Variant;
begin
  Result := '';
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    { All instances are blocked, including other sessions and portable copies.
      The legacy installer alias is also blocked because it can copy app files. }
    Processes := Services.ExecQuery(
      'SELECT ProcessId FROM Win32_Process WHERE Name = ''SistemPusulasi.exe'' OR Name = ''SistemPusulasi-Kur.exe''');
    if Processes.Count > 0 then
      Result := CustomMessage('CloseApp');
  except
    Log('Process safety check failed: ' + GetExceptionMessage);
    Result := CustomMessage('ProcessCheckFailed');
  end;
end;

function InitializeSetup: Boolean;
var
  ErrorText: String;
begin
  ErrorText := ProcessSafetyError;
  Result := ErrorText = '';
  if not Result then
    SuppressibleMsgBox(ErrorText, mbError, MB_OK, IDOK);
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption := WizardForm.WelcomeLabel2.Caption + #13#10#13#10 + CustomMessage('SafeInstallInfo');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Token: String;
begin
  if CurStep = ssPostInstall then
  begin
    Token := '{#AppVersion}-' + GetDateTimeString('yyyymmddhhnnsszzz', '', '');
    if not SaveStringToFile(ExpandConstant('{app}\installer-language.txt'), ActiveLanguage + '|' + Token, False) then
      RaiseException('The selected setup language could not be saved for System Compass.');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  { Repeat immediately before copying: the wizard may have been open for hours. }
  Result := ProcessSafetyError;
end;

function InitializeUninstall: Boolean;
var
  ErrorText: String;
begin
  Result := False;
  if CheckForMutexes('Global\SistemPusulasi-Setup') then
  begin
    SuppressibleMsgBox(CustomMessage('InstallerBusy'), mbError, MB_OK, IDOK);
    Exit;
  end;
  CreateMutex('Global\SistemPusulasi-Setup');
  ErrorText := ProcessSafetyError;
  Result := ErrorText = '';
  if not Result then
    SuppressibleMsgBox(ErrorText, mbError, MB_OK, IDOK);
end;

procedure InitializeUninstallProgressForm;
var
  ErrorText, AppExe: String;
  ResultCode: Integer;
begin
  { This event runs after uninstall confirmation, before any file deletion.
    Inno treats exceptions here as fatal, preserving the installed files. }
  ErrorText := ProcessSafetyError;
  if ErrorText <> '' then
    RaiseException(ErrorText);

  AppExe := ExpandConstant('{app}\SistemPusulasi.exe');
  { Fail closed if the cleanup executable is missing or fails. It validates
    task ownership and never deletes a same-name task belonging to another app. }
  if not FileExists(AppExe) then
    RaiseException(CustomMessage('ScheduleCleanupFailed'));
  if not Exec(AppExe, '--uninstall-schedule', ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException(CustomMessage('ScheduleCleanupFailed'));
  if ResultCode <> 0 then
  begin
    Log('Schedule cleanup returned exit code ' + IntToStr(ResultCode));
    RaiseException(CustomMessage('ScheduleCleanupFailed'));
  end;

  ErrorText := ProcessSafetyError;
  if ErrorText <> '' then
    RaiseException(ErrorText);
end;

{ There is deliberately no [UninstallDelete] entry for user data. Only files
  recorded by this installer are removed; LocalAppData reports/settings remain. }
