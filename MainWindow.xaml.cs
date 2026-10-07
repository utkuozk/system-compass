using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;

public partial class MainWindow : Window
{
    private readonly bool demo;
    private readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromSeconds(10)};
    private AppSettings settings;
    private string? selectedReportId;
    private ScanReport? shownReport;
    private bool workerRunning;
    private bool updatingHistory;
    private string updateKind="windows";
    private bool updateScanRunning => inventoryJobs.Count > 0;
    private bool refreshRunning, windowClosed;
    private string historySignature="";
    private DateTime nextReleaseCheck=DateTime.MinValue;
    public MainWindow(bool isDemo=false)
    {
        InitializeComponent(); demo=isDemo;
        InitializeHealthDashboard();
        InitializeWindowBehavior();
        settings=demo ? new() : LocalStore.LoadSettings();
        HourPicker.ItemsSource=Enumerable.Range(0,24).Select(h=>$"{h:00}:00"); HourPicker.SelectedIndex=Math.Clamp(settings.ScheduleHour,0,23);
        IntervalPicker.ItemsSource=new[]{T("Her 7 günde (önerilen)"),T("Her 14 günde"),T("Her 30 günde")}; IntervalPicker.SelectedIndex=settings.DeepScanIntervalDays==14?1:settings.DeepScanIntervalDays==30?2:0;
        AutoRepairCheck.IsChecked=settings.AutoRepair; ShowReportCheck.IsChecked=settings.ShowReportAfterScheduledScan;
        InitializeGitHubSettings();
        InitializeResultActions();
        if(demo) { Title+=T(" — Arayüz önizlemesi"); FooterText.Text=T("ÖRNEK VERİ • Bu önizleme gerçek tarama yapmaz ve ayarları değiştirmez."); ShowReport(DemoReport()); ScheduleBadge.Text=T("Önizleme"); }
        else { Refresh(); timer.Tick+=(_,_)=>Refresh(); timer.Start(); }
        InitializeInventoryProgress();
        Closed+=(_,_)=>{windowClosed=true;timer.Stop();actionTimer.Stop();inventoryTimer.Stop();foreach(var job in inventoryJobs.Values)job.Cancel.Cancel();};
    }
    private async void Refresh()
    {
        if(refreshRunning || windowClosed)return;
        refreshRunning=true;
        try {
            AutomaticGitHubCheck();
            var snapshot=await Task.Run(()=> {
                var reports=LocalStore.Reports();
                var progress=LocalStore.Read<ScanProgress>(LocalStore.ProgressPath);
                return (reports,progress,active:progress?.Running==true && IsScanAlive(progress),installed:SchedulerService.IsInstalled(),release:ReleaseFeed.Check());
            });
            if(windowClosed)return;
            var reports=snapshot.reports;
            var signature=string.Join("|",reports.Select(r=>$"{r.Id}:{r.FinishedUtc}:{r.Summary}"));
            if(signature!=historySignature) {
                historySignature=signature;
                updatingHistory=true;HistoryList.ItemsSource=reports.Select(r=>new HistoryRow(r)).ToList();updatingHistory=false;
            }
            if(selectedReportId==null && reports.Count>0 && (shownReport?.Id!=reports[0].Id || shownReport?.FinishedUtc!=reports[0].FinishedUtc))ShowReport(reports[0]);
            HistoryEmpty.Visibility=reports.Count==0?Visibility.Visible:Visibility.Collapsed;
            workerRunning=snapshot.active;
            ProgressPanel.Visibility=Visibility.Collapsed;
            ProgressText.Text=DynamicTranslations.KnownText(snapshot.progress?.Message??"");
            RenderHealthProgress(snapshot.progress,snapshot.active);
            QuickButton.IsEnabled=!workerRunning;DeepButton.IsEnabled=!workerRunning;
            ScheduleBadge.Text=T(snapshot.installed?"Günlük plan etkin":"Plan etkin değil");
            if(DateTime.UtcNow>=nextReleaseCheck) {
                nextReleaseCheck=DateTime.UtcNow.AddMinutes(1);
                var release=snapshot.release;
                ReleaseBanner.Visibility=release==null && githubRelease==null?Visibility.Collapsed:Visibility.Visible;
                ReleaseText.Text=githubRelease!=null?F("GitHub üzerinde {0} sürümü hazır.",githubRelease.Version):release==null?"":F("SystemCompass {0} hazır — {1}",release.Version,release.Notes);
            }
            RefreshAntivirusProgress();
            RefreshResultActions();
        } catch(Exception ex) { if(!windowClosed)ProgressText.Text=F("Kontrol tamamlanamadı: {0}",ex.Message); }
        finally {refreshRunning=false;}
    }
    private static bool IsAlive(int pid) { try { using var p=Process.GetProcessById(pid); return !p.HasExited && p.ProcessName.Equals("SistemPusulasi",StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    private void ShowReport(ScanReport report)
    {
        shownReport=report;
        int issues=report.Findings.Count(f=>f.Status is "Warning" or "Critical");
        int known=report.Findings.Count(f=>f.Status is not "Unknown");
        HeroEyebrow.Text=T(report.IsDemo?"ÖRNEK RAPOR • GERÇEK ÖLÇÜM DEĞİL":report.DeepScan?"KAPSAMLI KONTROL":"HAFİF KONTROL");
        HeroTitle.Text=DynamicTranslations.KnownText(report.Summary);
        HeroDetail.Text=T(report.RebootRequired?"Bazı işlemler yeniden başlatma bekliyor. Uygulama bilgisayarını yeniden başlatmaz.":report.RepairAttempted?"Onarım denemesi yapıldı. Sonuç ve doğrulama ayrıntıları aşağıda.":"Bulgular ve erişilemeyen kontroller aşağıda ayrı gösterilir.");
        HeroDetail.Text+=" "+T("Kaynak tanılama çıktıları özgün dilinde korunur.");
        HeroDetail.ToolTip=HeroDetail.Text;
        HeroDetail.Text=T(report.RebootRequired?"Yeniden başlatma bekliyor; bilgisayarı uygulama yeniden başlatmaz.":report.RepairAttempted?"Onarım denendi; doğrulama sonuçlarını Windows sekmesinde inceleyin.":"Bulgular sekmelerde gruplanır. Bir satır seçerek ayrıntıların tamamını okuyun.");
        IssueCount.Text=issues.ToString(); CoverageCount.Text=$"{known} / {report.Findings.Count}";
        LastScanText.Text=report.StartedUtc.ToLocalTime().ToString("dd MMM · HH:mm",System.Globalization.CultureInfo.CurrentCulture);
        ScanKindText.Text=T(report.DeepScan?"Kapsamlı tarama":"Hafif kontrol");
        if(!report.IsDemo && string.IsNullOrEmpty(report.AppVersion)) { HeroEyebrow.Text+=T(" · ESKİ SÜRÜM RAPORU"); HeroDetail.Text=T("Bu rapor eski sürümle oluşturuldu; bilinen sayım hatasını içerebilir. Güncel sonuç için yeniden kontrol edin.")+" "+T("Kaynak tanılama çıktıları özgün dilinde korunur."); }
        RenderHealthResults();
        RenderRepairAction();
    }
    internal static string DiagnosticLabel(ScanReport report,string title) {
        var f=report.Findings.LastOrDefault(x=>x.Title==title);
        return T(f==null?"Bu taramada kontrol edilmedi":f.Status switch {"Healthy"=>"Sorun bulunmadı","Critical"=>"Bozulma bulundu","Repaired"=>"Onarıldı ve doğrulandı",_=>"Sonuç doğrulanamadı"});
    }
    private void Page(string page) { OtherPagesScroll.Visibility=page=="overview"?Visibility.Collapsed:Visibility.Visible; AntivirusPanel.Visibility=page=="antivirus"?Visibility.Visible:Visibility.Collapsed; OverviewPanel.Visibility=page=="overview"?Visibility.Visible:Visibility.Collapsed; HistoryPanel.Visibility=page=="history"?Visibility.Visible:Visibility.Collapsed; SettingsPanel.Visibility=page=="settings"?Visibility.Visible:Visibility.Collapsed; UpdatesPanel.Visibility=page=="updates"?Visibility.Visible:Visibility.Collapsed; PageTitle.Text=T(page=="antivirus"?"Antivirüs ve koruma":page=="overview"?"Bilgisayarının durumu":page=="history"?"Rapor geçmişi":page=="updates"?"Güncelleme merkezi":"Bakım ayarları"); }
    private void WindowsUpdates_Click(object sender,RoutedEventArgs e)=>ShowUpdates("windows");
    private void SoftwareUpdates_Click(object sender,RoutedEventArgs e)=>ShowUpdates("software");
    private void DriverUpdates_Click(object sender,RoutedEventArgs e)=>ShowUpdates("drivers");
    private async void ShowUpdates(string kind) {
        updateKind=kind;Page("updates");
        UpdateTitle.Text=T(kind=="windows"?"Windows Update":kind=="drivers"?"Sürücü güncellemeleri":"Yazılım güncellemeleri");
        UpdateDescription.Text=T(kind=="software"?"WinGet'in tanıyabildiği uygulamalar kontrol edilir. Desteklenmeyen yazılımlar için güncellik doğrulanamaz.":kind=="drivers"?"Windows Update'in bu bilgisayara sunduğu sürücüler listelenir. Bu liste tüm üretici sürücülerini kapsamaz.":"Windows'un bu bilgisayar için sunduğu güncellemeleri kontrol et. Bu işlem indirme veya kurulum başlatmaz.");
        PopulateUpdates(null);UpdateStatus.Text=T("Sonuçlar okunuyor…");RenderInventoryProgress();
        var cached=demo?null:await Task.Run(()=>LocalStore.Read<UpdateInventoryResult>(Path.Combine(LocalStore.Root,"updates-"+kind+".json")));
        if(windowClosed || updateKind!=kind)return;
        if(inventoryResults.TryGetValue(kind,out var latest))cached=latest;
        PopulateUpdates(cached);
        UpdateStatus.Text=inventoryMessages.TryGetValue(kind,out var message)?message:cached==null?T("Henüz kontrol edilmedi. Güncellemeleri denetle düğmesini kullan."):F("Son kontrol {0:g} — {1}",cached.CheckedUtc.ToLocalTime(),DisplayUpdateStatus(cached.Status));
        RenderInventoryProgress();
    }
    private static string DisplayUpdateStatus(string status)=>DynamicTranslations.KnownText(status).Replace("Unknown:",T("Doğrulanamadı:")).Replace("Checked:",T("Kontrol edildi:")).Replace("availabilityUnknown",T("sürüm karşılaştırılamadı"));
    private void OpenUpdates_Click(object sender,RoutedEventArgs e) {
        if(demo)return;
        try { Process.Start(new ProcessStartInfo(updateKind=="software"?"ms-settings:appsfeatures":"ms-settings:windowsupdate") {UseShellExecute=true}); }
        catch(Exception ex){UpdateStatus.Text=F("Windows ayarları açılamadı: {0}",DynamicTranslations.KnownText(ex.Message));}
    }
    private void OpenRelease_Click(object sender,RoutedEventArgs e) {
        if(githubRelease!=null){Page("settings");return;}
        if(!demo && Directory.Exists(ReleaseFeed.FeedDirectory))Process.Start(new ProcessStartInfo("explorer.exe") {UseShellExecute=true,ArgumentList={ReleaseFeed.FeedDirectory}});
    }
    private void Overview_Click(object sender,RoutedEventArgs e) { selectedReportId=null; Page("overview"); if(!demo) Refresh(); }
    private void History_Click(object sender,RoutedEventArgs e) { Page("history"); }
    private async void Settings_Click(object sender,RoutedEventArgs e) { Page("settings"); if(!demo) {SettingsStatus.Text=T("Sonuçlar okunuyor…");var status=await Task.Run(SchedulerService.GetStatus);if(!windowClosed)SettingsStatus.Text=DynamicTranslations.KnownText(status);} }
    private void History_Selected(object sender,SelectionChangedEventArgs e) { if(updatingHistory) return; if(HistoryList.SelectedItem is HistoryRow row) {selectedReportId=row.Report.Id; ShowReport(row.Report); RenderHealthProgress(null,false); HealthTabs.SelectedItem=HealthAttentionTab; Page("overview");} }
    private void Quick_Click(object sender,RoutedEventArgs e) => StartScan(false);
    private void Deep_Click(object sender,RoutedEventArgs e) => StartScan(true);
    private async void StartScan(bool deep)
    {
        if(demo) { MessageBox.Show(T("Bu yalnızca arayüz önizlemesidir; tarama başlatılmadı."),"SystemCompass"); return; }
        if(workerRunning || healthLaunching || installLaunching || updateInstalling) return;
        healthLaunching=true;QuickButton.IsEnabled=DeepButton.IsEnabled=false;
        try { selectedReportId=null; ShowHealthScanStart(); await Task.Run(()=>App.Elevate(deep?"--worker --deep":"--worker")); healthLaunchPendingUntil=DateTime.UtcNow.AddSeconds(30);Refresh(); }
        catch(Exception ex) { healthLaunchPendingUntil=DateTime.MinValue; HealthCurrentStage.Text=T("Tarama başlatılamadı."); HealthStageCounts.Text=DynamicTranslations.KnownText(ex.Message); MessageBox.Show(F("Kontrol başlatılamadı. Yönetici izni verilmediyse işlem yapılmaz.\n{0}",DynamicTranslations.KnownText(ex.Message)),"SystemCompass"); }
        finally {healthLaunching=false;RefreshResultActions();}
    }
    private void Folder_Click(object sender,RoutedEventArgs e)
    {
        if(demo) return;
        var path=shownReport?.ReportDirectory ?? LocalStore.ReportsRoot;
        if(Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe") {UseShellExecute=true,ArgumentList={path}});
    }
    private void SaveSettings()
    {
        if(demo) return;
        settings=LocalStore.LoadSettings();
        settings.ScheduleHour=Math.Max(0,HourPicker.SelectedIndex); settings.DeepScanIntervalDays=IntervalPicker.SelectedIndex==1?14:IntervalPicker.SelectedIndex==2?30:7;
        settings.AutoRepair=AutoRepairCheck.IsChecked==true; settings.ShowReportAfterScheduledScan=ShowReportCheck.IsChecked==true;
        LocalStore.SaveSettings(settings);
    }
    private void SaveSettings_Click(object sender,RoutedEventArgs e) { if(demo)return; SaveSettings(); SettingsStatus.Text=T("Tercihler kaydedildi. Zamanlama saatini göreve uygulamak için 'Kaydet ve planı etkinleştir' düğmesini kullan."); }
    private void EnableSchedule_Click(object sender,RoutedEventArgs e) { if(demo)return; SaveSettings(); try{App.Elevate("--enable-schedule"); SettingsStatus.Text=T("Yönetici izninden sonra zamanlama sonucu ayrı pencerede gösterilecek.");}catch(Exception ex){SettingsStatus.Text=DynamicTranslations.KnownText(ex.Message);} }
    private void DisableSchedule_Click(object sender,RoutedEventArgs e) { if(demo)return; try{App.Elevate("--disable-schedule");SettingsStatus.Text=T("Yönetici izninden sonra plan durdurulacak. Devam eden tarama kesilmez.");}catch(Exception ex){SettingsStatus.Text=DynamicTranslations.KnownText(ex.Message);} }
    private static ScanReport DemoReport() => new() {IsDemo=true,DeepScan=true,Summary="Dikkat isteyen 2 bulgu var",Findings=new() {
        new(){Category="Windows",Title="Sistem dosyası bütünlüğü",Status="Healthy",Detail="Örnek sonuç: Korunan Windows dosyalarında bütünlük ihlali bulunmadı."},
        new(){Category="Disk",Title="SSD ve dosya sistemi",Status="Healthy",Detail="Örnek sonuç: Dosya sistemi kontrolü tamamlandı. Fiziksel disk testi yerine geçmez."},
        new(){Category="Olay kayıtları",Title="Beklenmeyen kapanmalar",Status="Warning",Detail="Örnek sonuç: 7 günde 3 kayıt. Güç kesintisi, zorla kapatma veya çökme olabilir; tek başına anakart arızası değildir."},
        new(){Category="Uygulamalar",Title="Başlatılamayan güncelleme hizmeti",Status="Warning",Detail="Örnek sonuç: Bir hizmette tekrar eden başlangıç hatası. Otomatik onarım uygulanmadı."},
        new(){Category="Bellek",Title="RAM donanım testi",Status="Unknown",Detail="Bu kontrol yapılmadı. Windows Bellek Tanılama yeniden başlatma gerektirir."}
    }};
}
public sealed class FindingRow(Finding finding)
{
    public Finding Finding=>finding;
    public string Category=>T(finding.Category); public string Title=>DynamicTranslations.KnownText(finding.Title); public string Detail=>DynamicTranslations.KnownText(finding.Detail);
    public string Label=>T(finding.Status switch {"Healthy"=>"Sorun yok","Warning"=>"İncelenmeli","Critical"=>"Sorun bulundu","Repaired"=>"Onarıldı","Info"=>"Bilgi",_=>"Doğrulanmadı"});
    public Brush Color=>(Brush)new BrushConverter().ConvertFromString(finding.Status switch {"Healthy" or "Repaired"=>"#DDEFE5","Critical"=>"#F9DDDB","Warning"=>"#FFF0CD",_=>"#E9EEF0"})!;
}
public sealed class HistoryRow(ScanReport report)
{
    public ScanReport Report=>report;
    public string DateLabel=>report.StartedUtc.ToLocalTime().ToString("g",System.Globalization.CultureInfo.CurrentCulture)+" · "+T(report.DeepScan?"Kapsamlı":"Hafif");
    public string Summary=>DynamicTranslations.KnownText(report.Summary);
    public string Counts=>F("{0} dikkat bulgusu · {1} doğrulanamayan kontrol",report.Findings.Count(f=>f.Status is "Critical" or "Warning"),report.Findings.Count(f=>f.Status=="Unknown"));
}





