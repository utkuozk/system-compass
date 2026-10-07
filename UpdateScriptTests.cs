using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace SistemPusulasi;

internal static class UpdateScriptTests
{
    internal static async Task<IReadOnlyList<string>> Run()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("SistemPusulasi.Updates.Install.ps1")
            ?? throw new InvalidOperationException("Embedded update script was not found for helper fixtures.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var source = await reader.ReadToEndAsync().ConfigureAwait(false);
        var sourceBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(source));
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new InvalidOperationException("Windows PowerShell is unavailable for update helper fixtures.");

        var wrapperPath = Path.Combine(Path.GetTempPath(), "SystemCompass-UpdateScriptTests-" + Guid.NewGuid().ToString("N") + ".ps1");
        string output;
        string error;
        int exitCode;
        try
        {
            File.WriteAllText(wrapperPath, BuildFixtureScript(sourceBase64), new UTF8Encoding(false));
            var info = new ProcessStartInfo(powershell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", wrapperPath })
                info.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = info };
            if (!process.Start()) throw new InvalidOperationException("Could not start the isolated helper fixture process.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("PowerShell helper fixtures exceeded 20 seconds.");
            }
            output = await stdoutTask.ConfigureAwait(false);
            error = await stderrTask.ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        finally { try { File.Delete(wrapperPath); } catch { } }
        if (exitCode != 0)
            throw new InvalidOperationException("Update helper fixtures failed: " + Clip(error + "\n" + output));
        var outputLines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var falseJson = outputLines.SingleOrDefault(line => line.StartsWith("FIXTURE_RESULT_FALSE:", StringComparison.Ordinal))?[21..];
        var trueJson = outputLines.SingleOrDefault(line => line.StartsWith("FIXTURE_RESULT_TRUE:", StringComparison.Ordinal))?[20..];
        if (falseJson == null || trueJson == null)
            throw new InvalidOperationException("Update helper fixtures omitted the captured Result records: " + Clip(output));
        var parsedFalse = UpdateInstaller.ParseResult(falseJson, 0);
        var parsedTrue = UpdateInstaller.ParseResult(trueJson, 0);
        if (parsedFalse.Status != "Installed" || parsedFalse.RebootRequired ||
            parsedTrue.Status != "Installed" || !parsedTrue.RebootRequired)
            throw new InvalidOperationException("Captured Result records failed UpdateInstaller.ParseResult validation.");

        var results = outputLines
            .Where(line => line.StartsWith("PASS: ", StringComparison.Ordinal)).ToArray();
        if (results.Length != 34)
            throw new InvalidOperationException("Update helper fixtures returned incomplete results: " + Clip(output));
        return results;
    }

