$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$WarningPreference = 'SilentlyContinue'
$InformationPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$reboot = $false
$operationStarted = $false

function Progress([string]$message) { [Console]::WriteLine('PUSULA_PROGRESS:' + $message) }
function Result([string]$status, [string]$detail, [bool]$verified=$false) {
    # The embedded script runs in a child scope (& { ... }); script: would read a different scope.
    [Console]::WriteLine('PUSULA_RESULT:' + (@{Status=$status; Detail=$detail; RebootRequired=[bool]$reboot; Verified=$verified} | ConvertTo-Json -Compress))
}
function Winget-InstallerType([string]$text) {
    # CLI labels are localized. Values are invariant; always enforce the returned type on upgrade.
    # Unrecognized or ambiguous output cannot authorize a generic EXE installer.
    $clean = [regex]::Replace($text, '\x1B\[[0-?]*[ -/]*[@-~]', '')
    $types = @([regex]::Matches($clean, '(?im)^\s+[^\r\n:]+:\s*(msix|msi|wix|burn|inno)\s*$') | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() } | Select-Object -Unique)
    if ($types.Count -eq 1) { return $types[0] }
    return ''
}
function Winget-UpgradeArguments([string]$id, [string]$version, [string]$type) {
    if ($type -notin @('msix','msi','wix','burn','inno')) { throw 'Desteklenmeyen yükleyici türü.' }
    $arguments = @('upgrade','--id',$id,'--exact','--source','winget','--version',$version,'--installer-type',$type,'--silent','--disable-interactivity','--skip-dependencies','--accept-package-agreements','--accept-source-agreements')
    # Additive switches retain publisher install-scope/options and explicitly suppress restarts.
    if ($type -in @('msi','wix')) { $arguments += @('--custom','/norestart REBOOT=ReallySuppress') }
    elseif ($type -in @('burn','inno')) { $arguments += @('--custom','/norestart') }
    return $arguments
}
function Available-Version($update, [string]$kind) {
    if ($kind -eq 'drivers') {
        $version = 'WUA sürücü önerisi'
        try { if ($null -ne $update.DriverVerDate) { $version += ' (' + ([datetime]$update.DriverVerDate).ToString('yyyy-MM-dd') + ')' } } catch { }
        return $version
    }
    $kb = @($update.KBArticleIDs)
    if ($kb.Count -gt 0) { return (($kb | ForEach-Object { 'KB' + $_ }) -join ', ') }
    return 'Uygulanabilir güncelleme'
}
function Contains-Firmware($update, [int]$depth=0) {
    # Also inspect bundles: a safe-looking parent title must not authorize embedded firmware.
    if ($depth -gt 16) { return $true }
    if ([string]$update.Title -match '\b(BIOS|UEFI|Firmware)\b|üretici yazılımı|ürün yazılımı|bellenim') { return $true }
    if ([int]$update.Type -eq 2) {
        if ([string]$update.DriverClass -match 'Firmware|BIOS|UEFI' -or [string]$update.DriverHardwareID -match '^UEFI[\\_]') { return $true }
    }
    for ($index=0; $index -lt $update.BundledUpdates.Count; $index++) {
        if (Contains-Firmware $update.BundledUpdates.Item($index) ($depth+1)) { return $true }
    }
    return $false
}
function Winget-InstalledVersion([string]$text, [string]$id) {
    $lines = ([regex]::Replace($text, '\x1B\[[0-?]*[ -/]*[@-~]', '') -replace "`r", '') -split "`n"
    $matchesFound = New-Object System.Collections.Generic.List[string]
    for ($i=1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -notmatch '^-{8,}$') { continue }
        $columns = [regex]::Matches($lines[$i-1], '\S(?:.*?\S)?(?=\s{2,}|$)')
        if ($columns.Count -notin @(3,4,5) -or $columns[0].Index -ne 0) { continue }
        for ($j=$i+1; $j -lt $lines.Count; $j++) {
            $line = $lines[$j]
            if ($line.Length -le $columns[2].Index) { continue }
            $identity = $line.Substring($columns[1].Index, $columns[2].Index-$columns[1].Index).Trim()
            $end = if ($columns.Count -gt 3) { [Math]::Min($line.Length,$columns[3].Index) } else { $line.Length }
            $version = $line.Substring($columns[2].Index,$end-$columns[2].Index).Trim()
            if ($identity -ceq $id -and $version -cmatch '^[vV]?\d[A-Za-z0-9._+~:-]{0,127}$') { $matchesFound.Add($version) }
        }
    }
    if ($matchesFound.Count -eq 1) { return $matchesFound[0] }
    return ''
}

