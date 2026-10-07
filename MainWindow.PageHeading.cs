using static SistemPusulasi.L10n;
namespace SistemPusulasi;

public partial class MainWindow
{
    private void SetPageHeading(string page)
    {
        var heading = page switch {
            "updates" when updateKind == "drivers" => ("Sürücü güncellemeleri", "Driver updates", "Windows Update üzerinden sunulan donanım sürücülerini kontrol edin.", "Check hardware drivers offered through Windows Update."),
            "updates" when updateKind == "software" => ("Yazılım güncellemeleri", "Software updates", "Uygulamaların yeni sürümlerini bulun ve seçtiklerinizi güncelleyin.", "Find newer application versions and update the items you select."),
            "updates" => ("Windows Update", "Windows Update", "Bu bilgisayar için sunulan Windows güncellemelerini inceleyin.", "Review Windows updates offered for this computer."),
            "history" => ("Rapor geçmişi", "Report history", "Önceki kontrolleri açın ve o tarihteki sonuçları inceleyin.", "Open previous checks and review their results at the time."),
            "antivirus" => ("Antivirüs ve koruma", "Antivirus and protection", "Koruma durumunu ve desteklenen güvenlik işlemlerini inceleyin.", "Review protection status and supported security actions."),
            "settings" => ("Bakım ayarları", "Maintenance settings", "Kontrol planını, dili ve uygulama güncellemelerini yönetin.", "Manage scheduled checks, language and application updates."),
            _ => ("Bilgisayarının durumu", "Your computer's status", "Kontrolün ilerlemesini izleyin ve sonuçları kategorilere göre inceleyin.", "Follow scan progress and review results by category.")
        };
        PageTitle.Text=L10n.Language=="en"?heading.Item2:heading.Item1;
        Subtitle.Text=L10n.Language=="en"?heading.Item4:heading.Item3;
    }
}
