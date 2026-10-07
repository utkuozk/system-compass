using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SistemPusulasi;

public sealed class AntivirusReport
{
    public DateTimeOffset CheckedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<Finding> Findings { get; set; } = new();
    public bool CanScan { get; set; }
    public string Summary { get; set; } = "Antivirüs durumu henüz kontrol edilmedi.";
}

/// <summary>Local Windows Defender integration. Actions never change protection preferences.</summary>
public static class AntivirusService
{
    private const int OutputLimit = 2 * 1024 * 1024;

    public static Task<AntivirusReport> ReadAsync() => ExecuteAsync("read");

    public static Task<AntivirusReport> RunAsync(string action)
    {
        action = (action ?? "").Trim().ToLowerInvariant();
        return action is "update" or "quick" or "full"
            ? ExecuteAsync(action)
            : Task.FromResult(Unknown("Geçersiz antivirüs işlemi. İmza güncelleme, hızlı veya tam tarama seçilmelidir."));
    }

    private static async Task<AntivirusReport> ExecuteAsync(string action)
    {
        try
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("SistemPusulasi.Security.Antivirus.ps1");
            if (resource == null) return Unknown("Antivirüs denetimi uygulama kaynağında bulunamadı.");
            using var reader = new StreamReader(resource, Encoding.UTF8);
            var script = await reader.ReadToEndAsync();
            var command = "& {\n" + script + "\n} -Action '" + action + "'";
            // Trusted embedded content only. Do not execute from a writable temporary file.
            if (command.Length + command.Count(c => c == '"') + 1000 >= 30000)
                return Unknown("Antivirüs denetimi komut sınırını aştı.");
            var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(shell)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command })
                start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            if (!process.Start()) return Unknown("Antivirüs denetimi başlatılamadı.");
            process.StandardInput.Close();
            var outputTask = CaptureAsync(process.StandardOutput);
            var errorTask = CaptureAsync(process.StandardError);
            if (action == "read")
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    // Only a read-only inventory may be terminated. No action timeout.
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                    // Drain asynchronously without holding the UI after a failed kill.
                    _ = ObserveCaptureAsync(outputTask, errorTask);
                    return Unknown("Salt okunur antivirüs kontrolü zaman aşımına uğradı. Durum doğrulanamadı.");
                }
            }
            else
            {
                // Signature updates/scans can take hours. Never kill them on a timeout,
                // cancellation, window close, or in a finally block.
                await process.WaitForExitAsync();
            }
            var output = await outputTask;
            await errorTask; // Drain stderr, but never expose local paths from engine errors.
            if (process.ExitCode != 0) return Unknown("Windows antivirüs denetimi tamamlanamadı. Ayrıntılar için Windows Güvenliği'ni açın.");
            if (output.Length >= OutputLimit) return Unknown("Antivirüs çıktısı sınırı aştı. Durum doğrulanamadı.");
            return ParseResult(output);
        }
        catch { return Unknown("Antivirüs durumu okunamadı. Windows Güvenliği'nden sağlayıcı ve işlem durumunu kontrol edin."); }
    }

    // Pure helper for tests. Eligibility is recomputed: a payload cannot enable
    // scanning merely by claiming CanScan, omitting a field, or coercing a string.
    internal static AntivirusReport ParseResult(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output.Trim());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Number(root, "SchemaVersion") != 1 ||
                Bool(root, "ProvidersKnown") == null || Bool(root, "DefenderKnown") == null || Bool(root, "ThreatsKnown") == null ||
                !root.TryGetProperty("Providers", out var providers) || providers.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("Threats", out var threats) || threats.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("Action", out var action) || action.ValueKind != JsonValueKind.Object ||
                Date(root, "CheckedUtc") is not { } checkedUtc)
                return Unknown("Antivirüs yanıtı eksik veya geçersiz. Durum doğrulanamadı.");

            var result = new AntivirusReport { CheckedUtc = checkedUtc };
            var names = new List<string>();
            var providersValid = Bool(root, "ProvidersKnown") == true && providers.GetArrayLength() > 0;
            var allDefenderNames = true;
            foreach (var item in providers.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                { providersValid = false; allDefenderNames = false; continue; }
                allDefenderNames &= IsDefenderName(item.GetString()!.Trim());
                names.Add(Display(item.GetString()!));
            }
            var defenderOnly = providersValid && allDefenderNames;
            var otherProviderRegistered = providersValid && !allDefenderNames;
            Add(result, "Kayıtlı antivirüs sağlayıcıları", providersValid ? "Info" : "Unknown",
                names.Count > 0 ? string.Join(", ", names) + ". Kayıt bilgisi, bu ürünlerin etkin veya güncel olduğunu tek başına kanıtlamaz."
                    : "Sağlayıcı kaydı okunamadı veya boş. Etkin koruma varsayılmadı.");

            var hasDefender = Bool(root, "DefenderKnown") == true && root.TryGetProperty("Defender", out var defenderValue) &&
                defenderValue.ValueKind == JsonValueKind.Object;
            var defender = root.TryGetProperty("Defender", out var value) ? value : default;
            var mode = Text(defender, "AMRunningMode");
            var service = Bool(defender, "AMServiceEnabled");
            var enabled = Bool(defender, "AntivirusEnabled");
            var active = hasDefender && mode == "Normal" && service == true && enabled == true;
            result.CanScan = defenderOnly && active;
            Add(result, "Microsoft Defender çalışma durumu", !hasDefender || service == null || enabled == null || string.IsNullOrWhiteSpace(mode) ? "Unknown" : active || otherProviderRegistered ? "Info" : "Warning",
                hasDefender ? $"Çalışma modu: {Display(mode)}; hizmet: {Label(service)}; antivirüs: {Label(enabled)}."
                    + (otherProviderRegistered && !active ? " Başka antivirüs sağlayıcısı kayıtlıyken Defender'ın pasif veya kapalı olması beklenebilir. Diğer sağlayıcının etkin korumasını kendi uygulamasından doğrulayın." : "")
                    : "Defender durumu okunamadı. Etkin koruma varsayılmadı.");
            var realtime = hasDefender ? Bool(defender, "RealTimeProtectionEnabled") : null;
            Add(result, "Gerçek zamanlı koruma", realtime == null ? "Unknown" : realtime == true || otherProviderRegistered ? "Info" : "Warning",
                "Defender bildirimi: " + Label(realtime) + ". Bu bilgi tek başına tam koruma garantisi değildir."
                    + (otherProviderRegistered && realtime == false ? " Kayıtlı diğer antivirüsün gerçek zamanlı korumasını kendi uygulamasından kontrol edin." : ""));
            var signatureVersion = hasDefender ? Text(defender, "AntivirusSignatureVersion") : "";
            var signatureDate = hasDefender ? Date(defender, "AntivirusSignatureLastUpdated") : null;
            var signatureOld = signatureDate != null && DateTimeOffset.UtcNow - signatureDate.Value > TimeSpan.FromDays(7);
            Add(result, "Defender güvenlik imzaları", string.IsNullOrWhiteSpace(signatureVersion) || signatureDate == null ? "Unknown" : signatureOld ? "Warning" : "Info",
                "Sürüm: " + (string.IsNullOrWhiteSpace(signatureVersion) ? "bilinmiyor" : Display(signatureVersion)) +
                "; son güncelleme: " + DateLabel(signatureDate) + "."
                    + (signatureOld ? " Bildirilen imza güncelleme tarihi yedi günden eski. İmza güncellemesini kontrol edin." : "")
                    + " En yeni sürüm olduğu ayrıca doğrulanmadı.");
            AddScanDates(result, defender, hasDefender, "Quick", "Son hızlı tarama");
            AddScanDates(result, defender, hasDefender, "Full", "Son tam tarama");

            var threatsValid = Bool(root, "ThreatsKnown") == true;
            var activeNames = new List<string>();
            var historicNames = new List<string>();
            foreach (var threat in threats.EnumerateArray())
            {
                var name = Text(threat, "Name");
                var isActive = Bool(threat, "IsActive");
                if (string.IsNullOrWhiteSpace(name) || isActive == null) { threatsValid = false; continue; }
                (isActive == true ? activeNames : historicNames).Add(Display(name));
            }
            Add(result, "Etkin Defender tespitleri", activeNames.Count > 0 ? "Critical" : threatsValid ? "Info" : "Unknown",
                activeNames.Count > 0 ? string.Join(", ", activeNames) + ". Windows Güvenliği'nde önerilen işlemleri inceleyin."
                    : threatsValid ? "Get-MpThreat şu anda etkin tespit bildirmedi. Bu bir temiz sistem veya tamamlanmış tarama garantisi değildir."
                    : "Etkin tespitler eksiksiz okunamadı; tehdit olmadığı varsayılmadı.");
            Add(result, "Defender tespit geçmişi", threatsValid ? "Info" : "Unknown",
                historicNames.Count > 0 ? string.Join(", ", historicNames) + ". Bunlar geçmiş tespitlerdir; etkin tehdit olarak sayılmadı."
                    : threatsValid ? "Bu sorguda geçmiş tespit kaydı bildirilmedi." : "Tespit geçmişi eksiksiz okunamadı.");

            var actionName = Text(action, "Name");
            var actionResult = Text(action, "Result");
            if (actionName is not ("read" or "update" or "quick" or "full") ||
                actionResult is not ("None" or "Busy" or "Blocked" or "BlockedAfterUpdate" or "Failed" or "ScanFailed" or "Submitted" or "Updated") ||
                (actionName == "read" ? actionResult != "None" : actionResult == "None"))
                return Unknown("Antivirüs işlem yanıtı geçersiz. Durum doğrulanamadı.");
            if (actionName != "read")
                Add(result, "İstenen antivirüs işlemi", actionResult is "Updated" or "Submitted" ? "Info" : "Warning", ActionDetail(actionResult));
            result.Summary = activeNames.Count > 0 ? $"Defender {activeNames.Count} etkin tespit bildirdi. Windows Güvenliği'ni inceleyin."
                : !threatsValid ? "Defender tespit durumu tam doğrulanamadı."
                : result.CanScan ? "Defender etkin modda. Bildirilen durum ve tespitler aşağıda; bu rapor temiz sistem garantisi değildir."
                : "Defender işlemleri kullanılamıyor: sağlayıcı veya etkin Defender durumu uygun değil ya da doğrulanamadı. Windows Güvenliği'ni inceleyin.";
            if (actionName != "read") result.Summary = ActionDetail(actionResult) + " " + result.Summary;
            return result;
        }
        catch { return Unknown("Antivirüs çıktısı yorumlanamadı. Durum doğrulanamadı."); }
    }

    private static bool IsDefenderName(string name) => name.Equals("Windows Defender", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Windows Defender Antivirus", StringComparison.OrdinalIgnoreCase) || name.Equals("Microsoft Defender Antivirus", StringComparison.OrdinalIgnoreCase);
    private static void AddScanDates(AntivirusReport report, JsonElement defender, bool known, string prefix, string title)
    {
        var start = known ? Date(defender, prefix + "ScanStartTime") : null;
        var end = known ? Date(defender, prefix + "ScanEndTime") : null;
        var completed = start != null && end >= start;
        Add(report, title, completed ? "Info" : "Unknown", $"Başlangıç: {DateLabel(start)}; bitiş: {DateLabel(end)}. " +
            (completed ? "Windows'un bildirdiği son zamanlar; tarama kapsamı veya temiz sonucu ayrıca doğrulanmadı."
                : "Tamamlanan bir tarama bu zamanlardan doğrulanamadı."));
    }
    private static string ActionDetail(string result) => result switch
    {
        "Updated" => "Windows imza güncelleme komutu döndü. Güncel imza bilgisi işlem sonrasında tekrar okundu.",
        "Submitted" => "İmzalar güncellendi ve istenen tarama komutu Windows Defender'a iletildi. Komutun dönmesi temiz sonuç veya taramanın tamamlandığı garantisi değildir; son zamanları ve Windows Güvenliği'ni inceleyin.",
        "Busy" => "Başka bir Sistem Pusulası antivirüs işlemi sürüyor. İkinci işlem başlatılmadı.",
        "Blocked" => "İşlem başlatılmadı. Yalnızca sağlayıcıları doğrulanan ve Normal modda etkin Defender ile kullanılabilir. Başka veya bilinmeyen antivirüs sağlayıcısı varsa kendi uygulamasını kullanın.",
        "BlockedAfterUpdate" => "İmzalar güncellendi, ancak sağlayıcı veya Defender durumu değişti. Tarama başlatılmadı.",
        "ScanFailed" => "İmzalar güncellendi; tarama komutu başarısız oldu. Windows Güvenliği'nden işlem durumunu kontrol edin.",
        _ => "İşlem tamamlanamadı. İmza güncelleme başarısızsa tarama başlatılmaz. Yönetici izni veya Windows Güvenliği'ndeki durumu kontrol edin."
    };
    private static string Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static bool? Bool(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
        ? value.ValueKind == JsonValueKind.True ? true : value.ValueKind == JsonValueKind.False ? false : null : null;
    private static int? Number(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static DateTimeOffset? Date(JsonElement item, string name)
    {
        return DateTimeOffset.TryParse(Text(item, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) &&
            date.Year > 2000 && date <= DateTimeOffset.UtcNow.AddMinutes(5) ? date : null;
    }
    private static string Label(bool? value) => value == true ? "açık" : value == false ? "kapalı" : "bilinmiyor";
    private static string DateLabel(DateTimeOffset? date) => date?.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "bilinmiyor";
    private static string Display(string text) => new string(text.Where(c => !char.IsControl(c)).Take(300).ToArray()).Trim();
    private static void Add(AntivirusReport report, string title, string status, string detail) => report.Findings.Add(new Finding
        { Category = "Antivirüs", Title = title, Status = status, Detail = detail });
    private static AntivirusReport Unknown(string detail)
    {
        var report = new AntivirusReport { Summary = detail };
        Add(report, "Antivirüs kontrolü", "Unknown", detail);
        return report;
    }
    private static async Task<string> CaptureAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            var remaining = OutputLimit - text.Length;
            if (remaining > 0) text.Append(buffer, 0, Math.Min(count, remaining));
        }
        return text.ToString();
    }
    private static async Task ObserveCaptureAsync(params Task<string>[] captures)
    {
        try { await Task.WhenAll(captures); } catch { }
    }
}
