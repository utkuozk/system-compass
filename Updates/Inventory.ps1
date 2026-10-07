param([ValidateSet('windows','software','drivers')][string]$Kind)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$InformationPreference = 'SilentlyContinue'
$WarningPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$entries = New-Object System.Collections.Generic.List[object]
$status = 'Unknown: Kontrol tamamlanmadı.'
$cliOutput = ''; $cliExitCode = -1

function Add-Entry([string]$name, [string]$installed, [string]$available, [string]$source, [string]$packageId="", [string]$updateId="", [int]$revision=0) {
    if ([string]::IsNullOrWhiteSpace($installed)) { $installed = 'Bilinmiyor' }
    if ([string]::IsNullOrWhiteSpace($available)) { $available = 'Bilinmiyor' }
    $entries.Add([pscustomobject]@{ Name=$name; InstalledVersion=$installed; AvailableVersion=$available; Source=$source; Kind=$Kind; PackageId=$packageId; UpdateId=$updateId; Revision=$revision })
}

function Read-InstalledPrograms {
    $readFailures = 0
    $seen = @{}
    foreach ($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryHive]::CurrentUser)) {
        foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
            $base = $null; $uninstall = $null
            try {
                $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
                $uninstall = $base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')
                if ($null -eq $uninstall) { continue }
                foreach ($keyName in $uninstall.GetSubKeyNames()) {
                    $key = $null
                    try {
                        $key = $uninstall.OpenSubKey($keyName)
                        if ($null -eq $key) { continue }
                        $name = [string]$key.GetValue('DisplayName')
                        if ([string]::IsNullOrWhiteSpace($name) -or [int]$key.GetValue('SystemComponent',0) -eq 1) { continue }
                        $version = [string]$key.GetValue('DisplayVersion')
                        $identity = $name + '|' + $version
                        if (-not $seen.ContainsKey($identity)) {
                            $seen[$identity] = $true
                            Add-Entry $name $version 'Bilinmiyor (availabilityUnknown)' 'Windows kurulu program kaydı'
                        }
                    } catch { $readFailures++ } finally { if ($null -ne $key) { $key.Dispose() } }
                }
            } catch { $readFailures++ } finally {
                if ($null -ne $uninstall) { $uninstall.Dispose() }
                if ($null -ne $base) { $base.Dispose() }
            }
        }
    }
    return $readFailures
}

