using System.Text.RegularExpressions;

namespace SistemPusulasi;

// Presentation-only translations of this application's recorded diagnostic narratives.
// Captured Windows output and dynamic identifiers are opaque: never run replacements on them.
internal static class DiagnosticDetailTranslations
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["Bu uygulamanın diğer taraması tamamlandığında yeniden deneyin."] = "Try again after this application's other scan finishes.",
        ["Bu kontrol veri çıktısında eksik; başarılı sayılmadı."] = "This check is missing from the collected data; it was not counted as successful.",
        ["Kontrol sonucu veri içermiyor."] = "The check result contains no data.",
        ["Şebeke gücü bağlı."] = "AC power is connected.",
        ["Pil kullanılıyor; otomatik onarım ertelenir."] = "Running on battery; automatic repair is deferred.",
        ["Güç durumu belirlenemedi."] = "Power status could not be determined.",
        ["Windows bütünlüğü denetimleri ve onarım için uygulamayı yönetici olarak açın."] = "Open the application as an administrator for Windows integrity checks and repair.",
        ["Otomatik onarım ertelenir. Uygulama yeniden başlatma yapmaz."] = "Automatic repair is deferred. The application does not restart the computer.",
        ["CHKDSK salt okunur denetiminde hata bildirmedi. Etkin sürücüde sonuçlar anlık durumdur."] = "CHKDSK reported no errors in its read-only check. Results on an active drive reflect its state at that moment.",
        ["Güvenlik koşulları doğrulanamadı."] = "Safety conditions could not be verified.",
        ["Yönetici yetkisi yok; DISM/SFC çalıştırılmadı."] = "Administrator permission is missing; DISM/SFC was not run.",
        ["Başka bir DISM/SFC işlemi çalışıyor; denetim ertelendi."] = "Another DISM/SFC operation is running; the check was deferred.",
        ["DISM bileşen deposunu onarılamaz olarak bildirdi. Otomatik onarım başlatılmadı; Windows kurtarma seçeneklerini inceleyin."] = "DISM reported that the component store cannot be repaired. Automatic repair was not started; review Windows recovery options.",
        ["Hızlı DISM kontrolü kayıtlı bozulma bildirdi. Otomatik onarım için yeni ScanHealth ile ayrıntılı taramada güncel bozulma doğrulanmalıdır."] = "The quick DISM check reported recorded corruption. Automatic repair requires current corruption to be verified by a new ScanHealth during a deep scan.",
        ["Güncel DISM/SFC çıktısında onarılabilir bozulma doğrulanmadığından onarım çalıştırılmadı."] = "Repair was not run because the current DISM/SFC output did not verify repairable corruption.",
        ["Bozulma doğrulandı; otomatik onarım ayarı kapalı."] = "Corruption was verified; automatic repair is disabled.",
        ["Son otomatik onarım girişiminin üzerinden en az yedi gün geçmelidir."] = "At least seven days must pass since the last automatic repair attempt.",
        ["Güncel ayarlar doğrulanamadığından onarım başlatılmadı."] = "Repair was not started because the current settings could not be verified.",
        ["Yönetici yetkisi, bağlı şebeke gücü, bekleyen yeniden başlatma olmaması ve başka DISM/SFC bulunmaması gerekir."] = "Repair requires administrator permission, connected AC power, no pending restart, and no other DISM/SFC operation.",
        ["DISM ve SFC tekrar denetlendi; bütünlük doğrulandı."] = "DISM and SFC were checked again; integrity was verified.",
        ["İki doğrulamanın da temiz olduğu doğrulanamadı. Ham kayıtları inceleyin."] = "Both checks could not be verified as clean. Review the raw logs.",
        ["Durum okunamadı; Windows bildirimlerini kontrol edin."] = "The status could not be read; check Windows notifications.",
        ["Olay kaydı eksik veya geçersiz; arıza olarak sayılmadı."] = "The event record is missing or invalid; it was not counted as a fault.",
        ["Son yedi gün içinde bu sağlayıcıda kritik/hata/uyarı kaydı bulunmadı; donanımın kesin sağlam olduğu anlamına gelmez."] = "No critical, error, or warning records were found for this provider in the past seven days; this does not guarantee that the hardware is healthy.",
        ["Son yedi gün için güvenilirlik kaydı yok; bu verinin kullanılabilirliği Windows ayarlarına bağlıdır."] = "No reliability records were found for the past seven days; availability of this data depends on Windows settings.",
        ["Fiziksel disk verisi alınamadı."] = "Physical disk data could not be obtained.",
        ["Disk geçmişinde düzeltilemeyen hata kaydı var; önemli dosyaları yedekleyip üretici tanı aracını kullanın."] = "The disk history contains uncorrectable errors; back up important files and use the manufacturer's diagnostic tool.",
        ["Windows aygıt hata kodu bildirmedi."] = "Windows reported no device error codes.",
        ["Aygıt devre dışı (kod 22); bu tek başına arıza değildir."] = "The device is disabled (code 22); this alone does not indicate a fault.",
        ["İşlemci yükü okunamadı."] = "Processor load could not be read.",
        ["Son 120 günde Windows Bellek Tanılama sonucu bulunamadı; yeni test veya yeniden başlatma yapılmadı."] = "No Windows Memory Diagnostic result was found in the past 120 days; no new test or restart was performed.",
        ["Ayrıntılı DISM taraması."] = "Detailed DISM scan.",
        ["Hızlı kontrol yalnızca daha önce kaydedilmiş bozulmayı sorgular; tüm dosyaları taramaz."] = "The quick check only queries previously recorded corruption; it does not scan all files.",
        ["SFC salt okunur doğrulama."] = "SFC read-only verification.",
        ["Bütünlük kontrolü temiz bildirdi."] = "The integrity check reported a clean result.",
        ["Bozulma açıkça doğrulandı."] = "Corruption was explicitly verified.",
        ["Bileşen deposu onarılamaz olarak bildirildi."] = "The component store was reported as unrepairable.",
        ["Kontrol süre sınırını aştı; sonuç bilinmiyor."] = "The check exceeded its time limit; the result is unknown.",
        ["Çıktı kesin sağlıklı/onarılabilir sonuç olarak tanınmadı; ham kaydı inceleyin."] = "The output was not recognized as a definite healthy or repairable result; review the raw log.",
        ["Gömülü tanılama komutu Windows uzunluk sınırını aşıyor."] = "The embedded diagnostic command exceeds the Windows length limit.",
        ["Geçersiz antivirüs işlemi. İmza güncelleme, hızlı veya tam tarama seçilmelidir."] = "Invalid antivirus operation. Select a signature update, quick scan, or full scan.",
        ["Antivirüs denetimi uygulama kaynağında bulunamadı."] = "The antivirus check could not be found in the application resources.",
        ["Antivirüs denetimi komut sınırını aştı."] = "The antivirus check exceeded the command length limit.",
        ["Antivirüs denetimi başlatılamadı."] = "The antivirus check could not be started.",
        ["Salt okunur antivirüs kontrolü zaman aşımına uğradı. Durum doğrulanamadı."] = "The read-only antivirus check timed out. Status could not be verified.",
        ["Windows antivirüs denetimi tamamlanamadı. Ayrıntılar için Windows Güvenliği'ni açın."] = "The Windows antivirus check could not be completed. Open Windows Security for details.",
        ["Antivirüs çıktısı sınırı aştı. Durum doğrulanamadı."] = "The antivirus output exceeded its limit. Status could not be verified.",
        ["Antivirüs durumu okunamadı. Windows Güvenliği'nden sağlayıcı ve işlem durumunu kontrol edin."] = "Antivirus status could not be read. Check the provider and operation status in Windows Security.",
        ["Antivirüs yanıtı eksik veya geçersiz. Durum doğrulanamadı."] = "The antivirus response is incomplete or invalid. Status could not be verified.",
        ["Sağlayıcı kaydı okunamadı veya boş. Etkin koruma varsayılmadı."] = "Provider registration could not be read or is empty. Active protection was not assumed.",
        ["Defender durumu okunamadı. Etkin koruma varsayılmadı."] = "Defender status could not be read. Active protection was not assumed.",
        ["Get-MpThreat şu anda etkin tespit bildirmedi. Bu bir temiz sistem veya tamamlanmış tarama garantisi değildir."] = "Get-MpThreat reported no active detections at this time. This does not guarantee a clean system or a completed scan.",
        ["Etkin tespitler eksiksiz okunamadı; tehdit olmadığı varsayılmadı."] = "Active detections could not be read completely; absence of threats was not assumed.",
        ["Bu sorguda geçmiş tespit kaydı bildirilmedi."] = "No historical detections were reported by this query.",
        ["Tespit geçmişi eksiksiz okunamadı."] = "Detection history could not be read completely.",
        ["Antivirüs işlem yanıtı geçersiz. Durum doğrulanamadı."] = "The antivirus operation response is invalid. Status could not be verified.",
        ["Antivirüs çıktısı yorumlanamadı. Durum doğrulanamadı."] = "The antivirus output could not be interpreted. Status could not be verified."
    };

    private const string Number = @"-?\d+(?:[.,]\d+)?";
    private const string Metric = @"(?:-?\d+(?:[.,]\d+)?|bilinmiyor)";
    private const string Date = @"(?:\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}|bilinmiyor)";
    private const string Repaired = " Onarım sonrası doğrulama temiz.";

    private static readonly (string Pattern, Func<Match, string> Render)[] Templates =
    [
        (@"\ASıcaklık: (?<temperature>" + Number + @" °C|bilinmiyor(?: \(sensör desteklenmiyor veya değer geçersiz\))?); aşınma: (?<wear>" + Metric + @"); düzeltilemeyen okuma/yazma: (?<read>" + Metric + @")/(?<write>" + Metric + @")\.(?<error>(?: [\s\S]*)?)\z",
            m => $"Temperature: {Temperature(m.Groups["temperature"].Value)}; wear: {Field(m, "wear")}; uncorrectable read/write errors: {Field(m, "read")}/{Field(m, "write")}." + m.Groups["error"].Value),
        (@"\ADisk sağlık bildirimi: (?<health>[^;\r\n]*); işlem durumu: (?<operational>[^\r\n]*)\. Bu bildirim tam yüzey testi değildir\.\z",
            m => $"Reported disk health: {m.Groups["health"].Value}; operational status: {m.Groups["operational"].Value}. This report is not a full surface test."),
        (@"\ABoş: (?<free>" + Number + @")? GB / toplam: (?<total>" + Number + @")? GB\.\z",
            m => $"Free: {m.Groups["free"].Value} GB / total: {m.Groups["total"].Value} GB."),
        (@"\AKullanılabilir: (?<free>" + Number + @")? GB / toplam: (?<total>" + Number + @")? GB\. Bu, RAM donanım testi değildir\.\z",
            m => $"Available: {m.Groups["free"].Value} GB / total: {m.Groups["total"].Value} GB. This is not a RAM hardware test."),
        (@"\AWindows aygıt hata kodu: (?<code>" + Number + @")?\. Aygıt Yöneticisi üzerinden inceleyin\.\z",
            m => $"Windows device error code: {m.Groups["code"].Value}. Review it in Device Manager."),
        (@"\AÜç kısa örnekte ortalama yük %(?<load>" + Number + @")\. Performans sayacından tahmini canlı frekans: (?<frequency>" + Number + @") MHz; işlemci kullanım kapasitesi: (?<utility>" + Number + @"%|bilinmiyor)\. Frekans yük ve güç yönetimiyle değişir; bu ölçüm stres testi veya sıcaklık ölçümü değildir\.\z",
            m => $"Average load across three short samples: {m.Groups["load"].Value}%. Estimated live frequency from the performance counter: {m.Groups["frequency"].Value} MHz; processor utility: {Field(m, "utility")}. Frequency varies with load and power management; this measurement is not a stress test or temperature measurement."),
        (@"\AÜç kısa örnekte ortalama yük %(?<load>" + Number + @")\. Canlı frekans okunamadı; Windows'un nominal/bildirilen frekansı: (?<frequency>" + Number + @" MHz|bilinmiyor)\. Frekans yük ve güç yönetimiyle değişir; bu ölçüm stres testi veya sıcaklık ölçümü değildir\.\z",
            m => $"Average load across three short samples: {m.Groups["load"].Value}%. Live frequency could not be read; Windows nominal/reported frequency: {Field(m, "frequency")}. Frequency varies with load and power management; this measurement is not a stress test or temperature measurement."),
        (@"\A(?<count>\d+) kayıt saat/tarih tutarsızlığı gösteriyor; son yedi günün bulgularına dahil edilmedi\.\z",
            m => $"{m.Groups["count"].Value} records show a date/time inconsistency; they were excluded from the past seven days' findings."),
        (@"\ASon yedi gün: en çok 40 kayıt içinden (?<count>\d+) olay\. Geçmiş olaylar mevcut arızayı tek başına kanıtlamaz\. (?<logs>[\s\S]*)\z",
            m => $"Past seven days: {m.Groups["count"].Value} events from up to 40 records. Historical events alone do not establish a current fault. " + m.Groups["logs"].Value),
        (@"\AEn çok 60 kayıttan (?<count>\d+) geçmiş olay \(başarılı kurulumlar da bu geçmişe girer\): (?<logs>[\s\S]*)\z",
            m => $"{m.Groups["count"].Value} historical events from up to 60 records (successful installations are also included): " + m.Groups["logs"].Value),
        (@"\A(?<logs>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[^\r\n]*: [\s\S]*) Geçmiş test, mevcut RAM durumunu garanti etmez\.\z",
            m => m.Groups["logs"].Value + " A past test does not guarantee the current condition of RAM."),
        (@"\AÇıkış kodu: (?<code>-?\d+)\. (?<error>[\s\S]*)\z",
            m => $"Exit code: {m.Groups["code"].Value}. " + ExactOrOriginal(m.Groups["error"].Value)),
        (@"\AOnarım koşulu eksik veya geçersiz: (?<key>Admin|AcOnline|AcKnown|PendingReboot|OtherServicing)\z",
            m => "Repair condition is missing or invalid: " + m.Groups["key"].Value),
        (@"\ABaşlangıç: (?<start>" + Date + @"); bitiş: (?<end>" + Date + @")\. (?<result>Windows'un bildirdiği son zamanlar; tarama kapsamı veya temiz sonucu ayrıca doğrulanmadı\.|Tamamlanan bir tarama bu zamanlardan doğrulanamadı\.)\z",
            m => $"Started: {Field(m, "start")}; ended: {Field(m, "end")}. " + (m.Groups["result"].Value.StartsWith("Windows'un", StringComparison.Ordinal)
                ? "These are the latest times reported by Windows; scan coverage or a clean result was not independently verified."
                : "A completed scan could not be verified from these times.")),
        (@"\AÇalışma modu: (?<mode>[^;\r\n]*); hizmet: (?<service>açık|kapalı|bilinmiyor); antivirüs: (?<enabled>açık|kapalı|bilinmiyor)\.(?<other> Başka antivirüs sağlayıcısı kayıtlıyken Defender'ın pasif veya kapalı olması beklenebilir\. Diğer sağlayıcının etkin korumasını kendi uygulamasından doğrulayın\.)?\z",
            m => $"Operating mode: {m.Groups["mode"].Value}; service: {Field(m, "service")}; antivirus: {Field(m, "enabled")}." + (m.Groups["other"].Success ? " Defender may be passive or disabled when another antivirus provider is registered. Verify that provider's active protection in its own application." : "")),
        (@"\ADefender bildirimi: (?<state>açık|kapalı|bilinmiyor)\. Bu bilgi tek başına tam koruma garantisi değildir\.(?<other> Kayıtlı diğer antivirüsün gerçek zamanlı korumasını kendi uygulamasından kontrol edin\.)?\z",
            m => $"Defender reports: {Field(m, "state")}. This information alone does not guarantee full protection." + (m.Groups["other"].Success ? " Check the registered third-party antivirus's real-time protection in its own application." : "")),
        (@"\ASürüm: (?<version>[^;\r\n]*); son güncelleme: (?<date>" + Date + @")\.(?<old> Bildirilen imza güncelleme tarihi yedi günden eski\. İmza güncellemesini kontrol edin\.)? En yeni sürüm olduğu ayrıca doğrulanmadı\.\z",
            m => $"Version: {Field(m, "version")}; last updated: {Field(m, "date")}." + (m.Groups["old"].Success ? " The reported signature update is more than seven days old. Check for a signature update." : "") + " It was not independently verified as the latest version."),
        (@"\A(?<names>[^\r\n]+)\. Kayıt bilgisi, bu ürünlerin etkin veya güncel olduğunu tek başına kanıtlamaz\.\z",
            m => m.Groups["names"].Value + ". Registration alone does not establish that these products are active or up to date."),
        (@"\A(?<names>[^\r\n]+)\. Windows Güvenliği'nde önerilen işlemleri inceleyin\.\z",
            m => m.Groups["names"].Value + ". Review the recommended actions in Windows Security."),
        (@"\A(?<names>[^\r\n]+)\. Bunlar geçmiş tespitlerdir; etkin tehdit olarak sayılmadı\.\z",
            m => m.Groups["names"].Value + ". These are historical detections; they were not counted as active threats.")
    ];

    private static readonly (Regex Pattern, Func<Match,string> Render)[] CachedTemplates = Templates.Select(t => (new Regex(t.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)), t.Render)).ToArray();

    private static readonly (string Turkish, string English)[] ErrorPrefixes =
    [
        ("Veri yorumlanamadı: ", "The data could not be interpreted: "),
        ("Onarım başlatılmadı: ", "Repair was not started: ")
    ];

    internal static bool TryTranslate(string value, out string translated)
    {
        translated = value;
        if (string.IsNullOrEmpty(value)) return false;
        if (English.TryGetValue(value, out var exact)) { translated = exact; return true; }
        try
        {
            foreach (var (pattern, render) in CachedTemplates)
            {
                var match = pattern.Match(value);
                if (!match.Success) continue;
                translated = render(match);
                return true;
            }
        }
        catch (RegexMatchTimeoutException) { return false; }

        // Only app-created integrity details acquire this suffix. Require a recognized base.
        if (value.EndsWith(Repaired, StringComparison.Ordinal) && TryTranslate(value[..^Repaired.Length], out var beforeRepair))
        { translated = beforeRepair + " Verification after repair was clean."; return true; }
        foreach (var context in new[] { "Ayrıntılı DISM taraması.", "Hızlı kontrol yalnızca daha önce kaydedilmiş bozulmayı sorgular; tüm dosyaları taramaz.", "SFC salt okunur doğrulama." })
            if (value.StartsWith(context + " ", StringComparison.Ordinal) && TryTranslate(value[(context.Length + 1)..], out var result))
            { translated = English[context] + " " + result; return true; }
        const string chkdsk = "Salt okunur CHKDSK kesin sonuç vermedi; etkin birimde geçici tutarsızlık olabilir. ";
        const string sfc = "SFC onarımı başlatılmadı. ";
        foreach (var (prefix, english) in new[] { (chkdsk, "Read-only CHKDSK did not give a conclusive result; an active volume may have temporary inconsistencies. "), (sfc, "SFC repair was not started. ") })
            if (value.StartsWith(prefix, StringComparison.Ordinal) && TryTranslate(value[prefix.Length..], out var failure))
            { translated = english + failure; return true; }
        foreach (var (prefix, english) in ErrorPrefixes)
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            { translated = english + value[prefix.Length..]; return true; }
        return false;
    }

    private static string ExactOrOriginal(string value) => English.TryGetValue(value, out var english) ? english : value;
    private static string Field(Match match, string name) => match.Groups[name].Value switch
    { "bilinmiyor" => "unknown", "açık" => "on", "kapalı" => "off", var value => value };
    private static string Temperature(string value) => value switch
    {
        "bilinmiyor" => "unknown",
        "bilinmiyor (sensör desteklenmiyor veya değer geçersiz)" => "unknown (sensor unsupported or value invalid)",
        _ => value
    };
}