    private static string BuildFixtureScript(string sourceBase64)
    {
        // Production code is parsed as data. Only the four named FunctionDefinitionAst
        // extents are evaluated; the installer's top-level body can never run here.
        return $$"""
$ErrorActionPreference = 'Stop'
$source = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{sourceBase64}}'))
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw 'Embedded update script did not parse.' }
$names = @('Result','Winget-InstallerType','Winget-UpgradeArguments','Winget-InstalledVersion')
$functions = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $names -contains $node.Name }, $true) | Sort-Object { $_.Extent.StartOffset })
if ($functions.Count -ne $names.Count -or @($functions.Name | Select-Object -Unique).Count -ne $names.Count) { throw 'Expected helper functions were not found exactly once.' }
$functionCode = ($functions | ForEach-Object { $_.Extent.Text }) -join "`n"
$results = [System.Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw ('Fixture failed: ' + $name) }
    $results.Add('PASS: ' + $name)
}
& {
    $reboot = $false
    Invoke-Expression $functionCode
    $oldOut = [Console]::Out
    $capture = [System.IO.StringWriter]::new()
    try {
        [Console]::SetOut($capture)
        Result 'Installed' 'fixture' $true
    } finally { [Console]::SetOut($oldOut) }
    $falseJson = $capture.ToString().Trim() -replace '^PUSULA_RESULT:', ''
    Write-Output ('FIXTURE_RESULT_FALSE:' + $falseJson)
    $falseResult = $falseJson | ConvertFrom-Json
    Check ($falseResult.Status -eq 'Installed' -and $falseResult.RebootRequired -ceq $false -and $falseResult.Verified -ceq $true) 'Result emits reboot false in the embedded wrapper scope'

    $reboot = $true
    $capture = [System.IO.StringWriter]::new()
    try {
        [Console]::SetOut($capture)
        Result 'Installed' 'fixture' $true
    } finally { [Console]::SetOut($oldOut) }
    $trueJson = $capture.ToString().Trim() -replace '^PUSULA_RESULT:', ''
    Write-Output ('FIXTURE_RESULT_TRUE:' + $trueJson)
    $trueResult = $trueJson | ConvertFrom-Json
    Check ($trueResult.Status -eq 'Installed' -and $trueResult.RebootRequired -ceq $true -and $trueResult.Verified -ceq $true) 'Result emits reboot true from the embedded wrapper scope'

    Check ((Winget-InstallerType "    Installer Type: msi`n") -ceq 'msi') 'Installer type parses English metadata'
    Check ((Winget-InstallerType "    Yükleyici Türü: wix`n") -ceq 'wix') 'Installer type parses Turkish metadata'
    Check ((Winget-InstallerType "    Installer Type: msi`n    Installer Type: inno") -ceq '') 'Ambiguous installer types are rejected'
    Check ((Winget-InstallerType '    Installer Type: exe') -ceq '') 'Unsupported installer type is rejected'

    $header = 'Name'.PadRight(16) + 'Id'.PadRight(36) + 'Version'.PadRight(48) + 'Available'
    $separator = '-' * 60
    $row = 'Example App'.PadRight(16) + 'Example.App'.PadRight(36) + '1.2.3'.PadRight(48) + '2.0.0'
    $installedOutput = $header + "`n" + $separator + "`n" + $row
    Check ((Winget-InstalledVersion $installedOutput 'Example.App') -ceq '1.2.3') 'Installed version helper extracts exact package version'
    Check ((Winget-InstalledVersion ($installedOutput + "`n" + $row) 'Example.App') -ceq '') 'Installed version helper rejects duplicate package rows'

    foreach ($type in @('msix','msi','wix','burn','inno')) {
        $upgradeArgs = @(Winget-UpgradeArguments 'Example.App' '2.0.0' $type)
        $expectedArgs = @('upgrade','--id','Example.App','--exact','--source','winget','--version','2.0.0','--installer-type',$type,'--silent','--disable-interactivity','--skip-dependencies','--accept-package-agreements','--accept-source-agreements')
        if ($type -in @('msi','wix')) { $expectedArgs += @('--custom','/norestart REBOOT=ReallySuppress') }
        elseif ($type -in @('burn','inno')) { $expectedArgs += @('--custom','/norestart') }
        Check (($upgradeArgs -join [char]0) -ceq ($expectedArgs -join [char]0)) ('Upgrade arguments match the fixed safe sequence: ' + $type)
        $typeIndex = [Array]::IndexOf($upgradeArgs, '--installer-type')
        Check ($typeIndex -ge 0 -and $upgradeArgs[$typeIndex + 1] -ceq $type) ('Upgrade enforces installer type: ' + $type)
        Check ($upgradeArgs -contains '--exact' -and $upgradeArgs -contains '--source' -and $upgradeArgs -contains 'winget' -and $upgradeArgs -contains '--version' -and $upgradeArgs -contains '2.0.0') ('Upgrade pins exact identity, source, and version: ' + $type)
        Check (-not ($upgradeArgs | Where-Object { $_ -in @('--allow-reboot','--reboot','--force','--all','--allow-unknown','--override') })) ('Upgrade excludes reboot, force, and broad-install switches: ' + $type)
        $customIndex = [Array]::IndexOf($upgradeArgs, '--custom')
        if ($type -in @('msi','wix')) {
            Check ($customIndex -ge 0 -and $upgradeArgs[$customIndex + 1] -ceq '/norestart REBOOT=ReallySuppress') ('MSI-family upgrade suppresses restart: ' + $type)
        } elseif ($type -in @('burn','inno')) {
            Check ($customIndex -ge 0 -and $upgradeArgs[$customIndex + 1] -ceq '/norestart') ('Burn/Inno upgrade suppresses restart: ' + $type)
        } else {
            Check ($customIndex -lt 0) 'MSIX upgrade has no EXE-specific switches'
        }
    }
    $threw = $false
    try { $null = Winget-UpgradeArguments 'Example.App' '2.0.0' 'exe' } catch { $threw = $true }
    Check $threw 'Upgrade arguments reject unsupported installer type'
}
$results
""";
    }

    private static string Clip(string value) => value.Length <= 3000 ? value : value[..3000];
}
