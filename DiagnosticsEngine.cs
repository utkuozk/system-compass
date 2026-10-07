using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using static SistemPusulasi.DiagnosticsPolicy;

namespace SistemPusulasi;

public sealed class DiagnosticsEngine
{
    private const int CaptureLimit = 2 * 1024 * 1024;
    private sealed record CommandResult(int ExitCode, string Output, string Error, bool TimedOut);
    private sealed record Guard(bool Admin, bool AcOnline, bool AcKnown, bool PendingReboot, bool OtherServicing);

    public async Task<ScanReport> RunAsync(bool deep, AppSettings settings, string reportDirectory, Action<string> progress, bool manualRepair = false)
    {
        // A user request always verifies current corruption; an old report never authorizes a write.
        deep |= manualRepair;
        var report = new ScanReport { DeepScan = deep, ReportDirectory = Path.GetFullPath(reportDirectory) };
        Directory.CreateDirectory(report.ReportDirectory);
        LocalStore.Initialize();
        FileStream? scanLock = null;
        try
        {
            try { scanLock = new FileStream(Path.Combine(LocalStore.Root, "scan.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { Add(report, "Tarama", "Başka tarama çalışıyor", "Unknown", "Bu uygulamanın diğer taraması tamamlandığında yeniden deneyin."); return Complete(report); }
            Say(progress, "Donanım, güç ve son yedi günün olay kayıtları kontrol ediliyor…");
            var collection = await CollectAsync(report.ReportDirectory, false);
            Guard? guard = null;
            try
            {
                if (collection.ExitCode != 0 || collection.TimedOut) throw new InvalidOperationException(Failure(collection));
                using var doc = JsonDocument.Parse(collection.Output.Trim());
                var present=doc.RootElement.EnumerateArray().Select(x=>Text(x,"Name")).ToHashSet(StringComparer.Ordinal);
                var expected=new List<string>{"Guard","Reliability","Disks","Volumes","Devices","Cpu","Memory","MemoryTest"};
                foreach(var provider in new[]{"Microsoft-Windows-WHEA-Logger","disk","Ntfs","stornvme","storahci","Microsoft-Windows-Kernel-Power","Microsoft-Windows-Kernel-Processor-Power","Service Control Manager","Microsoft-Windows-WER-SystemErrorReporting","Display"}) { expected.Add("Events:"+provider); expected.Add("FutureEvents:"+provider); }
                foreach(var missing in expected.Where(x=>!present.Contains(x))) Add(report,"Kontrol",missing,"Unknown","Bu kontrol veri çıktısında eksik; başarılı sayılmadı.");
                foreach (var check in doc.RootElement.EnumerateArray())
                {
                    var name = Text(check, "Name");
                    if (!Bool(check, "Ok")) { Add(report, "Kontrol", name, "Unknown", Text(check, "Error")); continue; }
                    if(!check.TryGetProperty("Data",out var data)) { Add(report,"Kontrol",name,"Unknown","Kontrol sonucu veri içermiyor."); continue; }
                    if (name == "Guard")
                    {
                        try
                        {
                            guard = ParseGuard(data);
                            report.RebootRequired = guard.PendingReboot;
                            Add(report, "Güç", "Güç kaynağı", guard.AcKnown ? "Info" : "Unknown", guard.AcOnline ? "Şebeke gücü bağlı." : guard.AcKnown ? "Pil kullanılıyor; otomatik onarım ertelenir." : "Güç durumu belirlenemedi.");
                            if (!guard.Admin) Add(report, "Yetki", "Yönetici yetkisi", "Info", "Windows bütünlüğü denetimleri ve onarım için uygulamayı yönetici olarak açın.");
                            if (guard.PendingReboot) Add(report, "Windows", "Yeniden başlatma bekleniyor", "Info", "Otomatik onarım ertelenir. Uygulama yeniden başlatma yapmaz.");
                        }
                        catch(Exception ex) { Add(report,"Kontrol",name,"Unknown",ex.Message); }
                        continue;
                    }
                    try { Interpret(report, name, data); }
                    catch(Exception ex) { Add(report,"Kontrol",name,"Unknown","Veri yorumlanamadı: "+ex.Message); }
                }
            }
            catch (Exception ex) { Add(report, "Kontrol", "Sistem verilerinin okunması", "Unknown", ex.Message); }

            var dismState = Integrity.Unknown;
            var sfcState = Integrity.Unknown;
            if (guard?.Admin == true && !guard.OtherServicing)
            {
                Say(progress, deep ? "Windows bileşen deposu ayrıntılı taranıyor…" : "Windows bileşen deposunun kayıtlı durumu kontrol ediliyor…");
                var dism = await RunSystemAsync("dism.exe", new[] { "/Online", "/Cleanup-Image", deep ? "/ScanHealth" : "/CheckHealth", "/English" }, "dism-health", report.ReportDirectory, TimeSpan.FromMinutes(45));
                dismState = dism.TimedOut ? Integrity.Unknown : Dism(dism.Output, dism.ExitCode);
                AddIntegrity(report, "Windows bileşen deposu", dismState, dism, deep ? "Ayrıntılı DISM taraması." : "Hızlı kontrol yalnızca daha önce kaydedilmiş bozulmayı sorgular; tüm dosyaları taramaz.");
                if (deep)
                {
                    Say(progress, "Windows sistem dosyaları doğrulanıyor…");
                    var sfc = await RunSystemAsync("sfc.exe", new[] { "/verifyonly" }, "sfc-verify", report.ReportDirectory, TimeSpan.FromMinutes(45));
                    sfcState = sfc.TimedOut ? Integrity.Unknown : Sfc(sfc.Output, sfc.ExitCode);
                    AddIntegrity(report, "Windows sistem dosyaları", sfcState, sfc, "SFC salt okunur doğrulama.");
                    Say(progress, "C: dosya sistemi salt okunur denetleniyor…");
                    var disk = await RunSystemAsync("chkdsk.exe", new[] { "C:" }, "chkdsk-readonly", report.ReportDirectory, TimeSpan.FromMinutes(30));
                    // Without /F or /R, CHKDSK cannot repair. Exit 0 documents success; other results are inconclusive on an active volume.
                    Add(report, "Depolama", "C: dosya sistemi", disk.ExitCode == 0 && !disk.TimedOut ? "Healthy" : "Unknown", disk.ExitCode == 0 && !disk.TimedOut ? "CHKDSK salt okunur denetiminde hata bildirmedi. Etkin sürücüde sonuçlar anlık durumdur." : "Salt okunur CHKDSK kesin sonuç vermedi; etkin birimde geçici tutarsızlık olabilir. " + Failure(disk));
                }
            }
            else Add(report, "Windows", "Windows bütünlüğü", "Unknown", guard == null ? "Güvenlik koşulları doğrulanamadı." : !guard.Admin ? "Yönetici yetkisi yok; DISM/SFC çalıştırılmadı." : "Başka bir DISM/SFC işlemi çalışıyor; denetim ertelendi.");

            if (dismState == Integrity.Unrepairable)
                Add(report, "Onarım", "Bileşen deposu onarılamıyor", "Critical", "DISM bileşen deposunu onarılamaz olarak bildirdi. Otomatik onarım başlatılmadı; Windows kurtarma seçeneklerini inceleyin.");
            else if (RepairPolicy.HasFreshCorruption(deep, dismState, sfcState))
                await TryRepairAsync(report, settings, progress, manualRepair);
            else if (!deep && dismState == Integrity.Corrupt)
                Add(report, "Onarım", "Ayrıntılı tarama gerekli", "Info", "Hızlı DISM kontrolü kayıtlı bozulma bildirdi. Otomatik onarım için yeni ScanHealth ile ayrıntılı taramada güncel bozulma doğrulanmalıdır.");
            else if (settings.AutoRepair || manualRepair)
                Add(report, "Onarım", manualRepair ? "İstenen onarım" : "Otomatik onarım", "Info", "Güncel DISM/SFC çıktısında onarılabilir bozulma doğrulanmadığından onarım çalıştırılmadı.");
        }
        catch (Exception ex) { Add(report, "Tarama", "Tarama tamamlanamadı", "Unknown", ex.Message); }
        finally { scanLock?.Dispose(); }
        return Complete(report);
    }

    private async Task TryRepairAsync(ScanReport report, AppSettings settings, Action<string> progress, bool manualRepair)
    {
        if (!manualRepair && !settings.AutoRepair) { Add(report, "Onarım", "Onarım kapalı", "Info", "Bozulma doğrulandı; otomatik onarım ayarı kapalı."); return; }
        if (!manualRepair && settings.LastAutoRepairUtc.HasValue && DateTimeOffset.UtcNow - settings.LastAutoRepairUtc.Value < TimeSpan.FromDays(7))
        { Add(report, "Onarım", "Onarım bekleme süresi", "Info", "Son otomatik onarım girişiminin üzerinden en az yedi gün geçmelidir."); return; }
        Say(progress, "Onarım öncesi güç, yetki ve yeniden başlatma koşulları tekrar doğrulanıyor…");
        Guard? guard = null;
        try
        {
            var result = await CollectAsync(report.ReportDirectory, true);
            if (result.ExitCode != 0 || result.TimedOut) throw new InvalidOperationException(Failure(result));
            using var doc = JsonDocument.Parse(result.Output.Trim());
            var check = doc.RootElement.EnumerateArray().First(x => Text(x, "Name") == "Guard");
            if (!Bool(check, "Ok")) throw new InvalidOperationException(Text(check, "Error"));
            guard = ParseGuard(check.GetProperty("Data"));
        }
        catch (Exception ex) { Add(report, "Onarım", "Onarım koşulları", "Unknown", ex.Message); return; }
        report.RebootRequired |= guard.PendingReboot;
        // The UI may change preferences during the deep recheck. Respect its latest saved values.
        var latestSettings = File.Exists(LocalStore.SettingsPath) ? LocalStore.Read<AppSettings>(LocalStore.SettingsPath) : settings;
        if (latestSettings == null) { Add(report, "Onarım", "Onarım ayarları okunamadı", "Unknown", "Güncel ayarlar doğrulanamadığından onarım başlatılmadı."); return; }
        if (!RepairPolicy.CanRepair(true, manualRepair, latestSettings, guard.Admin, guard.AcOnline && guard.AcKnown, guard.PendingReboot, guard.OtherServicing, DateTimeOffset.UtcNow))
        {
            Add(report, "Onarım", manualRepair ? "İstenen onarım ertelendi" : "Otomatik onarım ertelendi", "Info", "Yönetici yetkisi, bağlı şebeke gücü, bekleyen yeniden başlatma olmaması ve başka DISM/SFC bulunmaması gerekir."); return;
        }
        // Both modes record the attempt before mutation so the next automatic scan respects cooldown.
        // AutoRepair remains the user's saved preference, including when a one-shot request overrides it.
        var previous = settings.LastAutoRepairUtc;
        latestSettings.LastAutoRepairUtc = DateTimeOffset.UtcNow;
        settings.LastAutoRepairUtc = latestSettings.LastAutoRepairUtc;
        try { LocalStore.SaveSettings(latestSettings); }
        catch (Exception ex) { settings.LastAutoRepairUtc = previous; Add(report, "Onarım", "Onarım kaydı saklanamadı", "Unknown", "Onarım başlatılmadı: " + ex.Message); return; }
        report.RepairAttempted = true;
        LocalStore.SaveReport(report);
        Say(progress, "Doğrulanmış bozulma onarılıyor: DISM. Bu işlem tamamlanana kadar uygulama açık kalır…");
        // Repairs deliberately have no cancellation/timeout kill path.
        var restore = await RunSystemAsync("dism.exe", new[] { "/Online", "/Cleanup-Image", "/RestoreHealth", "/NoRestart", "/English" }, "dism-restore", report.ReportDirectory, null);
        if (!DismRepairSucceeded(restore.Output, restore.ExitCode))
        {
            report.RebootRequired |= restore.ExitCode == 3010;
            Add(report, "Onarım", "DISM onarımı doğrulanamadı", "Unknown", "SFC onarımı başlatılmadı. " + Failure(restore)); return;
        }
        Say(progress, "Windows sistem dosyaları onarılıyor: SFC…");
        var sfc = await RunSystemAsync("sfc.exe", new[] { "/scannow" }, "sfc-repair", report.ReportDirectory, null);
        if (!SfcRepairSucceeded(sfc.Output, sfc.ExitCode)) Add(report, "Onarım", "SFC onarımı doğrulanamadı", "Unknown", Failure(sfc));
        Say(progress, "Onarım sonrası bileşen deposu ve sistem dosyaları tekrar doğrulanıyor…");
        var verifyDism = await RunSystemAsync("dism.exe", new[] { "/Online", "/Cleanup-Image", "/ScanHealth", "/English" }, "dism-after", report.ReportDirectory, TimeSpan.FromMinutes(45));
        var verifySfc = await RunSystemAsync("sfc.exe", new[] { "/verifyonly" }, "sfc-after", report.ReportDirectory, TimeSpan.FromMinutes(45));
        var goodDism = !verifyDism.TimedOut && Dism(verifyDism.Output, verifyDism.ExitCode) == Integrity.Healthy;
        var goodSfc = !verifySfc.TimedOut && Sfc(verifySfc.Output, verifySfc.ExitCode) == Integrity.Healthy;
        if (goodDism) MarkRepaired(report, "Windows bileşen deposu");
        if (goodSfc) MarkRepaired(report, "Windows sistem dosyaları");
        Add(report, "Onarım", "Onarım sonrası doğrulama", goodDism && goodSfc ? "Repaired" : "Unknown", goodDism && goodSfc ? "DISM ve SFC tekrar denetlendi; bütünlük doğrulandı." : "İki doğrulamanın da temiz olduğu doğrulanamadı. Ham kayıtları inceleyin.");
        try
        {
            var after = await CollectAsync(report.ReportDirectory, true);
            using var doc = JsonDocument.Parse(after.Output.Trim());
            var check = doc.RootElement.EnumerateArray().First();
            if (Bool(check, "Ok")) report.RebootRequired |= ParseGuard(check.GetProperty("Data")).PendingReboot;
        }
        catch { Add(report, "Windows", "Onarım sonrası yeniden başlatma durumu", "Unknown", "Durum okunamadı; Windows bildirimlerini kontrol edin."); }
    }

    internal static void Interpret(ScanReport report, string name, JsonElement data)
    {
        var rows = Rows(data).ToArray();
        if ((name.StartsWith("Events:") || name.StartsWith("FutureEvents:") || name=="MemoryTest") && rows.Any(x=>x.ValueKind!=JsonValueKind.Object || !Number(x,"Id").HasValue || string.IsNullOrWhiteSpace(Text(x,"Time")))) {
            Add(report,"Kontrol",name,"Unknown","Olay kaydı eksik veya geçersiz; arıza olarak sayılmadı."); return;
        }
        if (name.StartsWith("FutureEvents:"))
        {
            if (rows.Length > 0) Add(report, "Olay kayıtları", "Gelecek tarihli kayıt: " + name[13..], "Info", $"{rows.Length} kayıt saat/tarih tutarsızlığı gösteriyor; son yedi günün bulgularına dahil edilmedi.");
            return;
        }
        if (name.StartsWith("Events:"))
        {
            var provider = name[7..];
            if (rows.Length == 0) Add(report, "Olay kayıtları", provider, "Info", "Son yedi gün içinde bu sağlayıcıda kritik/hata/uyarı kaydı bulunmadı; donanımın kesin sağlam olduğu anlamına gelmez.");
            else Add(report, "Olay kayıtları", provider, provider.Contains("WHEA") && rows.Any(x => Number(x,"Level") <= 2) ? "Critical" : "Warning", $"Son yedi gün: en çok 40 kayıt içinden {rows.Length} olay. Geçmiş olaylar mevcut arızayı tek başına kanıtlamaz. " + string.Join(" | ", rows.Take(3).Select(x => $"{Text(x,"Time")} / {Text(x,"Id")}: {Text(x,"Message")}")));
            return;
        }
        switch (name)
        {
            case "Reliability":
                Add(report, "Kararlılık", "Güvenilirlik geçmişi", "Info", rows.Length == 0 ? "Son yedi gün için güvenilirlik kaydı yok; bu verinin kullanılabilirliği Windows ayarlarına bağlıdır." : $"En çok 60 kayıttan {rows.Length} geçmiş olay (başarılı kurulumlar da bu geçmişe girer): " + string.Join(" | ", rows.Take(3).Select(x => Text(x,"Source") + ": " + Text(x,"Message")))); break;
            case "Disks":
                if (rows.Length == 0) Add(report,"Depolama","Fiziksel diskler","Unknown","Fiziksel disk verisi alınamadı.");
                foreach (var disk in rows)
                {
                    var health = Text(disk,"Health");
                    Add(report,"Depolama",Text(disk,"Name"),health.Equals("Healthy",StringComparison.OrdinalIgnoreCase) ? "Healthy" : health is "Unhealthy" ? "Critical" : health is "Warning" ? "Warning" : "Unknown", $"Disk sağlık bildirimi: {health}; işlem durumu: {string.Join(", ",Rows(disk.GetProperty("Operational")).Select(x=>x.ToString()))}. Bu bildirim tam yüzey testi değildir.");
                    var temperature = Number(disk,"Temperature");
                    var validTemperature=temperature is >=5 and <=120;
                    Add(report,"Depolama",Text(disk,"Name")+" telemetri", !string.IsNullOrWhiteSpace(Text(disk,"ReliabilityError")) || !validTemperature ? "Unknown" : temperature >= 70 ? "Warning" : "Info", $"Sıcaklık: {(validTemperature ? temperature + " °C" : "bilinmiyor (sensör desteklenmiyor veya değer geçersiz)")}; aşınma: {Metric(disk,"Wear")}; düzeltilemeyen okuma/yazma: {Metric(disk,"ReadErrorsUncorrected")}/{Metric(disk,"WriteErrorsUncorrected")}. " + Text(disk,"ReliabilityError"));
                    if (Number(disk,"ReadErrorsUncorrected") > 0 || Number(disk,"WriteErrorsUncorrected") > 0) Add(report,"Depolama",Text(disk,"Name")+" hata sayacı","Warning","Disk geçmişinde düzeltilemeyen hata kaydı var; önemli dosyaları yedekleyip üretici tanı aracını kullanın.");
                } break;
            case "Volumes":
                foreach(var volume in rows)
                {
                    var size=Number(volume,"Size"); var free=Number(volume,"FreeSpace");
                    Add(report,"Depolama",Text(volume,"DeviceID")+" boş alan", !size.HasValue || size<=0 || !free.HasValue ? "Unknown" : free/size<0.10 ? "Warning":"Info",$"Boş: {free/1073741824:0.0} GB / toplam: {size/1073741824:0.0} GB.");
                } break;
            case "Devices":
                if(rows.Length==0) Add(report,"Aygıtlar","Aygıt durumları","Info","Windows aygıt hata kodu bildirmedi.");
                foreach(var device in rows) { var code=Number(device,"ConfigManagerErrorCode"); Add(report,"Aygıtlar",Text(device,"Name"),code==22 ? "Info":"Warning",code==22 ? "Aygıt devre dışı (kod 22); bu tek başına arıza değildir." : $"Windows aygıt hata kodu: {code}. Aygıt Yöneticisi üzerinden inceleyin."); } break;
            case "Cpu":
                var loads=rows.Select(x=>Number(x,"LoadPercentage")).Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();
                var frequencies=rows.Select(x=>Number(x,"EstimatedLiveMHz")).Where(x=>x is >0).Select(x=>x!.Value).ToArray();
                var nominal=rows.Select(x=>Number(x,"ReportedMHz")).Where(x=>x is >0).Select(x=>x!.Value).ToArray();
                var utility=rows.Select(x=>Number(x,"Utility")).Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();
                var frequencyDetail=frequencies.Length>0?$"Performans sayacından tahmini canlı frekans: {frequencies.Average():0} MHz; işlemci kullanım kapasitesi: {(utility.Length>0?utility.Average().ToString("0")+"%":"bilinmiyor")}":$"Canlı frekans okunamadı; Windows'un nominal/bildirilen frekansı: {(nominal.Length>0?nominal.Average().ToString("0")+" MHz":"bilinmiyor")}";
                Add(report,"İşlemci","İşlemci örneklemesi",loads.Length==0?"Unknown":"Info",loads.Length==0?"İşlemci yükü okunamadı.":$"Üç kısa örnekte ortalama yük %{loads.Average():0}. {frequencyDetail}. Frekans yük ve güç yönetimiyle değişir; bu ölçüm stres testi veya sıcaklık ölçümü değildir."); break;
            case "Memory":
                var memory=rows.FirstOrDefault(); var total=Number(memory,"TotalVisibleMemorySize"); var available=Number(memory,"FreePhysicalMemory");
                Add(report,"Bellek","RAM kullanımı",!total.HasValue||total<=0||!available.HasValue?"Unknown":available/total<0.05?"Warning":"Info",$"Kullanılabilir: {available/1048576:0.0} GB / toplam: {total/1048576:0.0} GB. Bu, RAM donanım testi değildir."); break;
            case "MemoryTest":
                if(rows.Length==0) Add(report,"Bellek","Önceki bellek testi","Unknown","Son 120 günde Windows Bellek Tanılama sonucu bulunamadı; yeni test veya yeniden başlatma yapılmadı.");
                else foreach(var test in rows) Add(report,"Bellek","Önceki bellek testi",Number(test,"Id") is 1202 or 1102?"Critical":Number(test,"Id") is 1201 or 1101?"Info":"Unknown",Text(test,"Time")+": "+Text(test,"Message")+" Geçmiş test, mevcut RAM durumunu garanti etmez."); break;
        }
    }

    private static Guard ParseGuard(JsonElement d)
    {
        foreach(var key in new[]{"Admin","AcOnline","AcKnown","PendingReboot","OtherServicing"})
            if(d.ValueKind!=JsonValueKind.Object || !d.TryGetProperty(key,out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidOperationException("Onarım koşulu eksik veya geçersiz: "+key);
        return new(Bool(d,"Admin"),Bool(d,"AcOnline"),Bool(d,"AcKnown"),Bool(d,"PendingReboot"),Bool(d,"OtherServicing"));
    }
    private static bool EmptyRow(JsonElement d) => d.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || d.ValueKind==JsonValueKind.Object && !d.EnumerateObject().Any();
    private static IEnumerable<JsonElement> Rows(JsonElement d) => d.ValueKind==JsonValueKind.Array ? d.EnumerateArray().Where(x=>!EmptyRow(x)) : EmptyRow(d) ? Array.Empty<JsonElement>() : new[]{d};
    private static string Text(JsonElement d,string key) => d.ValueKind==JsonValueKind.Object && d.TryGetProperty(key,out var v) && v.ValueKind!=JsonValueKind.Null ? v.ToString():"";
    private static bool Bool(JsonElement d,string key) => d.ValueKind==JsonValueKind.Object && d.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.True;
    private static double? Number(JsonElement d,string key) => d.ValueKind==JsonValueKind.Object && d.TryGetProperty(key,out var v) && v.TryGetDoubleSafe(out var number)?number:null;
    private static string Metric(JsonElement d,string key) => Number(d,key)?.ToString("0") ?? "bilinmiyor";
    private static void Add(ScanReport r,string category,string title,string status,string detail) => r.Findings.Add(new Finding{Category=category,Title=title,Status=status,Detail=detail});
    private static void Say(Action<string> progress,string message) { try { progress(message); } catch { /* UI observer cannot interrupt servicing. */ } }
    private static void MarkRepaired(ScanReport r,string title) { foreach(var finding in r.Findings.Where(x=>x.Title==title && x.Status=="Critical")) { finding.Status="Repaired"; finding.Detail+=" Onarım sonrası doğrulama temiz."; } }
    private static void AddIntegrity(ScanReport r,string title,Integrity state,CommandResult result,string context) => Add(r,"Windows",title,state==Integrity.Healthy?"Healthy":state is Integrity.Corrupt or Integrity.Unrepairable?"Critical":"Unknown",context+" "+(state==Integrity.Healthy?"Bütünlük kontrolü temiz bildirdi.":state==Integrity.Corrupt?"Bozulma açıkça doğrulandı.":state==Integrity.Unrepairable?"Bileşen deposu onarılamaz olarak bildirildi.":Failure(result)));
    private static string Failure(CommandResult result) => result.TimedOut ? "Kontrol süre sınırını aştı; sonuç bilinmiyor." : $"Çıkış kodu: {result.ExitCode}. " + (string.IsNullOrWhiteSpace(result.Error)? "Çıktı kesin sağlıklı/onarılabilir sonuç olarak tanınmadı; ham kaydı inceleyin.":result.Error[..Math.Min(result.Error.Length,1200)]);
    private static ScanReport Complete(ScanReport report)
    {
        report.FinishedUtc=DateTimeOffset.UtcNow;
        var critical=report.Findings.Count(x=>x.Status=="Critical"); var warning=report.Findings.Count(x=>x.Status=="Warning"); var unknown=report.Findings.Count(x=>x.Status=="Unknown");
        report.Summary=critical>0?$"{critical} ciddi bulgu; {warning} uyarı; {unknown} belirsiz kontrol.":warning>0?$"{warning} uyarı; {unknown} belirsiz kontrol.":unknown>0?$"{unknown} kontrolün sonucu bilinmiyor.":"Denetlenen alanlarda ciddi bulgu yok.";
        return report;
    }

    private static async Task<CommandResult> CollectAsync(string directory,bool guardOnly)
    {
        var assembly=Assembly.GetExecutingAssembly();
        var resource=assembly.GetManifestResourceNames().Single(x=>x.EndsWith("Diagnostics.Collect.ps1",StringComparison.Ordinal));
        using var stream=assembly.GetManifestResourceStream(resource)!;
        using var reader=new StreamReader(stream,Encoding.UTF8);
        var script=await reader.ReadToEndAsync();
        var encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        if(encoded.Length>30000) throw new InvalidOperationException("Gömülü tanılama komutu Windows uzunluk sınırını aşıyor.");
        return await RunSystemAsync("WindowsPowerShell\\v1.0\\powershell.exe",new[]{"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",encoded},guardOnly?"guard-"+DateTime.UtcNow.ToString("HHmmssfff"):"hardware",directory,TimeSpan.FromMinutes(6),utf8:true,guardOnly:guardOnly);
    }
    private static async Task<CommandResult> RunSystemAsync(string executable,string[] args,string logName,string directory,TimeSpan? timeout,bool utf8=false,bool guardOnly=false)
    {
        try
        {
            var system=Environment.GetFolderPath(Environment.SpecialFolder.System);
            var info=new ProcessStartInfo(Path.Combine(system,executable)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true};
            foreach(var arg in args) info.ArgumentList.Add(arg);
            info.Environment["PUSULA_GUARD_ONLY"]=guardOnly?"1":"0";
            using var process=new Process{StartInfo=info};
            process.Start();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var output=ReadBoundedAsync(process.StandardOutput.BaseStream,Path.Combine(directory,logName+".stdout.log"),utf8);
            var error=ReadBoundedAsync(process.StandardError.BaseStream,Path.Combine(directory,logName+".stderr.log"),utf8);
            process.StandardInput.Close();
            bool timedOut=false;
            if(timeout.HasValue)
            {
                using var cancellation=new CancellationTokenSource(timeout.Value);
                try { await process.WaitForExitAsync(cancellation.Token); }
                catch(OperationCanceledException) { timedOut=true; try { process.Kill(entireProcessTree:true); } catch { } await process.WaitForExitAsync(); }
            }
            else await process.WaitForExitAsync();
            return new(process.ExitCode,await output,await error,timedOut);
        }
        catch(Exception ex) { return new(-1,"",ex.Message,false); }
    }
    private static async Task<string> ReadBoundedAsync(Stream source,string logPath,bool utf8)
    {
        FileStream? log=null;
        try { log=new FileStream(logPath,FileMode.Create,FileAccess.Write,FileShare.Read); } catch(IOException) { } catch(UnauthorizedAccessException) { }
        using var capture=new MemoryStream(); var buffer=new byte[8192]; var tail=new MemoryStream(); long written=0;
        int count;
        while((count=await source.ReadAsync(buffer))>0)
        {
            if(log!=null && written<8*1024*1024)
            {
                var save=(int)Math.Min(count,8*1024*1024-written);
                try { await log.WriteAsync(buffer.AsMemory(0,save)); written+=save; }
                catch(IOException) { log.Dispose(); log=null; }
                catch(UnauthorizedAccessException) { log.Dispose(); log=null; }
            }
            if(capture.Length<CaptureLimit) { var save=(int)Math.Min(count,CaptureLimit-capture.Length); capture.Write(buffer,0,save); }
            tail.Write(buffer,0,count);
            if(tail.Length>65536) { var bytes=tail.ToArray(); tail.SetLength(0); tail.Write(bytes,bytes.Length-32768,32768); }
        }
        if(log!=null) { try { await log.FlushAsync(); } catch(IOException) { } finally { log.Dispose(); } }
        var bytesOut=capture.ToArray();
        var unicode=!utf8 && bytesOut.Take(Math.Min(200,bytesOut.Length)).Count(b=>b==0)>10;
        var encoding=unicode?Encoding.Unicode:utf8?Encoding.UTF8:Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var text=encoding.GetString(bytesOut);
        if(capture.Length>=CaptureLimit) text+="\n[output capture truncated; final output follows]\n"+encoding.GetString(tail.ToArray());
        return text.Trim('\uFEFF','\0');
    }
}

internal static class JsonNumberExtensions
{
    public static bool TryGetDoubleSafe(this JsonElement value,out double result)
    { result=0; return value.ValueKind==JsonValueKind.Number && value.TryGetDouble(out result); }
}
