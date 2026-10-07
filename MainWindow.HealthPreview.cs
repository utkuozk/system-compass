using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;

public partial class MainWindow
{
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;foreach(var nested in VisualDescendants<T>(child))yield return nested;}
    }
    private static ScanReport HealthFixture()
    {
        var report=new ScanReport {IsDemo=true,AppVersion="preview",DeepScan=true,Summary="15 ciddi bulgu; 15 uyarı; 15 belirsiz kontrol.",StartedUtc=DateTimeOffset.UtcNow.AddMinutes(-9),FinishedUtc=DateTimeOffset.UtcNow.AddMinutes(-2)};
        for(int i=0;i<60;i++)report.Findings.Add(new Finding {
            Category=(i%5) switch {0=>"Windows",1=>"Disk",2=>"Bellek",3=>"Güç",_=>"Olay kayıtları"},
            Title=DynamicTranslations.KnownText((i%5) switch {0=>"Windows sistem dosyaları",1=>"C: dosya sistemi",2=>"RAM donanım testi",3=>"Güç kaynağı",_=>"Beklenmeyen kapanmalar"})+" / "+(i+1),
            Status=(i%4) switch {0=>"Warning",1=>"Critical",2=>"Unknown",_=>"Healthy"},
            Detail=string.Join("\n",Enumerable.Range(1,28).Select(n=>"[SAMPLE OUTPUT "+n+"] Synthetic diagnostic record. Full source output stays available in this detail pane; its final line is END-OF-DETAIL."))
        });
        var tracker=new ScanTracker(true,false);
        tracker.Attach(report);tracker.Start("system-data","Donanım, güç ve son yedi günün olay kayıtları kontrol ediliyor…");tracker.Finish("system-data","Warning","Donanım, disk, güç ve olay kayıtları birlikte toplandı. Tek tek kontrollerin sonuçları aşağıdaki bulgulardadır.");
        tracker.Start("dism","Windows bileşen deposu ayrıntılı taranıyor…");tracker.Finish("dism","Healthy","DISM yeniden taradı; bileşen deposu bütünlüğü doğrulandı.");
        tracker.Start("sfc","Windows sistem dosyaları doğrulanıyor…");
        report.Steps=tracker.Snapshot().Steps;
        return report;
    }
    internal static void RenderHealthUiPreview(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string previousLanguage=L10n.Language;
        try {
            foreach(var language in new[]{"tr","en"}) {
                SetLanguage(language);
                var window=new MainWindow(true);
                try {
                    var report=HealthFixture();window.ShowReport(report);window.HealthTabs.SelectedItem=window.HealthAttentionTab;
                    foreach(var size in new[]{(1240,800),(1000,700)})RenderPreview((FrameworkElement)window.Content,size.Item1,size.Item2,Path.Combine(outputDirectory,$"health-report-{language}-{size.Item1}x{size.Item2}.png"));
                    var progress=new ScanProgress {ScanId="synthetic-live",DeepScan=true,Running=true,StartedUtc=DateTimeOffset.UtcNow.AddMinutes(-3),Steps=report.Steps,PartialFindings=report.Findings.Take(8).ToList()};
                    window.RenderHealthProgress(progress,true);window.HealthTabs.SelectedItem=window.HealthProgressTab;
                    foreach(var size in new[]{(1240,800),(1000,700)})RenderPreview((FrameworkElement)window.Content,size.Item1,size.Item2,Path.Combine(outputDirectory,$"health-progress-{language}-{size.Item1}x{size.Item2}.png"));
                } finally {window.Close();}
            }
        } finally {SetLanguage(previousLanguage);}
    }
    internal static void CheckHealthUi(Action<bool,string> check)
    {
        var originalLanguage=L10n.Language;
        try {
            SetLanguage("en");
            var window=new MainWindow(true);
            try {
                var report=HealthFixture();window.ShowReport(report);window.HealthTabs.SelectedItem=window.HealthAttentionTab;
                var content=(FrameworkElement)window.Content;content.Measure(new Size(1000,700));content.Arrange(new Rect(0,0,1000,700));content.UpdateLayout();content.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);content.UpdateLayout();
                check(window.OverviewPanel.Parent is Grid && window.OverviewPanel.Parent is not ScrollViewer,"Health overview has bounded layout outside page scrolling");
                check(window.FindingsList.ActualHeight>75 && window.FindingsList.ActualHeight<450,"Health findings table remains bounded at 1000x700");
                var quickBounds=window.QuickButton.TransformToAncestor(content).TransformBounds(new Rect(window.QuickButton.RenderSize));
                var deepBounds=window.DeepButton.TransformToAncestor(content).TransformBounds(new Rect(window.DeepButton.RenderSize));
                check(window.QuickButton.Visibility==Visibility.Visible && window.DeepButton.Visibility==Visibility.Visible && quickBounds.Height>0 && new Rect(0,0,1000,700).Contains(quickBounds) && new Rect(0,0,1000,700).Contains(deepBounds),"Health scan actions stay visible in compact layout");
                check(window.FindingsList.Columns.All(c=>c.ActualWidth>50) && window.FindingsList.Columns.Sum(c=>c.ActualWidth)>window.FindingsList.ActualWidth*0.8,"Health table columns have usable rendered widths");
                var renderedRow=(DataGridRow?)window.FindingsList.ItemContainerGenerator.ContainerFromIndex(0);
                check(renderedRow!=null && VisualDescendants<DataGridCell>(renderedRow).Any(c=>c.ActualWidth>50 && VisualDescendants<TextBlock>(c).Any(t=>!string.IsNullOrEmpty(t.Text))),"Health table realizes a visible readable finding row");
                check(window.FindingsList.Items.Count==45,"Needs attention includes critical, warning and unverified findings");
                check(window.HealthDetailText.Text.Contains("END-OF-DETAIL"),"Full long diagnostic details remain accessible");
                check(window.HealthWindowsTab.Header?.ToString()=="Windows" && window.HealthProgressTab.Header?.ToString()=="Scan progress" && window.HealthHardwareTab.Header?.ToString()=="Hardware / power","Health navigation translated into English");
                foreach(var tab in new[]{window.HealthWindowsTab,window.HealthStorageTab,window.HealthHardwareTab,window.HealthOtherTab}) {
                    window.HealthTabs.SelectedItem=tab;check(window.FindingsList.Items.Count>0,"Health group exposes its findings: "+tab.Header);
                }
                window.HealthTabs.SelectedItem=window.HealthAttentionTab;
                string fullDetail=window.HealthDetailText.Text;window.HealthDetailText.Select(5,10);window.RenderHealthResults();
                check(window.HealthDetailText.SelectionStart==5 && window.HealthDetailText.Text==fullDetail,"Unchanged health refresh preserves selected detail content and caret");
                var progress=new ScanProgress {ScanId="synthetic-live",Running=true,StartedUtc=DateTimeOffset.UtcNow.AddMinutes(-3),Steps=report.Steps,PartialFindings=report.Findings.Take(8).ToList()};
                window.RenderHealthProgress(progress,true);content.UpdateLayout();content.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);content.UpdateLayout();
                check(window.HealthTabs.SelectedItem==window.HealthProgressTab && window.HealthCurrentStage.Text.Contains("Windows system files (SFC)"),"Live scan opens progress and identifies actual running stage");
                check(window.HealthElapsed.Text.StartsWith("Elapsed:") && window.HealthStageCounts.Text.Contains("pending"),"Live elapsed and ordered counts translated without invented percentages");
                window.HealthTabs.SelectedItem=window.HealthWindowsTab;
                check(window.HealthReportContext.Text.StartsWith("LIVE") && window.FindingsList.Items.Count==2,"Live partial findings clearly distinguished from prior report");
                window.RenderHealthProgress(progress,false);window.HealthTabs.SelectedItem=window.HealthProgressTab;
                check(window.HealthCurrentStage.Text.StartsWith("Scan interrupted") && window.HealthStepsList.Items.Cast<HealthStepRow>().Any(s=>s.Status=="Unverified"),"Stopped scan never rendered as successful completion");
                window.HealthTabs.SelectedItem=window.HealthWindowsTab;
                check(window.HealthReportContext.Text.StartsWith("INTERRUPTED SCAN") && window.FindingsList.Items.Count==2,"Interrupted scan retains available partial findings");
                window.selectedReportId=report.Id;window.ShowReport(report);window.RenderHealthProgress(null,false);window.HealthTabs.SelectedItem=window.HealthStorageTab;
                check(window.HealthReportContext.Text.StartsWith("COMPLETED REPORT") && window.FindingsList.Items.Count==12,"Historical report uses grouped report findings");
                check(new HealthStepRow(new(){Id="sfc",Title="Windows sistem dosyaları (SFC)",Status="Skipped",Detail="Hızlı taramada çalıştırılmaz; ayrıntılı tarama gerekir."}).Detail.StartsWith("Not run in a quick scan"),"Skipped scan explanation translated");
            } finally {window.Close();}
        } finally {SetLanguage(originalLanguage);}
    }
}



