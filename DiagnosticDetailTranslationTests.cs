using System.Text.Json;

namespace SistemPusulasi;

internal static class DiagnosticDetailTranslationTests
{
    internal static IEnumerable<string> Run()
    {
        var results = new List<string>();
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException("Test failed: " + name); results.Add("PASS: " + name); }
        string Translate(string input)
        {
            Check(DiagnosticDetailTranslations.TryTranslate(input, out var translated), "Recognizes diagnostic detail: " + input[..Math.Min(input.Length, 65)]);
            return translated;
        }
        const string telemetry = "Sıcaklık: bilinmiyor (sensör desteklenmiyor veya değer geçersiz); aşınma: 0; düzeltilemeyen okuma/yazma: bilinmiyor/bilinmiyor.";
        const string expected = "Temperature: unknown (sensor unsupported or value invalid); wear: 0; uncorrectable read/write errors: unknown/unknown.";
        Check(Translate(telemetry) == expected, "Unknown sensor telemetry translates the complete app narrative");
        const string osError = " Erişim engellendi. C:\\Sıcaklık\\aşınma.log\nHata kodu: 0x80070005";
        Check(Translate(telemetry + osError) == expected + osError, "Telemetry preserves multiline Windows error suffix and path exactly");
        Check(Translate(telemetry + " ") == expected + " ", "Persisted telemetry retains its trailing separator");
        Check(Translate("Sıcaklık: 73,5 °C; aşınma: 12; düzeltilemeyen okuma/yazma: 3/4.") ==
            "Temperature: 73,5 °C; wear: 12; uncorrectable read/write errors: 3/4.", "Telemetry preserves decimal spelling and every error count");
        Check(Translate("Sıcaklık: bilinmiyor; aşınma: bilinmiyor; düzeltilemeyen okuma/yazma: 0/0.").Contains("Temperature: unknown; wear: unknown;", StringComparison.Ordinal),
            "Older persisted telemetry with a short unknown label remains supported");
        const string disk = "Disk sağlık bildirimi: Healthy; işlem durumu: OK, In Service. Bu bildirim tam yüzey testi değildir.";
        Check(Translate(disk).Contains("Healthy; operational status: OK, In Service.", StringComparison.Ordinal), "Disk status values are retained verbatim");
        Check(Translate("Üç kısa örnekte ortalama yük %31. Performans sayacından tahmini canlı frekans: 4050 MHz; işlemci kullanım kapasitesi: 27%. Frekans yük ve güç yönetimiyle değişir; bu ölçüm stres testi veya sıcaklık ölçümü değildir.").Contains("31%. Estimated live frequency", StringComparison.Ordinal),
            "CPU live frequency narrative translates with all measurements");
        Check(Translate("Üç kısa örnekte ortalama yük %3. Canlı frekans okunamadı; Windows'un nominal/bildirilen frekansı: bilinmiyor. Frekans yük ve güç yönetimiyle değişir; bu ölçüm stres testi veya sıcaklık ölçümü değildir.").Contains("frequency: unknown.", StringComparison.Ordinal),
            "CPU fallback frequency narrative translates unknown values");
        Check(Translate("Kullanılabilir: 8,3 GB / toplam: 16,0 GB. Bu, RAM donanım testi değildir.") == "Available: 8,3 GB / total: 16,0 GB. This is not a RAM hardware test.",
            "Memory detail preserves localized numeric formatting");
        Check(Translate("Şebeke gücü bağlı.") == "AC power is connected.", "Power detail translates offline");
        const string eventOutput = "2026-10-07T10:20:30.0000000Z / 41: Sıcaklık: bilinmiyor\nSürücü C:\\Türkçe\\kernel.log | Aygıt devre dışı (kod 22); bu tek başına arıza değildir.";
        Check(Translate("Son yedi gün: en çok 40 kayıt içinden 3 olay. Geçmiş olaylar mevcut arızayı tek başına kanıtlamaz. " + eventOutput).EndsWith(eventOutput, StringComparison.Ordinal),
            "Event summary translates while raw event messages remain verbatim");
        const string reliabilityOutput = "Windows Update: Başarıyla yüklendi | Disk: Erişim engellendi.";
        Check(Translate("En çok 60 kayıttan 4 geçmiş olay (başarılı kurulumlar da bu geçmişe girer): " + reliabilityOutput).EndsWith(reliabilityOutput, StringComparison.Ordinal),
            "Reliability narrative preserves all source names and messages");
        const string pastMemory = "2026-10-01T10:20:30.0000000Z: Windows Bellek Tanılama hata bulmadı.";
        Check(Translate(pastMemory + " Geçmiş test, mevcut RAM durumunu garanti etmez.") == pastMemory + " A past test does not guarantee the current condition of RAM.",
            "Past memory test translates only its generated caveat");
        Check(Translate("Başlangıç: 06.10.2026 23:41; bitiş: bilinmiyor. Tamamlanan bir tarama bu zamanlardan doğrulanamadı.") ==
            "Started: 06.10.2026 23:41; ended: unknown. A completed scan could not be verified from these times.", "Antivirus incomplete scan times translate without inventing completion");
        Check(Translate("Başlangıç: 06.10.2026 23:41; bitiş: 07.10.2026 00:02. Windows'un bildirdiği son zamanlar; tarama kapsamı veya temiz sonucu ayrıca doğrulanmadı.").Contains("06.10.2026 23:41; ended: 07.10.2026 00:02", StringComparison.Ordinal),
            "Antivirus completed scan times preserve both timestamps");
        Check(Translate("Çalışma modu: Normal; hizmet: açık; antivirüs: bilinmiyor.") == "Operating mode: Normal; service: on; antivirus: unknown.", "Antivirus state labels translate with the operating mode intact");
        Check(Translate("Sürüm: 1.451.12.0; son güncelleme: 07.10.2026 09:25. En yeni sürüm olduğu ayrıca doğrulanmadı.").StartsWith("Version: 1.451.12.0; last updated: 07.10.2026 09:25.", StringComparison.Ordinal),
            "Antivirus signatures retain the reported version and date");
        const string provider = "Türkçe Defender, Other.Provider";
        Check(Translate(provider + ". Kayıt bilgisi, bu ürünlerin etkin veya güncel olduğunu tek başına kanıtlamaz.").StartsWith(provider + ". ", StringComparison.Ordinal),
            "Provider display names remain in their original language");
        const string error = "Erişim engellendi: C:\\Onarım\\Sıcaklık.log\nWindows bütünlüğü denetimleri ve onarım için uygulamayı yönetici olarak açın.";
        Check(Translate("Ayrıntılı DISM taraması. Çıkış kodu: -1. " + error).EndsWith(error, StringComparison.Ordinal),
            "Integrity details translate their wrappers without rewriting captured stderr");
        Check(Translate("SFC salt okunur doğrulama. Bozulma açıkça doğrulandı. Onarım sonrası doğrulama temiz.") ==
            "SFC read-only verification. Corruption was explicitly verified. Verification after repair was clean.", "Historical repaired integrity narratives remain compatible");
        foreach (var raw in new[] { "C:\\Sıcaklık\\bilinmiyor.log", "Aygıt: Türkçe Disk", "Erişim engellendi.", "Sıcaklık: bilinmiyor", "Başlangıç: someday; bitiş: unknown.", "Özel metin " + telemetry, telemetry + "X", "Onarım sonrası doğrulama temiz." })
            Check(!DiagnosticDetailTranslations.TryTranslate(raw, out var unchanged) && unchanged == raw, "Unrecognized source text stays untouched: " + raw[..Math.Min(raw.Length, 60)]);

        // Exercise actual producer output too, so app template changes cannot silently regress.
        var report = new ScanReport();
        using var hardware = JsonDocument.Parse("{\"Name\":\"Türkçe Disk\",\"Health\":\"Healthy\",\"Operational\":[\"OK\"],\"Wear\":0,\"ReliabilityError\":\"Erişim engellendi.\"}");
        DiagnosticsEngine.Interpret(report, "Disks", hardware.RootElement);
        Check(report.Findings.All(f => DiagnosticDetailTranslations.TryTranslate(f.Detail, out _)) && report.Findings[1].Detail.Contains("Sıcaklık:", StringComparison.Ordinal),
            "Actual disk producer details translate without modifying stored findings");
        return results;
    }
}