try {
    $entry = $env:PUSULA_SELECTED_UPDATE | ConvertFrom-Json
    if ($entry.Kind -notin @('windows','drivers','software')) { throw 'Geçersiz güncelleme türü.' }
    if ($entry.Kind -eq 'software') {
        if ([string]$entry.PackageId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,255}$' -or
            [string]$entry.AvailableVersion -cnotmatch '^[vV]?\d[A-Za-z0-9._+~:-]{0,127}$' -or
            [string]$entry.InstalledVersion -cnotmatch '^[vV]?\d[A-Za-z0-9._+~:-]{0,127}$') { throw 'Kesin paket kimliği veya sürümü geçersiz.' }
        $appInstaller = @(Get-AppxPackage -Name Microsoft.DesktopAppInstaller -ErrorAction Stop)
        if ($appInstaller.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$appInstaller[0].InstallLocation)) {
            throw 'Geçerli kullanıcı için tek bir App Installer paketi doğrulanamadı.'
        }
        $wingetPath = Join-Path ([string]$appInstaller[0].InstallLocation) 'winget.exe'
        # Restrict discovery to the signed App Installer binary for the current user; never PATH scripts/aliases.
        $signature = Get-AuthenticodeSignature -LiteralPath $wingetPath
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
            throw 'Microsoft imzalı WinGet çalıştırılabilir dosyası doğrulanamadı.'
        }
        Progress 'Kurulu paket sürümü yeniden doğrulanıyor…'
        $ErrorActionPreference = 'Continue'
        $before = (& $wingetPath list --id ([string]$entry.PackageId) --exact --source winget --disable-interactivity 2>&1 | Out-String)
        $beforeExit = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($beforeExit -ne 0 -or (Winget-InstalledVersion $before ([string]$entry.PackageId)) -cne [string]$entry.InstalledVersion) {
            Result 'Rejected' 'Seçilen kurulu paket sürümü değişti veya kesin olarak doğrulanamadı; yeniden kontrol edin.'; return
        }
        Progress 'Yükleyici türü ve yeniden başlatmadan kurulum desteği denetleniyor…'
        $ErrorActionPreference = 'Continue'
        $metadata = (& $wingetPath show --id ([string]$entry.PackageId) --exact --source winget --version ([string]$entry.AvailableVersion) --disable-interactivity --accept-source-agreements 2>&1 | Out-String)
        $metadataExit = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        $installerType = Winget-InstallerType $metadata
        if ($metadataExit -ne 0 -or -not $installerType) {
            Result 'Rejected' 'Yeniden başlatmadan kurulumu desteklenen yükleyici doğrulanamadı. Desteklenen türler: MSIX, MSI, WiX, Burn ve Inno. Yayıncının güncelleyicisini kullanın.'; return
        }
        $upgradeArguments = @(Winget-UpgradeArguments ([string]$entry.PackageId) ([string]$entry.AvailableVersion) $installerType)
        Progress 'Seçilen yazılım indiriliyor ve kuruluyor; yönetici izni istenirse Windows penceresinden onaylayın…'
        # No --allow-reboot, --force, override, dependency installations or update-all.
        $operationStarted = $true
        $ErrorActionPreference = 'Continue'
        $installOutput = (& $wingetPath @upgradeArguments 2>&1 | Out-String)
        $installExit = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        Progress 'Kurulum sonrası seçilen paket sürümü doğrulanıyor…'
        $ErrorActionPreference = 'Continue'
        $after = (& $wingetPath list --id ([string]$entry.PackageId) --exact --source winget --disable-interactivity 2>&1 | Out-String)
        $afterExit = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        try { $reboot = [bool](New-Object -ComObject Microsoft.Update.SystemInfo).RebootRequired } catch { }
        if ($installExit -eq 0 -and $afterExit -eq 0 -and (Winget-InstalledVersion $after ([string]$entry.PackageId)) -ceq [string]$entry.AvailableVersion) {
            Result 'Installed' 'Seçilen yazılım sürümünün kurulu olduğu doğrulandı. Otomatik yeniden başlatma yapılmadı.' $true
        } elseif ($installExit -eq 0) {
            Result 'Unknown' 'WinGet tamamlandı fakat seçilen sürümün kurulu olduğu doğrulanamadı; yeniden kontrol edin.'
        } else {
            $cleanOutput = [regex]::Replace([string]$installOutput, '\x1B\[[0-?]*[ -/]*[@-~]', '').Trim()
            if ($cleanOutput.Length -gt 1200) { $cleanOutput = $cleanOutput.Substring($cleanOutput.Length-1200) }
            Result 'Failed' ('WinGet kurulum çıkış kodu: ' + $installExit + '. ' + $cleanOutput)
        }
        return
    }
    $identity = [guid]::Empty
    if (-not [guid]::TryParseExact([string]$entry.UpdateId,'D',[ref]$identity) -or $identity -eq [guid]::Empty -or
        [int]$entry.Revision -le 0) { throw 'Windows Update kimliği veya revizyonu geçersiz.' }
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Windows/sürücü kurulumu için yönetici izni gerekli.' }
    $systemInfo = New-Object -ComObject Microsoft.Update.SystemInfo
    if ($systemInfo.RebootRequired) { $reboot=$true; Result 'Rejected' 'Windows önce yeniden başlatılmayı bekliyor; kurulum başlatılmadı.'; return }
    if (@(Get-Process -Name dism,sfc,msiexec -ErrorAction SilentlyContinue).Count -gt 0) { Result 'Busy' 'Başka bir sistem bakım veya kurulum işlemi çalışıyor.'; return }
    $session = New-Object -ComObject Microsoft.Update.Session
    $session.ClientApplicationID = 'Sistem Pusulasi - confirmed single update installation'
    $searcher = $session.CreateUpdateSearcher()
    $searcher.Online = $true
    $type = if ($entry.Kind -eq 'drivers') { 'Driver' } else { 'Software' }
    Progress 'Windows Update tam kimlik ve revizyonla yeniden aranıyor…'
    # GUID/integer are parsed before use. Keep configured organizational update source unchanged.
    $search = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='$type' and UpdateID='$($identity.ToString('D'))' and RevisionNumber=$([int]$entry.Revision)")
    if ([int]$search.ResultCode -ne 2 -or $search.Updates.Count -ne 1) { Result 'Rejected' 'Seçilen güncelleme kimliği ve revizyonu artık tek bir kullanılabilir güncelleme olarak doğrulanamadı.'; return }
    $update = $search.Updates.Item(0)
    if (Contains-Firmware $update) { Result 'Rejected' 'BIOS/UEFI/firmware güncellemesi algılandı; bu uygulama bunları kurmaz. Üreticinin yönergelerini kullanın.'; return }
    if ([string]$update.Identity.UpdateID -ine $identity.ToString('D') -or [int]$update.Identity.RevisionNumber -ne [int]$entry.Revision -or
        (Available-Version $update ([string]$entry.Kind)) -cne [string]$entry.AvailableVersion -or [string]$update.Title -cne [string]$entry.Name) {
        Result 'Rejected' 'Seçilen güncellemenin sürüm veya kimlik bilgisi değişti; yeniden kontrol edin.'; return
    }
    if ($update.InstallationBehavior.CanRequestUserInput) { Result 'Rejected' 'Bu güncelleme etkileşimli kurulum gerektiriyor; Windows Update üzerinden kurun.'; return }
    $collection = New-Object -ComObject Microsoft.Update.UpdateColl
    [void]$collection.Add($update)
    $installer = $session.CreateUpdateInstaller()
    $installer.Updates = $collection
    $installer.IsForced = $false
    $installer.AllowSourcePrompts = $false
    $installer.ForceQuiet = $true
    if ($installer.IsBusy) { Result 'Busy' 'Windows Update başka bir kurulum yürütüyor.'; return }
    if ($installer.RebootRequiredBeforeInstallation) { $reboot=$true; Result 'Rejected' 'Kurulumdan önce Windows yeniden başlatılmalı; kurulum başlatılmadı.'; return }
    # The UI confirmation explicitly includes the selected update's license consent.
    if (-not $update.EulaAccepted) { $update.AcceptEula() }
    if (-not $update.IsDownloaded) {
        Progress 'Yalnızca seçilen güncelleme Windows Update üzerinden indiriliyor…'
        $downloader = $session.CreateUpdateDownloader()
        $downloader.Updates = $collection
        $downloader.IsForced = $false
        $download = $downloader.Download()
        if ([int]$download.ResultCode -ne 2 -or [int]$download.GetUpdateResult(0).ResultCode -ne 2 -or -not $update.IsDownloaded) {
            Result 'Failed' 'Seçilen güncellemenin indirilmesi başarıyla tamamlanmadı.'; return
        }
    }
    if ($systemInfo.RebootRequired -or $installer.RebootRequiredBeforeInstallation) { $reboot=$true; Result 'Rejected' 'Windows yeniden başlatma bekliyor; indirilen güncellemenin kurulumu ertelendi.'; return }
    if ($installer.IsBusy) { Result 'Busy' 'Windows Update başka bir kurulum başlattı; seçilen kurulum ertelendi.'; return }
    Progress 'Yalnızca seçilen güncelleme kuruluyor; işlem tamamlanana kadar bekleniyor…'
    # Synchronous servicing continues without timeout/cancellation. This script never reboots the PC.
    $operationStarted = $true
    $installation = $installer.Install()
    $specific = $installation.GetUpdateResult(0)
    $reboot = [bool]$installation.RebootRequired -or [bool]$specific.RebootRequired
    if ([int]$installation.ResultCode -eq 2 -and [int]$specific.ResultCode -eq 2 -and [int]$specific.HResult -eq 0) {
        Result 'Installed' 'Windows Update seçilen güncellemenin başarıyla kurulduğunu bildirdi. Yeniden başlatma gerekiyorsa siz başlatabilirsiniz.' $true
    } else {
        Result 'Failed' ('Seçilen güncelleme kurulumu başarıyla tamamlanmadı. Windows Update sonucu: ' + [int]$specific.ResultCode + '; HRESULT: ' + [int]$specific.HResult + '.')
    }
} catch {
    $failureDetail = $_.Exception.Message
    if ($operationStarted) {
        try { $reboot = [bool](New-Object -ComObject Microsoft.Update.SystemInfo).RebootRequired } catch { }
        Result 'Unknown' ('Kurulum başladı fakat tamamlanma sonucu doğrulanamadı. Yeniden kontrol edin. ' + $failureDetail)
    } else {
        Result 'Failed' ('Kurulum başlatılamadı veya hazırlık tamamlanamadı. ' + $failureDetail)
    }
}
# Downloads/cache are owned by Windows Update/WinGet. Never delete their files or user caches.
