using System.IO;
using System.Windows;
namespace SistemPusulasi;

public partial class MainWindow
{
    internal static void CheckDiagnosticDetailUi(Action<bool,string> check)
    {
        var previous=L10n.Language;
        const string source="Sıcaklık: bilinmiyor (sensör desteklenmiyor veya değer geçersiz); aşınma: 0; düzeltilemeyen okuma/yazma: bilinmiyor/bilinmiyor.";
        var finding=new Finding{Category="Depolama",Title="Samsung SSD 970 EVO Plus 500GB telemetri",Status="Unknown",Detail=source};
        try {
            foreach(var language in new[]{"en","tr"}) {
                L10n.SetLanguage(language);
                var window=new MainWindow(true);
                try {
                    var report=new ScanReport{IsDemo=true,AppVersion="preview",Summary="1 kontrolün sonucu bilinmiyor.",Findings=new(){finding}};
                    window.ShowReport(report);window.HealthTabs.SelectedItem=window.HealthAttentionTab;
                    check(language=="en"?window.HealthDetailText.Text.StartsWith("Temperature:"):window.HealthDetailText.Text==source,"Selected diagnostic detail follows application language: "+language);
                    check(language!="en" || HealthTranslations.Detail(finding.Title+": "+source).Contains("telemetry: Temperature:"),"Progress stage detail preserves whole diagnostic template before splitting: "+language);
                    check(finding.Detail==source,"Viewing translated details preserves stored report text: "+language);
                    var directory=Path.Combine(AppContext.BaseDirectory,"ui-previews");Directory.CreateDirectory(directory);
                    RenderPreview((FrameworkElement)window.Content,1240,800,Path.Combine(directory,"diagnostic-detail-"+language+".png"));
                } finally {window.Close();}
            }
        } finally {L10n.SetLanguage(previous);}
    }
}
