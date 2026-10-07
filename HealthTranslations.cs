namespace SistemPusulasi;
public static class HealthTranslations
{
    private static readonly Dictionary<string,string> English=new(StringComparer.Ordinal) {
        ["Tarama durumu"]="Scan progress", ["Dikkat isteyenler"]="Needs attention", ["Windows"]="Windows", ["Depolama"]="Storage", ["Donanım / güç"]="Hardware / power", ["Olaylar / diğer"]="Events / other",
        ["Tarama başlatılamadı."]="Scan could not be started.", ["KESİLEN TARAMA • {0} kısmi bulgu; tamamlanmayan kontroller doğrulanamadı."]="INTERRUPTED SCAN • {0} partial findings; unfinished checks remain unverified.",
        ["Kategori"]="Category", ["Kontrol"]="Check", ["Sonuç"]="Result", ["Tarama adımı"]="Scan step", ["Durum"]="State", ["HENÜZ RAPOR YOK"]="NO REPORT YET",
        ["Bulgular sekmelerde gruplanır. Bir satır seçerek ayrıntıların tamamını okuyun."]="Findings are grouped in tabs. Select a row to read its full details.",
        ["Adım ayrıntıları"]="Step details", ["Tarama başladığında adımlar ve gerçek sonuçları burada görünür."]="Steps and their actual results appear here when the scan starts.",
        ["CANLI • {0} sonuç alındı; kalan kontroller henüz tamamlanmadı."]="LIVE • {0} findings received; remaining checks are unfinished.",
        ["Henüz tamamlanmış rapor yok."]="No completed report yet.", ["TAMAMLANMIŞ RAPOR • {0:g} • {1} bulgu"]="COMPLETED REPORT • {0:g} • {1} findings",
        ["Üstteki özet önceki rapora aittir: {0:g}."]="The summary above belongs to the previous report: {0:g}.",
        ["Yeni tarama sürüyor; bu geçmiş rapor değişmez."]="A new scan is running; this historical report is unchanged.",
        ["Bu grupta henüz sonuç alınmadı."]="No results received in this group yet.", ["Bir kontrol başlatın veya geçmiş raporlardan birini açın."]="Start a check or open a previous report.", ["Bu grupta bulgu yok."]="No findings in this group.",
        ["Bulgu ayrıntıları"]="Finding details", ["Bir satır seçerek ayrıntıların tamamını okuyun. Doğrulanamayan kontroller başarı sayılmaz."]="Select a row to read its full details. Unverified checks do not count as success.",
        ["Tarama başlangıcı"]="Starting scan", ["Diğer sayfaları kullanabilirsiniz. Sonuçlar tarama ilerledikçe burada güncellenir."]="You can use other pages. Results update here as the scan progresses.",
        ["Şu an: {0}"]="Current stage: {0}", ["Henüz tarama adımı yok."]="No scan steps yet.", ["Tarama tamamlandı"]="Scan finished", ["Raporun tarama adımları"]="Report scan steps",
        ["Tarama kesildi; tamamlanmayan adımlar doğrulanamadı."]="Scan interrupted; unfinished steps could not be verified.",
        ["Eski raporlarda adım zaman çizelgesi bulunmayabilir."]="Older reports may not contain a step timeline.", ["{0} tamamlandı · {1} atlandı · {2} doğrulanamadı · {3} bekliyor"]="{0} completed · {1} skipped · {2} unverified · {3} pending", ["Geçen süre: {0}"]="Elapsed: {0}",
        ["Çalışıyor"]="Running", ["Tamamlandı"]="Completed", ["Atlandı"]="Skipped", ["Doğrulanamadı"]="Unverified", ["Bekliyor"]="Pending",
        ["Bu adım henüz başlamadı."]="This step has not started yet.", ["Bu adım sürüyor. Sonuç henüz doğrulanmadı."]="This step is running. Its result is not verified yet.", ["Bu adım bu taramada uygulanmadı."]="This step was not performed in this scan.", ["Bu adım için ek ayrıntı yok."]="No additional details for this step.",
        ["Sistem, donanım ve olay kayıtları"]="System, hardware and event logs", ["Windows bileşen deposu (DISM)"]="Windows component store (DISM)", ["Windows sistem dosyaları (SFC)"]="Windows system files (SFC)", ["C: dosya sistemi (CHKDSK)"]="C: file system (CHKDSK)",
        ["Onarım gereksinimi ve koşulları"]="Repair eligibility and conditions", ["DISM onarımı"]="DISM repair", ["SFC onarımı"]="SFC repair", ["Onarım sonrası DISM doğrulaması"]="DISM verification after repair", ["Onarım sonrası SFC doğrulaması"]="SFC verification after repair",
        ["Windows güncelleme envanteri"]="Windows update inventory", ["Sürücü güncelleme envanteri"]="Driver update inventory", ["Yazılım güncelleme envanteri"]="Software update inventory", ["Antivirüs koruma durumu"]="Antivirus protection status", ["Raporun kaydedilmesi"]="Saving report",
        ["Hızlı taramada çalıştırılmaz; ayrıntılı tarama gerekir."]="Not run in a quick scan; a comprehensive scan is required.", ["İstenen onarımın kapsamı dışında."]="Outside the scope of the requested repair.", ["Güncelleme envanteri ayrıntılı taramada kontrol edilir."]="Update inventory is checked during a comprehensive scan.",
        ["Donanım, disk, güç ve olay kayıtları birlikte toplandı. Tek tek kontrollerin sonuçları aşağıdaki bulgulardadır."]="Hardware, disk, power and event records were collected together. Individual check results appear in the finding tabs.",
        ["Tarama işlemi durdu; bu kontrolün sonucu doğrulanamadı."]="The scan process stopped; this check could not be verified.", ["Tarama işlemi durdu; tamamlanmayan kontrollerin sonucu bilinmiyor."]="The scan process stopped; unfinished check results are unknown.", ["Bu adımın tamamlandığı doğrulanamadı."]="Completion of this step could not be verified.", ["Tarama tamamlanamadı; bu adımın sonucu doğrulanamadı."]="The scan did not finish; this step could not be verified.",
        ["Başka tarama çalışıyor; kontroller başlatılmadı."]="Another scan is running; these checks were not started.", ["Önceki tarama adımı tamamlanamadı."]="The previous scan step did not finish.", ["Bu denetimin kapsamı dışında."]="Outside the scope of this check.", ["Önceki kontrolün tamamlandığı doğrulanamadı."]="Completion of the preceding check could not be verified.",
        ["Onarım gereksinimi değerlendirildi; onarım yalnızca güncel bozulma ve güvenlik koşulları doğrulanınca çalışır."]="Repair eligibility was assessed; repair runs only after current corruption and safety conditions are verified.",
        ["Onarım çalıştırılmadı veya önceki onarım adımı doğrulanamadı."]="Repair was not run, or the previous repair step could not be verified.",
        ["Güncel bozulma ve onarım koşulları doğrulandı; onarım başlatılıyor."]="Current corruption and repair conditions were verified; starting repair.",
        ["DISM onarım komutu başarı bildirdi; bütünlük onarım sonrası taramayla doğrulanacak."]="The DISM repair command reported success; integrity will be verified by a subsequent scan.",
        ["SFC onarım komutu başarı bildirdi; bütünlük yeniden doğrulanacak."]="The SFC repair command reported success; integrity will be verified again.",
        ["DISM yeniden taradı; bileşen deposu bütünlüğü doğrulandı."]="DISM rescanned and verified component store integrity.", ["DISM yeniden taradı; bileşen deposu bozulması devam ediyor."]="DISM rescanned; component store corruption remains.",
        ["SFC yeniden doğruladı; sistem dosyaları bütünlüğü doğrulandı."]="SFC rechecked and verified system file integrity.", ["SFC yeniden doğruladı; sistem dosyası bozulması devam ediyor."]="SFC rechecked; system file corruption remains.",
        ["Tarama raporu ve okunabilir kayıt kaydedildi."]="The scan report and readable log were saved.", ["Rapor kaydediliyor…"]="Saving report…", ["Rapor kaydı tamamlanamadı."]="The report could not be saved.",
        ["Donanım, güç ve son yedi günün olay kayıtları kontrol ediliyor…"]="Checking hardware, power and event records from the last seven days…", ["Windows bileşen deposu ayrıntılı taranıyor…"]="Scanning the Windows component store…", ["Windows bileşen deposunun kayıtlı durumu kontrol ediliyor…"]="Checking the recorded Windows component store state…", ["Windows sistem dosyaları doğrulanıyor…"]="Verifying Windows system files…", ["C: dosya sistemi salt okunur denetleniyor…"]="Checking the C: file system in read-only mode…", ["Onarım gereksinimi ve koşulları değerlendiriliyor…"]="Assessing repair eligibility and conditions…",
        ["Doğrulanmış bozulma onarılıyor: DISM…"]="Repairing verified corruption: DISM…", ["Windows sistem dosyaları onarılıyor: SFC…"]="Repairing Windows system files: SFC…", ["Onarım sonrası Windows bileşen deposu doğrulanıyor…"]="Verifying the Windows component store after repair…", ["Onarım sonrası Windows sistem dosyaları doğrulanıyor…"]="Verifying Windows system files after repair…",
        ["DİKKAT İSTEYEN"]="NEEDS ATTENTION", ["CANLI TARAMA • ÖZET HENÜZ HAZIR DEĞİL"]="LIVE SCAN • SUMMARY NOT READY YET"
    };
    public static void Register()
    {
        L10n.RegisterEnglish(English);
        L10n.RegisterEnglish(new Dictionary<string,string> {
            ["KESİLMİŞ RAPOR • {0:g} • {1} bulgu"]="INTERRUPTED REPORT • {0:g} • {1} findings",
            ["KISMİ RAPOR • {0:g} • {1} bulgu"]="PARTIAL REPORT • {0:g} • {1} findings",
            ["Yeniden başlatma bekliyor; bilgisayarı uygulama yeniden başlatmaz."]="Restart pending; the app will not restart your computer.",
            ["Onarım denendi; doğrulama sonuçlarını Windows sekmesinde inceleyin."]="Repair was attempted; review verification results in the Windows tab."
        });
    }
    public static string StageTitle(string id,string fallback)=>L10n.T(fallback);
    public static string Detail(string text)
    {
        return string.Join("\n",text.Split('\n').Select(line=>{
            if(L10n.EnglishTranslations.ContainsKey(line))return L10n.T(line);
            var separator=line.IndexOf(": ",StringComparison.Ordinal);
            return separator>0?DynamicTranslations.KnownText(line[..separator])+": "+DynamicTranslations.KnownText(line[(separator+2)..]):DynamicTranslations.KnownText(line);
        }));
    }
}
