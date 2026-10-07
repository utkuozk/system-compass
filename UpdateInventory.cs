using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SistemPusulasi;

public sealed record UpdateEntry(string Name, string InstalledVersion, string AvailableVersion, string Source, string Kind)
{
    public string PackageId {get;init;}="";
    public string UpdateId {get;init;}="";
    public int Revision {get;init;}
}

public sealed class UpdateInventoryResult
{
    public List<UpdateEntry> Entries { get; set; } = new();
    public string Status { get; set; } = "Unknown: Henüz kontrol edilmedi.";
    public DateTimeOffset CheckedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Independent, read-only inventories. Status describes coverage, never inferred from an empty list.</summary>
public static class UpdateInventory
{
    private const int OutputLimit = 4 * 1024 * 1024;
    private static readonly InventoryScanGate ScanGate = new();

    // Even resource loading, process setup and JSON parsing must stay off the caller's UI thread.
    public static Task<UpdateInventoryResult> ScanAsync(string kind, CancellationToken cancellationToken = default,
        IProgress<string>? progress = null) => Task.Run(() => ScanCoreAsync(kind, cancellationToken, progress));

    private static async Task<UpdateInventoryResult> ScanCoreAsync(string kind, CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        kind = (kind ?? "").Trim().ToLowerInvariant();
        if (kind is not ("windows" or "software" or "drivers"))
            return Unknown("Geçersiz kontrol türü: windows, software veya drivers bekleniyor.");
        using var scanLease = await ScanGate.EnterAsync(kind, cancellationToken, progress).ConfigureAwait(false);
        using var captureCancellation = new CancellationTokenSource();
        Process? process = null;
        Task<string>? outputTask = null;
        Task<string>? errorTask = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Güncelleme taraması başlatılıyor…");
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("SistemPusulasi.Updates.Inventory.ps1");
            if (resource == null) return Unknown("Güncelleme kontrolü kaynağı uygulamada bulunamadı.");
            using var reader = new StreamReader(resource, Encoding.UTF8);
            var script = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            // Execute trusted assembly content in memory, including from an elevated scheduled worker.
            var command = "& {\n" + script + "\n} -Kind '" + kind + "'";
            if (command.Length + command.Count(x => x == '"') + 1000 >= 30000)
                return Unknown("Güncelleme kontrolü komut boyutu sınırını aştı.");

            // Use an existing PowerShell only. Never provision WinGet, modules, sources, or agreements.
            var modernShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
            var shell = kind == "software" && File.Exists(modernShell) ? modernShell :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(shell)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command })
                start.ArgumentList.Add(arg);
            process = new Process { StartInfo = start };
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start()) return Unknown("Güncelleme kontrolü başlatılamadı.");
            process.StandardInput.Close();
            outputTask = CaptureAsync(process.StandardOutput, captureCancellation.Token);
            errorTask = CaptureAsync(process.StandardError, captureCancellation.Token);
            progress?.Report(kind == "software" ? "Yazılım kaynaklarının yanıtı bekleniyor…" : "Windows Update hizmetinin yanıtı bekleniyor…");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(kind == "software" ? 90 : 150));
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try { await process.WaitForExitAsync(waitCancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                return Unknown("Kontrol zaman aşımına uğradı; güncelleme durumu doğrulanamadı.");
            }
            // Inherited pipe handles must not keep a completed scan waiting forever.
            string[] captured;
            var captureTask = Task.WhenAll(outputTask, errorTask);
            ObserveFault(captureTask);
            try { captured = await captureTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false); }
            catch (TimeoutException) { return Unknown("Kontrol çıktısı zamanında tamamlanmadı; sonuç doğrulanamadı."); }
            cancellationToken.ThrowIfCancellationRequested();
            var output = captured[0];
            var error = captured[1];
            if (process.ExitCode != 0) return Unknown("Kontrol tamamlanamadı. " + Clip(error));
            if (output.Length >= OutputLimit) return Unknown("Kontrol çıktısı sınırı aştı; sonuç doğrulanamadı.");
            progress?.Report("Tarama sonuçları yorumlanıyor…");
            var result = ParseResult(output, kind);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { return Unknown("Güncelleme durumu okunamadı. " + Clip(ex.Message)); }
        finally
        {
            if (process != null)
            {
                await StopAndDrainAsync(process, captureCancellation, outputTask, errorTask).ConfigureAwait(false);
                process.Dispose();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    // Pure validation helper for self-tests: an empty, malformed or partial payload cannot imply success.
    internal static UpdateInventoryResult ParseResult(string output, string kind)
    {
        try
        {
            using var document = JsonDocument.Parse(output.Trim());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Entries", out var entries) || entries.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("Status", out var status) || status.ValueKind != JsonValueKind.String)
                return Unknown("Kontrol geçerli bir veri ve durum bildirmedi.");
            var result = JsonSerializer.Deserialize<UpdateInventoryResult>(root.GetRawText());
            if (result == null || !(result.Status.StartsWith("Checked:", StringComparison.Ordinal) || result.Status.StartsWith("Unknown:", StringComparison.Ordinal)))
                return Unknown("Kontrol geçerli bir durum bildirmedi.");
            if (result.Entries == null || result.Entries.Any(x => x == null || x.Kind != kind || string.IsNullOrWhiteSpace(x.Name) ||
                string.IsNullOrWhiteSpace(x.InstalledVersion) || string.IsNullOrWhiteSpace(x.AvailableVersion) || string.IsNullOrWhiteSpace(x.Source)))
                return Unknown("Kontrol verisi eksik veya beklenen türle uyuşmuyor.");
            result.CheckedUtc = DateTimeOffset.UtcNow;
            if (kind == "software" && root.TryGetProperty("CliOutput", out var cli) && cli.ValueKind == JsonValueKind.String &&
                root.TryGetProperty("CliExitCode", out var cliExit) && cliExit.TryGetInt32(out var exitCode))
            {
                var visibleUpdates = ParseWingetOutput(cli.GetString() ?? "", exitCode);
                if (visibleUpdates.Entries.Count > 0) return visibleUpdates;
                result.Status += " WinGet görünür güncelleme tablosu da doğrulanamadı; boş çıktı güncel olduğunuz anlamına gelmez.";
            }
            return result;
        }
        catch (Exception ex) { return Unknown("Kontrol çıktısı yorumlanamadı. " + Clip(ex.Message)); }
    }

    internal static UpdateInventoryResult ParseWingetOutput(string output, int exitCode)
    {
        var result = Unknown("WinGet görünür güncelleme tablosu doğrulanamadı.");
        if (exitCode != 0) return result;
        var lines = Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", "").Replace("\r", "").Split('\n');
        for (var index = 1; index < lines.Length; index++)
        {
            if (!Regex.IsMatch(lines[index].Trim(), @"^-{8,}$")) continue;
            var columns = Regex.Matches(lines[index - 1], @"\S(?:.*?\S)?(?=\s{2,}|$)").Cast<Match>().ToArray();
            if (columns.Length is not (4 or 5) || columns[0].Index != 0) continue;
            for (var row = index + 1; row < lines.Length; row++)
            {
                var line = lines[row];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Length <= columns[^1].Index) break;
                var cells = columns.Select((column, number) => line.Substring(column.Index,
                    Math.Max(0, Math.Min(line.Length, number + 1 < columns.Length ? columns[number + 1].Index : line.Length) - column.Index)).Trim()).ToArray();
                if (cells.Any(string.IsNullOrWhiteSpace) || !Regex.IsMatch(cells[1], @"^[A-Za-z0-9][A-Za-z0-9_.-]*$") ||
                    !Regex.IsMatch(cells[2], @"^[vV]?\d[A-Za-z0-9._+~:-]*$") ||
                    !Regex.IsMatch(cells[3], @"^[vV]?\d[A-Za-z0-9._+~:-]*$") ||
                    cells[2].Contains('…') || cells[3].Contains('…')) break;
                result.Entries.Add(new(cells[0], cells[2], cells[3], "WinGet CLI (sınırlı görünür liste)", "software") {PackageId=cells[1]});
            }
            // CLI display is localized and may omit pinned/unmatched packages; never claim a complete scan.
            if (result.Entries.Count > 0)
            {
                result.Status = "Checked: WinGet görünür listesinde " + result.Entries.Count + " yazılım güncellemesi bulundu. Bu sınırlı liste tam envanter değildir; sabitlenmiş, eşleştirilemeyen veya diğer kaynaklardaki programların durumu bilinmiyor.";
                return result;
            }
        }
        return result;
    }

    private static async Task<string> CaptureAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            var remaining = OutputLimit - text.Length;
            if (remaining > 0) text.Append(buffer, 0, Math.Min(count, remaining));
        }
        return text.ToString();
    }

    private static async Task StopAndDrainAsync(Process process, CancellationTokenSource captureCancellation,
        Task<string>? outputTask, Task<string>? errorTask)
    {
        // This Process belongs only to the trusted, read-only inventory command above.
        TryKill(process);
        using (var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            try { await process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false); }
            catch { /* Unstarted process or a process that could not be stopped. */ }
        }
        var captures = new[] { outputTask, errorTask }.Where(task => task != null).Cast<Task<string>>().ToArray();
        if (captures.Length == 0) return;
        var drain = Task.WhenAll(captures);
        try { await drain.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch { /* Cancellation/pipe errors are observed by the awaited task. */ }
        finally
        {
            captureCancellation.Cancel();
            try { process.StandardOutput.Close(); } catch { }
            try { process.StandardError.Close(); } catch { }
            // A stream implementation may complete after the bounded drain. Observe every late fault.
            ObserveFault(drain);
            foreach (var capture in captures) ObserveFault(capture);
        }
    }

    private static void ObserveFault(Task task) => _ = task.ContinueWith(completed => { _ = completed.Exception; },
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }
    private static string Clip(string value) => value.Length > 700 ? value[..700] : value;
    private static UpdateInventoryResult Unknown(string detail) => new() { Status = "Unknown: " + detail, CheckedUtc = DateTimeOffset.UtcNow };
}