try {
    if ($Kind -eq 'software') {
        $reason = ''
        try {
            if (-not (Get-Module -ListAvailable -Name Microsoft.WinGet.Client)) { throw 'Microsoft.WinGet.Client modülü kurulu değil.' }
            Import-Module Microsoft.WinGet.Client -ErrorAction Stop | Out-Null
            # Explicit source avoids msstore agreement prompts; no acceptance or installation calls.
            $packages = @(Get-WinGetPackage -Source winget -ErrorAction Stop)
            if ($packages.Count -eq 0) { throw 'WinGet karşılaştırılabilir kurulu program verisi döndürmedi.' }
            $unknown = 0
            foreach ($package in $packages) {
                if (-not $package.IsUpdateAvailable) {
                    if ([string]::IsNullOrWhiteSpace([string]$package.Source) -or $package.Id -match '^(ARP|MSIX)\\') { $unknown++ }
                    continue
                }
                $available = @($package.AvailableVersions | Where-Object { $package.CompareToVersion([string]$_).ToString() -eq 'Lesser' })
                if ($available.Count -eq 0) { $unknown++; Add-Entry ([string]$package.Name) ([string]$package.InstalledVersion) 'Güncelleme var; sürüm belirlenemedi' 'WinGet / winget'; continue }
                # AvailableVersions has no guaranteed order. List proven newer versions rather than guessing latest.
                Add-Entry ([string]$package.Name) ([string]$package.InstalledVersion) ($available -join ', ') 'WinGet / winget' ([string]$package.Id)
            }
            $status = 'Checked: WinGet/winget kaynağında ' + $entries.Count + ' güncelleme bulundu. Bu kaynakla eşleştirilemeyen veya diğer kaynaklardaki programların durumu bilinmiyor.'
            if ($unknown -gt 0) { $status += ' ' + $unknown + ' program/sürüm karşılaştırması doğrulanamadı.' }
        } catch {
            $reason = $_.Exception.Message
            $entries.Clear()
            $failures = Read-InstalledPrograms
            $status = 'Unknown: Yazılım güncelleme karşılaştırması kullanılamadı. ' + $reason + ' Yalnızca kurulu programlar listeleniyor; yeni sürümlerin bulunabilirliği bilinmiyor (availabilityUnknown).'
            if ($failures -gt 0) { $status += ' Bazı kurulu program kayıtları da okunamadı.' }
            try {
                $winget = Get-Command winget.exe -CommandType Application -ErrorAction Stop
                # Listing only: no --all, install/update package args or source agreement acceptance.
                $ErrorActionPreference = 'Continue'
                $cliOutput = (& $winget.Source upgrade --source winget --disable-interactivity 2>&1 | Out-String)
                $cliExitCode = $LASTEXITCODE
            } catch {
                $cliOutput = ''; $cliExitCode = -1
                $status += ' WinGet komut satırı kullanılamadı: ' + $_.Exception.Message
            } finally { $ErrorActionPreference = 'Stop' }
        }
    } else {
        $drivers = @()
        if ($Kind -eq 'drivers') {
            try { $drivers = @(Get-CimInstance Win32_PnPSignedDriver -Property DeviceName,DriverVersion,HardWareID -ErrorAction Stop) } catch { }
        }
        $session = New-Object -ComObject Microsoft.Update.Session
        $session.ClientApplicationID = 'Sistem Pusulasi - read-only update inventory'
        $searcher = $session.CreateUpdateSearcher()
        $searcher.Online = $true
        # Respect configured Windows/organization service. No downloads, installs or EULA acceptance.
        $type = if ($Kind -eq 'drivers') { 'Driver' } else { 'Software' }
        $result = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='$type'")
        for ($i=0; $i -lt $result.Updates.Count; $i++) {
            $update = $result.Updates.Item($i)
            $installed = 'Bilinmiyor (WUA bildirmiyor)'
            $available = 'Uygulanabilir güncelleme'
            if ($Kind -eq 'drivers') {
                $hardwareId = [string]$update.DriverHardwareID
                if (-not [string]::IsNullOrWhiteSpace($hardwareId)) {
                    $matches = @($drivers | Where-Object { @($_.HardWareID) -contains $hardwareId } | Select-Object -ExpandProperty DriverVersion -Unique)
                    if ($matches.Count -eq 1) { $installed = [string]$matches[0] }
                }
                # WUA exposes driver date/model, not a reliable universal driver version field.
                $available = 'WUA sürücü önerisi'
                try { if ($null -ne $update.DriverVerDate) { $available += ' (' + ([datetime]$update.DriverVerDate).ToString('yyyy-MM-dd') + ')' } } catch { }
            } else {
                $kb = @($update.KBArticleIDs)
                if ($kb.Count -gt 0) { $available = ($kb | ForEach-Object { 'KB' + $_ }) -join ', ' }
            }
            Add-Entry ([string]$update.Title) $installed $available 'Windows Update Agent (yapılandırılmış kaynak)' '' ([string]$update.Identity.UpdateID) ([int]$update.Identity.RevisionNumber)
        }
        if ([int]$result.ResultCode -eq 2) {
            $status = 'Checked: Yapılandırılmış Windows Update kaynağında ' + $entries.Count + ' uygulanabilir ' + $(if ($Kind -eq 'drivers') { 'sürücü güncellemesi' } else { 'Windows/Microsoft güncellemesi' }) + ' bulundu.'
            if ($Kind -eq 'drivers') { $status += ' Bu kontrol yalnızca Windows Update tarafından sunulan sürücüleri kapsar.' }
        } else {
            $status = 'Unknown: Windows Update araması tam başarıyla tamamlanmadı (sonuç kodu ' + [int]$result.ResultCode + '). Listelenen sonuçlar kısmi olabilir; boş liste güncel olduğunuz anlamına gelmez.'
        }
    }
} catch { $status = 'Unknown: Güncelleme kontrolü doğrulanamadı. ' + $_.Exception.Message }
[pscustomobject]@{ Entries=@($entries.ToArray()); Status=$status; CheckedUtc=[DateTimeOffset]::UtcNow.ToString('o'); CliOutput=$cliOutput; CliExitCode=$cliExitCode } | ConvertTo-Json -Depth 6 -Compress
