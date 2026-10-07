param([ValidateSet('read', 'update', 'quick', 'full')][string]$Action = 'read')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

# Only documented Defender status/cmdlets are used. SecurityCenter2 supplies display
# names only: productState and executable/resource paths are deliberately not read.
function Get-Field($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}
function Get-Bool($Object, [string]$Name) {
    $value = Get-Field $Object $Name
    if ($value -is [bool]) { return $value }
    return $null
}
function Get-Date($Object, [string]$Name) {
    $value = Get-Field $Object $Name
    if ($value -is [datetime] -and $value.Year -gt 2000) { return $value.ToUniversalTime().ToString('o') }
    return $null
}
function Get-Snapshot {
    $result = [ordered]@{
        SchemaVersion = 1
        CheckedUtc = [datetime]::UtcNow.ToString('o')
        ProvidersKnown = $false
        Providers = @()
        DefenderKnown = $false
        Defender = $null
        ThreatsKnown = $false
        Threats = @()
        Action = [ordered]@{ Name = $Action; Result = 'None' }
    }
    try {
        $products = @(Get-CimInstance -Namespace 'root/SecurityCenter2' -ClassName 'AntiVirusProduct' -ErrorAction Stop)
        $names = @()
        $valid = $products.Count -gt 0
        foreach ($product in $products) {
            $name = Get-Field $product 'displayName'
            if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name)) { $valid = $false; continue }
            $names += $name.Trim()
        }
        $result.Providers = @($names | Sort-Object -Unique)
        $result.ProvidersKnown = $valid
    } catch { }
    try {
        $status = Get-MpComputerStatus -ErrorAction Stop
        if ($null -ne $status) {
            $result.Defender = [ordered]@{
                AMRunningMode = [string](Get-Field $status 'AMRunningMode')
                AMServiceEnabled = Get-Bool $status 'AMServiceEnabled'
                AntivirusEnabled = Get-Bool $status 'AntivirusEnabled'
                RealTimeProtectionEnabled = Get-Bool $status 'RealTimeProtectionEnabled'
                AntivirusSignatureVersion = [string](Get-Field $status 'AntivirusSignatureVersion')
                AntivirusSignatureLastUpdated = Get-Date $status 'AntivirusSignatureLastUpdated'
                QuickScanStartTime = Get-Date $status 'QuickScanStartTime'
                QuickScanEndTime = Get-Date $status 'QuickScanEndTime'
                FullScanStartTime = Get-Date $status 'FullScanStartTime'
                FullScanEndTime = Get-Date $status 'FullScanEndTime'
            }
            $result.DefenderKnown = $true
        }
    } catch { }
    try {
        $threats = @(Get-MpThreat -ErrorAction Stop)
        $valid = $true
        $items = @()
        foreach ($threat in $threats) {
            $name = Get-Field $threat 'ThreatName'
            $active = Get-Bool $threat 'IsActive'
            if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or $null -eq $active) { $valid = $false; continue }
            # Never serialize Resources, arbitrary file paths, or the whole CIM object.
            $items += [ordered]@{ Name = $name; IsActive = $active }
        }
        $result.Threats = @($items)
        $result.ThreatsKnown = $valid
    } catch { }
    return $result
}
function Test-Eligible($Snapshot) {
    if (-not $Snapshot.ProvidersKnown -or -not $Snapshot.DefenderKnown) { return $false }
    if ($Snapshot.Providers.Count -eq 0) { return $false }
    # Exact known Defender names only. Unknown or third-party registrations block
    # actions even if their current protection state cannot be determined.
    foreach ($name in $Snapshot.Providers) {
        if ($name -notin @('Windows Defender', 'Windows Defender Antivirus', 'Microsoft Defender Antivirus')) { return $false }
    }
    return ($Snapshot.Defender.AMRunningMode -ceq 'Normal' -and
        $Snapshot.Defender.AMServiceEnabled -eq $true -and
        $Snapshot.Defender.AntivirusEnabled -eq $true)
}

$snapshot = Get-Snapshot
if ($Action -ne 'read') {
    $mutex = $null
    $ownsMutex = $false
    $outcome = 'Failed'
    try {
        # Acquisition and release stay on this synchronous PowerShell thread.
        # The system-wide name also serializes normal/elevated app processes.
        $mutex = New-Object System.Threading.Mutex($false, 'Global\SistemPusulasi.AntivirusAction.v1')
        try { $ownsMutex = $mutex.WaitOne(0) }
        catch [System.Threading.AbandonedMutexException] { $ownsMutex = $true }
        if (-not $ownsMutex) { $outcome = 'Busy' }
        else {
            $snapshot = Get-Snapshot
            if (-not (Test-Eligible $snapshot)) { $outcome = 'Blocked' }
            else {
                # Use the machine's configured signature sources. Never change
                # preferences, activate Defender, choose paths, or upload files.
                Update-MpSignature -ErrorAction Stop | Out-Null
                $outcome = 'Updated'
                if ($Action -in @('quick', 'full')) {
                    # Recheck provider/mode after the potentially lengthy update.
                    $snapshot = Get-Snapshot
                    if (-not (Test-Eligible $snapshot)) { $outcome = 'BlockedAfterUpdate' }
                    else {
                        $scanType = if ($Action -eq 'quick') { 'QuickScan' } else { 'FullScan' }
                        $outcome = 'ScanFailed'
                        Start-MpScan -ScanType $scanType -ErrorAction Stop | Out-Null
                        # A returned cmdlet is not a clean verdict or independent
                        # proof that a full scan completed: expose snapshot dates.
                        $outcome = 'Submitted'
                    }
                }
            }
        }
    } catch {
        # Exception messages may contain local resources/paths; report a fixed
        # outcome and let Windows Security show detailed engine diagnostics.
        if ($outcome -ne 'ScanFailed') { $outcome = 'Failed' }
    } finally {
        try {
            # Capture the actual post-action state, including after errors.
            $snapshot = Get-Snapshot
            $snapshot.Action = [ordered]@{ Name = $Action; Result = $outcome }
        } finally {
            if ($ownsMutex -and $null -ne $mutex) { $mutex.ReleaseMutex() }
            if ($null -ne $mutex) { $mutex.Dispose() }
        }
    }
}
$snapshot | ConvertTo-Json -Depth 6 -Compress
