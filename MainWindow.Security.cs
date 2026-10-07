using System.IO;
using System.Windows;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;

public partial class MainWindow
{
    private bool antivirusReading;
    private DateTimeOffset antivirusShown;
    private static string AntivirusPath=>Path.Combine(LocalStore.Root,"antivirus.json");
    private static string AntivirusProgressPath=>Path.Combine(LocalStore.Root,"antivirus-progress.json");
    private async void Antivirus_Click(object sender,RoutedEventArgs e)
    {
        Page("antivirus");
        var cached=demo?null:await Task.Run(()=>LocalStore.Read<AntivirusReport>(AntivirusPath));
        if(windowClosed || AntivirusPanel.Visibility!=Visibility.Visible)return;
        if(cached!=null)RenderAntivirus(cached);
        else if(!demo)await ReadAntivirus();
    }
    private async void AntivirusRefresh_Click(object sender,RoutedEventArgs e)=>await ReadAntivirus();
    private async Task ReadAntivirus()
    {
        if(demo || antivirusReading)return;
        antivirusReading=true; AntivirusRefresh.IsEnabled=false; AntivirusStatus.Text=T("Koruma durumu okunuyor…");
        try {var report=await Task.Run(()=>AntivirusService.ReadAsync()); await Task.Run(()=>LocalStore.Write(AntivirusPath,report));RenderAntivirus(report);}
        catch(Exception ex){AntivirusStatus.Text=F("Koruma durumu doğrulanamadı: {0}",DynamicTranslations.KnownText(ex.Message));}
        finally {antivirusReading=false;AntivirusRefresh.IsEnabled=true;}
    }
    private void RenderAntivirus(AntivirusReport report)
    {
        antivirusShown=report.CheckedUtc;
        AntivirusStatus.Text=F("{0:g} · {1}",report.CheckedUtc.ToLocalTime(),DynamicTranslations.KnownText(report.Summary))+"\n"+T("Kaynak tanılama çıktıları özgün dilinde korunur.");
        AntivirusFindings.ItemsSource=report.Findings.Select(x=>new FindingRow(x)).ToList();
        AntivirusQuick.IsEnabled=AntivirusFull.IsEnabled=AntivirusUpdate.IsEnabled=report.CanScan;
    }
    private bool antivirusProgressReading;
    private async void RefreshAntivirusProgress()
    {
        if(demo || antivirusProgressReading || AntivirusPanel.Visibility!=Visibility.Visible)return;
        antivirusProgressReading=true;
        try {
        var snapshot=await Task.Run(()=>{var p=LocalStore.Read<ScanProgress>(AntivirusProgressPath);return (progress:p,active:p?.Running==true && IsAlive(p.ProcessId),report:LocalStore.Read<AntivirusReport>(AntivirusPath));});
        if(windowClosed)return;
        var progress=snapshot.progress;var active=snapshot.active;var report=snapshot.report;
        if(report!=null && report.CheckedUtc>antivirusShown)RenderAntivirus(report);
        AntivirusProgress.Text=DynamicTranslations.KnownText(active?progress!.Message:progress?.Running==true?"Önceki işlem kesilmiş olabilir; durumu yenileyin.":progress?.Message??"");
        AntivirusQuick.IsEnabled=AntivirusFull.IsEnabled=AntivirusUpdate.IsEnabled=!active && report?.CanScan==true;
        } finally {antivirusProgressReading=false;}
    }
    private void AntivirusUpdate_Click(object sender,RoutedEventArgs e)=>StartAntivirus("update");
    private void AntivirusQuick_Click(object sender,RoutedEventArgs e)=>StartAntivirus("quick");
    private void AntivirusFull_Click(object sender,RoutedEventArgs e)=>StartAntivirus("full");
    private void StartAntivirus(string action)
    {
        if(demo)return;
        string detail=action=="update"?T("Microsoft Defender tehdit verileri resmî kaynaktan güncellenecek."):F("Önce Microsoft Defender tehdit verileri güncellenecek, ardından {0} tarama çalışacak. Defender mevcut ilkelerine göre tehditleri karantinaya alabilir. Tam tarama uzun sürebilir.",T(action=="full"?"tam":"hızlı"));
        if(MessageBox.Show(detail+"\n\n"+T("Devam edilsin mi?"),F("SystemCompass — {0}",T("Antivirüs kontrolü")),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        try {App.Elevate("--antivirus "+action); AntivirusProgress.Text=T("Windows yönetici izni ve işlem başlangıcı bekleniyor…");}
        catch(Exception ex){AntivirusProgress.Text=F("İşlem başlatılmadı: {0}",DynamicTranslations.KnownText(ex.Message));}
    }
}
