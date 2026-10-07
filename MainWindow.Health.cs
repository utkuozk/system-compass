using System.Windows;
using System.Windows.Controls;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;

public partial class MainWindow
{
    private ScanProgress? healthProgress;
    private bool healthInitialized;
    private string healthScanId="";
    private bool healthLaunching;
    private DateTime healthLaunchPendingUntil=DateTime.MinValue;
    private string healthFindingsSignature="",healthStepsSignature="";
    private bool healthPolling;
    private readonly System.Windows.Threading.DispatcherTimer healthPoll=new(){Interval=TimeSpan.FromSeconds(2)};
    private readonly System.Windows.Threading.DispatcherTimer healthClock=new(){Interval=TimeSpan.FromSeconds(1)};
    private void InitializeHealthDashboard()
    {
        HealthTranslations.Register();
        HealthProgressTab.Header=T("Tarama durumu"); HealthAttentionTab.Header=T("Dikkat isteyenler");
        HealthWindowsTab.Header=T("Windows"); HealthStorageTab.Header=T("Depolama");
        HealthHardwareTab.Header=T("Donanım / güç"); HealthOtherTab.Header=T("Olaylar / diğer");
        HealthCategoryColumn.Header=T("Kategori"); HealthTitleColumn.Header=T("Kontrol"); HealthStatusColumn.Header=T("Sonuç");
        HealthStepColumn.Header=T("Tarama adımı"); HealthStepStateColumn.Header=T("Durum"); HealthStepOutcomeColumn.Header=T("Sonuç");
        HealthIssueLabel.Text=T("DİKKAT İSTEYEN");
        HeroEyebrow.Text=T("HENÜZ RAPOR YOK");
        HeroDetail.Text=T("Bulgular sekmelerde gruplanır. Bir satır seçerek ayrıntıların tamamını okuyun.");
        healthInitialized=true;
        healthClock.Tick+=(_,_)=>RenderHealthElapsed();
        healthPoll.Tick+=async(_,_)=>{
            if(healthPolling || windowClosed)return;
            healthPolling=true;
            try {
                var snapshot=await Task.Run(()=>{var state=LocalStore.Read<ScanProgress>(LocalStore.ProgressPath);return(state,active:state?.Running==true && IsScanAlive(state));});
                if(windowClosed)return;
                bool wasRunning=workerRunning;
                workerRunning=snapshot.active;
                RenderHealthProgress(snapshot.state,snapshot.active);RefreshResultActions();
                if(wasRunning && !snapshot.active)Refresh();
            } catch(Exception ex){HealthCurrentStage.Text=F("Kontrol tamamlanamadı: {0}",ex.Message);}
            finally {healthPolling=false;}
        };
        if(!demo)healthPoll.Start();
        Closed+=(_,_)=>{healthClock.Stop();healthPoll.Stop();};
        RenderHealthResults(); RenderHealthProgress(null,false);
    }
    private void HealthTab_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!healthInitialized || e.Source!=HealthTabs)return;
        bool progress=HealthTabs.SelectedItem==HealthProgressTab;
        HealthProgressPanel.Visibility=progress?Visibility.Visible:Visibility.Collapsed;
        HealthResultsPanel.Visibility=progress?Visibility.Collapsed:Visibility.Visible;
        if(progress) { if(HealthStepsList.SelectedItem is HealthStepRow stage)SetHealthDetail(stage.Title,stage.Detail,T("Adım ayrıntıları"));else SetHealthDetail(T("Tarama durumu"),T("Tarama başladığında adımlar ve gerçek sonuçları burada görünür."),""); }
        else RenderHealthResults();
    }
    internal static string HealthGroup(Finding finding)
    {
        var category=finding.Category;
        if(category is "Windows" or "Onarım" or "Güncellemeler" or "Antivirüs" or "Güvenlik")return "windows";
        if(category.Contains("Disk",StringComparison.OrdinalIgnoreCase) || category.Contains("Depolama",StringComparison.OrdinalIgnoreCase))return "storage";
        if(category is "Bellek" or "Aygıtlar" or "Donanım" or "Güç" or "Pil" or "İşlemci" || category.Contains("Sıcaklık",StringComparison.OrdinalIgnoreCase))return "hardware";
        return "other";
    }
    private void RenderHealthResults()
    {
        if(!healthInitialized)return;
        bool interrupted=selectedReportId==null && healthProgress?.Interrupted==true;
        bool live=selectedReportId==null && healthProgress?.Running==true;
        var findings=live || interrupted?healthProgress!.PartialFindings:shownReport?.Findings??new();
        HealthReportContext.Text=live?F("CANLI • {0} sonuç alındı; kalan kontroller henüz tamamlanmadı.",findings.Count):shownReport==null?T("Henüz tamamlanmış rapor yok."):F(shownReport.Interrupted?"KESİLMİŞ RAPOR • {0:g} • {1} bulgu":!shownReport.FinishedUtc.HasValue?"KISMİ RAPOR • {0:g} • {1} bulgu":"TAMAMLANMIŞ RAPOR • {0:g} • {1} bulgu",shownReport.StartedUtc.ToLocalTime(),findings.Count);
        if(live && shownReport!=null)HealthReportContext.Text+=" "+F("Üstteki özet önceki rapora aittir: {0:g}.",shownReport.StartedUtc.ToLocalTime());
        if(interrupted)HealthReportContext.Text=F("KESİLEN TARAMA • {0} kısmi bulgu; tamamlanmayan kontroller doğrulanamadı.",findings.Count);
        else if(healthProgress?.Running==true && selectedReportId!=null)HealthReportContext.Text+=" "+T("Yeni tarama sürüyor; bu geçmiş rapor değişmez.");
        if(HealthTabs.SelectedItem==HealthProgressTab)return;
        string group=HealthTabs.SelectedItem==HealthWindowsTab?"windows":HealthTabs.SelectedItem==HealthStorageTab?"storage":HealthTabs.SelectedItem==HealthHardwareTab?"hardware":HealthTabs.SelectedItem==HealthOtherTab?"other":"attention";
        var selected=(FindingsList.SelectedItem as FindingRow)?.Finding;
        var rows=findings.Where(f=>group=="attention"?f.Status is "Critical" or "Warning" or "Unknown":HealthGroup(f)==group).Select(f=>new FindingRow(f)).ToList();
        string signature=group+"|"+string.Join("|",rows.Select(r=>r.Finding.Category+r.Finding.Title+r.Finding.Status+r.Finding.Detail));
        if(signature==healthFindingsSignature){if(FindingsList.SelectedItem is FindingRow existing)SetHealthDetail(existing.Title,existing.Detail,existing.Label);return;}
        healthFindingsSignature=signature;
        FindingsList.ItemsSource=rows;
        EmptyText.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;
        EmptyText.Text=T(live?"Bu grupta henüz sonuç alınmadı.":shownReport==null?"Bir kontrol başlatın veya geçmiş raporlardan birini açın.":"Bu grupta bulgu yok.");
        var next=rows.FirstOrDefault(r=>selected!=null && r.Finding.Title==selected.Title && r.Finding.Category==selected.Category)??rows.FirstOrDefault();
        FindingsList.SelectedItem=next;
        if(next==null)SetHealthDetail(T("Bulgu ayrıntıları"),T("Bir satır seçerek ayrıntıların tamamını okuyun. Doğrulanamayan kontroller başarı sayılmaz."),"");
    }
    private void HealthFinding_Selected(object sender,SelectionChangedEventArgs e)
    {
        if(FindingsList.SelectedItem is FindingRow row)SetHealthDetail(row.Title,row.Detail,row.Label);
    }
    private void HealthStep_Selected(object sender,SelectionChangedEventArgs e)
    {
        if(HealthTabs.SelectedItem==HealthProgressTab && HealthStepsList.SelectedItem is HealthStepRow row)SetHealthDetail(row.Title,row.Detail,row.Status);
    }
    private void SetHealthDetail(string title,string detail,string label) {HealthDetailTitle.Text=title;if(HealthDetailText.Text!=detail){HealthDetailText.Text=detail;HealthDetailText.ScrollToHome();}HealthDetailLabel.Text=label;}
    private void ShowHealthScanStart()
    {
        healthLaunchPendingUntil=DateTime.UtcNow.AddSeconds(30);
        QuickButton.IsEnabled=DeepButton.IsEnabled=false;
        Page("overview");HealthTabs.SelectedItem=HealthProgressTab;
        HealthCurrentStage.Text=T("Yönetici izni ve kontrol başlangıcı bekleniyor…");
        HealthStageCounts.Text=T("Tarama başladığında adımlar ve gerçek sonuçları burada görünür.");
        HealthElapsed.Text="";
        SetHealthDetail(T("Tarama başlangıcı"),T("Diğer sayfaları kullanabilirsiniz. Sonuçlar tarama ilerledikçe burada güncellenir."),"");
    }
    private void RenderHealthProgress(ScanProgress? progress,bool active)
    {
        if(!healthInitialized)return;
        if(progress?.Running==true && !active)ScanTracker.NormalizeStopped(progress);
        healthProgress=progress;
        if(active)healthLaunchPendingUntil=DateTime.MinValue;
        if(healthLaunching || DateTime.UtcNow<healthLaunchPendingUntil)return;
        if(active && selectedReportId==null && progress!=null && healthScanId!=progress.ScanId) {healthScanId=progress.ScanId;HealthTabs.SelectedItem=HealthProgressTab;}
        var steps=(selectedReportId==null?progress?.Steps:null)??shownReport?.Steps??new();
        string selectedId=(HealthStepsList.SelectedItem as HealthStepRow)?.Step.Id??"";
        var rows=steps.OrderBy(s=>s.Order).Select(s=>new HealthStepRow(s)).ToList();
        string signature=string.Join("|",rows.Select(r=>r.Step.Id+r.Step.Status+r.Step.Outcome+r.Step.Detail));
        bool changed=signature!=healthStepsSignature;
        if(changed){healthStepsSignature=signature;HealthStepsList.ItemsSource=rows;}
        var current=rows.FirstOrDefault(r=>r.Step.Status=="Running");
        HealthCurrentStage.Text=current!=null && active && selectedReportId==null?F("Şu an: {0}",current.Title):active && selectedReportId==null?DynamicTranslations.KnownText(progress?.Message??""):rows.Count==0?T("Henüz tarama adımı yok."):T(progress?.FinishedUtc!=null && selectedReportId==null?"Tarama tamamlandı":"Raporun tarama adımları");
        if(selectedReportId==null?progress?.Interrupted==true:shownReport?.Interrupted==true)HealthCurrentStage.Text=T("Tarama kesildi; tamamlanmayan adımlar doğrulanamadı.");
        HealthStageCounts.Text=rows.Count==0?T("Eski raporlarda adım zaman çizelgesi bulunmayabilir."):F("{0} tamamlandı · {1} atlandı · {2} doğrulanamadı · {3} bekliyor",steps.Count(s=>s.Status=="Completed"),steps.Count(s=>s.Status=="Skipped"),steps.Count(s=>s.Status=="Unknown"),steps.Count(s=>s.Status=="Pending"));
        if(changed)HealthStepsList.SelectedItem=rows.FirstOrDefault(r=>r.Step.Id==selectedId)??current??rows.FirstOrDefault();
        if(HealthTabs.SelectedItem==HealthProgressTab && HealthStepsList.SelectedItem is HealthStepRow selected)SetHealthDetail(selected.Title,selected.Detail,selected.Status);
        if(active)healthClock.Start();else healthClock.Stop();RenderHealthElapsed();RenderHealthResults();
    }
    private void RenderHealthElapsed()
    {
        var start=selectedReportId==null?healthProgress?.StartedUtc:shownReport?.StartedUtc;
        var end=selectedReportId==null?healthProgress?.FinishedUtc:shownReport?.FinishedUtc;
        if(start==null) {HealthElapsed.Text="";return;}
        var elapsed=(end??DateTimeOffset.UtcNow)-start.Value;
        if(elapsed<TimeSpan.Zero)elapsed=TimeSpan.Zero;
        HealthElapsed.Text=F("Geçen süre: {0}",elapsed.TotalHours>=1?elapsed.ToString(@"h\:mm\:ss"):elapsed.ToString(@"mm\:ss"));
    }
}
public sealed class HealthStepRow(ScanStep step)
{
    public ScanStep Step=>step;
    public int Order=>step.Order;
    public string Title=>HealthTranslations.StageTitle(step.Id,step.Title);
    public string Status=>T(step.Status switch {"Running"=>"Çalışıyor","Completed"=>"Tamamlandı","Skipped"=>"Atlandı","Unknown"=>"Doğrulanamadı",_=>"Bekliyor"});
    public string Outcome=>string.IsNullOrEmpty(step.Outcome)?"—":new FindingRow(new(){Status=step.Outcome}).Label;
    public string Detail=>string.IsNullOrWhiteSpace(step.Detail)?T(step.Status switch {"Pending"=>"Bu adım henüz başlamadı.","Running"=>"Bu adım sürüyor. Sonuç henüz doğrulanmadı.","Skipped"=>"Bu adım bu taramada uygulanmadı.",_=>"Bu adım için ek ayrıntı yok."}):HealthTranslations.Detail(step.Detail);
}
