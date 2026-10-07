using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SistemPusulasi;

public sealed record UpdateInstallResult(string Status, string Detail, bool RebootRequired = false);

/// <summary>Installs exactly one confirmed selection. Once servicing starts it has no timeout or cancellation.</summary>
public static class UpdateInstaller
{
    private static readonly Regex PackageIdentity = new(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,255}\z", RegexOptions.CultureInvariant);
    private static readonly Regex PackageVersion = new(@"\A[vV]?\d[A-Za-z0-9._+~:-]{0,127}\z", RegexOptions.CultureInvariant);
    private static readonly Regex FirmwareTitle = new(@"\b(BIOS|UEFI|Firmware)\b|üretici yazılımı|ürün yazılımı|bellenim",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private const int CaptureLimit = 65536;

    // Callers must obtain confirmation, including license consent, before invoking this API.
    public static Task<UpdateInstallResult> RunAsync(UpdateEntry entry, Action<string>? progress = null) =>
        Task.Run(() => RunCoreAsync(entry, progress));

    public static string? Validate(UpdateEntry? entry)
    {
        if (entry == null || entry.Kind is not ("windows" or "drivers" or "software"))
            return "Güncelleme türü geçersiz.";
        if (string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 2048 ||
            string.IsNullOrWhiteSpace(entry.AvailableVersion) || entry.AvailableVersion.Length > 2048 ||
            entry.Name.Any(char.IsControl) || entry.AvailableVersion.Any(char.IsControl))
            return "Güncelleme kaydı eksik veya geçersiz.";
        if (entry.Kind == "software")
        {
            if (entry.Source is not ("WinGet / winget" or "WinGet CLI (sınırlı görünür liste)") ||
                !PackageIdentity.IsMatch(entry.PackageId ?? "") || !PackageVersion.IsMatch(entry.AvailableVersion) ||
                !PackageVersion.IsMatch(entry.InstalledVersion ?? "") || entry.InstalledVersion == entry.AvailableVersion ||
                !string.IsNullOrEmpty(entry.UpdateId) || entry.Revision != 0)
                return "Yazılım için winget kaynağı, kesin paket kimliği ve tek bir doğrulanmış sürüm gerekli.";
        }
        else if (entry.Source != "Windows Update Agent (yapılandırılmış kaynak)" ||
            !Guid.TryParseExact(entry.UpdateId, "D", out var id) || id == Guid.Empty ||
            entry.Revision <= 0 || !string.IsNullOrEmpty(entry.PackageId))
            return "Windows Update kimliği ve revizyonu doğrulanamadı; yeniden kontrol edin.";
        if (entry.Kind != "software" && FirmwareTitle.IsMatch(entry.Name))
            return "BIOS/UEFI/firmware güncellemeleri bu uygulamadan kurulmaz; üreticinin yönergelerini kullanın.";
        return null;
    }

    private static async Task<UpdateInstallResult> RunCoreAsync(UpdateEntry entry, Action<string>? progress)
    {
        var validation = Validate(entry);
        if (validation != null) return new("Rejected", validation);
        try
        {
            LocalStore.Initialize();
            // The same maintenance lock is held by DiagnosticsEngine, including its DISM/SFC work.
            // FileShare.None is cross-process and survives UI windows closing while this worker continues.
            using var installLock = AcquireLock("install.lock");
            if (installLock == null) return new("Busy", "Başka bir güncelleme kurulumu çalışıyor; tamamlanmasını bekleyin.");
            using var scanLock = AcquireLock("scan.lock");
            if (scanLock == null) return new("Busy", "Sistem taraması veya onarım çalışıyor; tamamlanmasını bekleyin.");
            Say(progress, "Seçilen güncellemenin kimliği ve kullanılabilirliği yeniden doğrulanıyor…");
            if (entry.Kind == "software")
            {
                // Read-only preflight may time out. No timeout applies to the installation below.
                var current = await UpdateInventory.ScanAsync("software").ConfigureAwait(false);
                var matches = current.Entries.Where(x => x.Kind == entry.Kind && x.PackageId == entry.PackageId &&
                    x.Source == entry.Source && x.InstalledVersion == entry.InstalledVersion &&
                    x.AvailableVersion == entry.AvailableVersion).ToArray();
                if (!current.Status.StartsWith("Checked:", StringComparison.Ordinal) || matches.Length != 1)
                    return new("Rejected", "Seçilen yazılım sürümü artık tek bir kullanılabilir güncelleme olarak doğrulanamadı. Yeniden kontrol edin.");
            }

            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("SistemPusulasi.Updates.Install.ps1");
            if (resource == null) return new("Failed", "Gömülü kurulum kaynağı bulunamadı.");
            using var resourceReader = new StreamReader(resource, Encoding.UTF8);
            var script = await resourceReader.ReadToEndAsync().ConfigureAwait(false);
            var command = "& {\n" + script + "\n}";
            if (command.Length + command.Count(c => c == '"') + 1000 >= 30000)
                return new("Failed", "Kurulum komutu boyut sınırını aştı.");
            var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            var info = new ProcessStartInfo(shell)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command })
                info.ArgumentList.Add(arg);
            // Untrusted selection data never becomes PowerShell source or command arguments.
            info.Environment["PUSULA_SELECTED_UPDATE"] = JsonSerializer.Serialize(entry);
            using var process = new Process { StartInfo = info };
            if (!process.Start()) return new("Failed", "Kurulum işlemi başlatılamadı.");
            process.StandardInput.Close();
            var output = ReadOutputAsync(process.StandardOutput, progress);
            var errors = ReadErrorAsync(process.StandardError);
            // Never kill an active installer, even if output capture fails or the observing UI disappears.
            await process.WaitForExitAsync().ConfigureAwait(false);
            try
            {
                var captured = await output.ConfigureAwait(false);
                var error = await errors.ConfigureAwait(false);
                return ParseResult(captured, process.ExitCode, error);
            }
            catch (Exception ex) { return new("Unknown", "Kurulum işlemi sona erdi fakat sonucu okunamadı. " + Clip(ex.Message)); }
        }
        catch (Exception ex) { return new("Failed", "Kurulum başlatılamadı veya doğrulanamadı. " + Clip(ex.Message)); }
    }

    private static FileStream? AcquireLock(string name)
    {
        try { return new(Path.Combine(LocalStore.Root, name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }

    internal static UpdateInstallResult ParseResult(string output, int exitCode, string error = "")
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (exitCode != 0 || root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("Status", out var status) || status.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("Detail", out var detail) || detail.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("RebootRequired", out var reboot) || reboot.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new("Unknown", "Kurulum tamamlanma sonucu doğrulanamadı. " + "Worker exit: " + exitCode + ". " + Clip(error));
            var state = status.GetString() ?? "";
            if (state is not ("Installed" or "Failed" or "Rejected" or "Busy" or "Unknown"))
                return new("Unknown", "Kurulum geçerli bir tamamlanma durumu bildirmedi.");
            // A submitted request, exit zero, or aggregate success is insufficient.
            if (state == "Installed" && (!root.TryGetProperty("Verified", out var verified) || verified.ValueKind != JsonValueKind.True))
                return new("Unknown", "Kurulumun seçilen güncelleme için başarıyla tamamlandığı doğrulanamadı.");
            return new(state, Clip(detail.GetString() ?? ""), reboot.GetBoolean());
        }
        catch { return new("Unknown", "Kurulum sonucu okunamadı. " + "Worker exit: " + exitCode + ". " + Clip(error)); }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, Action<string>? progress)
    {
        var result = "";
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("PUSULA_PROGRESS:", StringComparison.Ordinal)) Say(progress, Clip(line[16..]));
            else if (line.StartsWith("PUSULA_RESULT:", StringComparison.Ordinal))
                result = line.Length <= CaptureLimit ? line[14..] : "";
        }
        return result;
    }

    private static async Task<string> ReadErrorAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            if (text.Length < CaptureLimit) text.Append(buffer, 0, Math.Min(count, CaptureLimit - text.Length));
        return text.ToString();
    }

    private static void Say(Action<string>? progress, string message) { try { progress?.Invoke(message); } catch { } }
    private static string Clip(string text) => text.Length <= 1800 ? text : text[..1800];
}
