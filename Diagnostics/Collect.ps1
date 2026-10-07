$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$ProgressPreference = 'SilentlyContinue'
$checks = New-Object 'System.Collections.Generic.List[object]'
function Check($name, [scriptblock]$body) {
    try { $data = @(& $body); $checks.Add([pscustomobject]@{ Name=$name; Ok=$true; Data=$(if ($name -eq 'Guard') { $data | Select-Object -First 1 } else { ,$data }); Error=$null }) }
    catch { $checks.Add([pscustomobject]@{ Name=$name; Ok=$false; Data=$null; Error=$_.Exception.Message }) }
}
function Events($log, $provider, $from, $to, $limit, $levels) {
    $filter = @{LogName=$log; ProviderName=$provider; StartTime=$from; EndTime=$to}
    if ($levels) { $filter.Level=$levels }
    try { @(Get-WinEvent -FilterHashtable $filter -MaxEvents $limit -ErrorAction Stop | ForEach-Object {
        [pscustomobject]@{ Id=$_.Id; Level=$_.Level; Provider=$_.ProviderName; Time=$_.TimeCreated.ToUniversalTime().ToString('o'); Message=([string]$_.Message).Substring(0,[Math]::Min(1200,([string]$_.Message).Length)) }
    }) } catch { if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { @() } else { throw } }
}
Check 'Guard' {
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class PusulaPower { [StructLayout(LayoutKind.Sequential)] public struct Status { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; } [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out Status s); }'
    $power = New-Object PusulaPower+Status
    if (-not [PusulaPower]::GetSystemPowerStatus([ref]$power)) { throw 'Güç durumu okunamadı.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $admin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
    $sessionManager = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -ErrorAction Stop
    $pending = $pending -or ($null -ne $sessionManager.PendingFileRenameOperations -and @($sessionManager.PendingFileRenameOperations).Count -gt 0)
    $active = @(Get-Process -Name dism,DismHost,sfc -ErrorAction SilentlyContinue).Count -gt 0
    [pscustomobject]@{ Admin=$admin; AcOnline=($power.ACLineStatus -eq 1); AcKnown=($power.ACLineStatus -in @(0,1)); PendingReboot=[bool]$pending; OtherServicing=[bool]$active }
}
if ($env:PUSULA_GUARD_ONLY -eq '1') { ConvertTo-Json -InputObject @($checks.ToArray()) -Depth 9 -Compress; exit }
$now = Get-Date
$from = $now.AddDays(-7)
foreach ($provider in @('Microsoft-Windows-WHEA-Logger','disk','Ntfs','stornvme','storahci','Microsoft-Windows-Kernel-Power','Microsoft-Windows-Kernel-Processor-Power','Service Control Manager','Microsoft-Windows-WER-SystemErrorReporting','Display')) {
    Check ('Events:'+$provider) { Events 'System' $provider $from $now 40 @(1,2,3) }
    Check ('FutureEvents:'+$provider) { Events 'System' $provider $now.AddSeconds(1) ([datetime]::new(9998,12,31,23,59,59)) 5 $null }
}
Check 'Reliability' {
    $lower = [Management.ManagementDateTimeConverter]::ToDmtfDateTime($from)
    $upper = [Management.ManagementDateTimeConverter]::ToDmtfDateTime($now)
    @(Get-CimInstance Win32_ReliabilityRecords -Filter "TimeGenerated >= '$lower' AND TimeGenerated <= '$upper'" | Sort-Object TimeGenerated -Descending | Select-Object -First 60 | ForEach-Object {
        [pscustomobject]@{ Source=$_.SourceName; EventId=$_.EventIdentifier; Time=$_.TimeGenerated.ToUniversalTime().ToString('o'); Message=([string]$_.Message).Substring(0,[Math]::Min(1000,([string]$_.Message).Length)) }
    })
}
Check 'Disks' {
    @(Get-PhysicalDisk | ForEach-Object {
        $disk=$_; $counter=$null; $reliabilityError=$null
        try { $counter=$disk | Get-StorageReliabilityCounter -ErrorAction Stop } catch { $reliabilityError=$_.Exception.Message }
        [pscustomobject]@{ Name=$disk.FriendlyName; Health=[string]$disk.HealthStatus; Operational=@($disk.OperationalStatus | ForEach-Object { [string]$_ }); SizeBytes=$disk.Size; Temperature=$counter.Temperature; Wear=$counter.Wear; ReadErrorsUncorrected=$counter.ReadErrorsUncorrected; WriteErrorsUncorrected=$counter.WriteErrorsUncorrected; PowerOnHours=$counter.PowerOnHours; ReliabilityError=$reliabilityError }
    })
}
Check 'Volumes' { @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Select-Object DeviceID,Size,FreeSpace) }
Check 'Devices' { @(Get-CimInstance Win32_PnPEntity -Filter 'ConfigManagerErrorCode <> 0' | Select-Object -First 100 Name,ConfigManagerErrorCode,Status) }
Check 'Cpu' {
    $samples=@()
    for ($i=0; $i -lt 3; $i++) {
        $perf=$null
        try { $perf=Get-CimInstance Win32_PerfFormattedData_Counters_ProcessorInformation -Filter "Name = '_Total'" } catch { }
        $samples += @(Get-CimInstance Win32_Processor | ForEach-Object {
            $live=$null
            if ($null -ne $perf.ProcessorFrequency -and $null -ne $perf.PercentProcessorPerformance -and $perf.ProcessorFrequency -gt 0 -and $perf.PercentProcessorPerformance -gt 0) { $live=[double]$perf.ProcessorFrequency * [double]$perf.PercentProcessorPerformance / 100 }
            [pscustomobject]@{ Name=$_.Name; LoadPercentage=$_.LoadPercentage; ReportedMHz=$_.CurrentClockSpeed; MaxClockMHz=$_.MaxClockSpeed; EstimatedLiveMHz=$live; Utility=$perf.PercentProcessorUtility }
        })
        if ($i -lt 2) { Start-Sleep -Seconds 1 }
    }
    $samples
}
Check 'Memory' { Get-CimInstance Win32_OperatingSystem | Select-Object TotalVisibleMemorySize,FreePhysicalMemory,LastBootUpTime }
Check 'MemoryTest' { Events 'System' 'Microsoft-Windows-MemoryDiagnostics-Results' $now.AddDays(-120) $now 8 $null }
ConvertTo-Json -InputObject @($checks.ToArray()) -Depth 9 -Compress
