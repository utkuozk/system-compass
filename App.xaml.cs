using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
namespace SistemPusulasi;

public partial class App : Application
{
    private Mutex? runningMarker;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DynamicTranslations.Register();
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_,args) => { UiDialogs.Show(args.Exception.Message,"Sistem Pusulası"); args.Handled=true; };
        try {
            if(e.Args.Length==1 && e.Args[0]=="--uninstall-schedule") {
                var result=SchedulerService.Remove();
                Shutdown(result is "Günlük tarama görevi kaldırıldı." or "Kurulu zamanlama görevi bulunamadı." or "Görev sahiplik doğrulamasından geçmedi; korunuyor."?0:1);return;
            }
            if(!e.Args.Contains("--self-test")) {
                runningMarker=new Mutex(false,@"Global\SystemCompassRunning");
                Exit+=(_,_)=>runningMarker.Dispose();
            }
            if(e.Args.Contains("--self-test")) { try { SelfTests.Run(); var concurrencyResults=await InventoryConcurrencyTests.Run(); File.AppendAllLines(Path.Combine(AppContext.BaseDirectory,"self-test-results.txt"),concurrencyResults); Shutdown(0); } catch(Exception testError) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test-results.txt"),testError.ToString()); Shutdown(1); } return; }
            if(e.Args.Contains("--demo")) { OpenWindow(true); return; }
            LocalStore.Initialize();
            var appSettings=LocalStore.LoadSettings();
            InstallerLanguage.ApplyPending(
                Path.Combine(AppContext.BaseDirectory,"installer-language.txt"),
                Path.Combine(LocalStore.Root,"installer-language-applied.txt"),
                appSettings,LocalStore.SaveSettings);
            L10n.SetLanguage(appSettings.Language);
            if(e.Args.Length==2 && e.Args[0]=="--antivirus" && e.Args[1] is "update" or "quick" or "full") {
                if(!IsAdmin()){Elevate("--antivirus "+e.Args[1]);Shutdown();return;}
                await RunAntivirusWorker(e.Args[1]); Shutdown();return;
            }
            if(e.Args.Length==2 && e.Args[0]=="--install-update") {
                await RunUpdateWorker(e.Args[1]); Shutdown();return;
            }
            if(e.Args.Contains("--install") || string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"SistemPusulasi-Kur",StringComparison.OrdinalIgnoreCase)) { InstallService.Install(); Shutdown(); return; }
            if(e.Args.Contains("--enable-schedule") || e.Args.Contains("--disable-schedule") || e.Args.Contains("--finish-install")) {
                if(!IsAdmin()) { Elevate(string.Join(" ",e.Args)); Shutdown(); return; }
                var disable=e.Args.Contains("--disable-schedule");
                string result=disable ? SchedulerService.Remove() : SchedulerService.Register(LocalStore.LoadSettings());
                File.WriteAllText(Path.Combine(LocalStore.Root,"schedule-result.txt"),result);
                UiDialogs.Show(result,"Sistem Pusulası",MessageBoxButton.OK,MessageBoxImage.Information);
                if(e.Args.Contains("--finish-install")) OpenWindow(false); else Shutdown();
                return;
            }
            if(e.Args.Contains("--worker") || e.Args.Contains("--scheduled") || e.Args.Contains("--repair")) {
                if(e.Args.Contains("--repair") && e.Args.Contains("--scheduled")) throw new ArgumentException("İstenen onarım zamanlanmış taramayla birlikte çalıştırılamaz.");
                if(!IsAdmin()) { if(!e.Args.Contains("--scheduled")) Elevate(string.Join(" ",e.Args)); Shutdown(); return; }
                bool scheduled=e.Args.Contains("--scheduled");
                bool manualRepair=e.Args.Contains("--repair");
                var settings=LocalStore.LoadSettings();
                bool deep=manualRepair || e.Args.Contains("--deep") || scheduled && LocalStore.DeepDue(settings,LocalStore.Reports());
                bool completed=await Task.Run(()=>RunWorker(deep,settings,manualRepair));
                if(completed && scheduled && settings.ShowReportAfterScheduledScan) Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--report") {UseShellExecute=true});
                Shutdown(); return;
            }
            OpenWindow(false);
        } catch(Exception ex) { UiDialogs.Show(ex.Message,"Sistem Pusulası — İşlem tamamlanamadı",MessageBoxButton.OK,MessageBoxImage.Warning); Shutdown(); }
    }
    private void OpenWindow(bool demo) { ShutdownMode=ShutdownMode.OnLastWindowClose; var window=new MainWindow(demo); MainWindow=window; window.InitializeLanguagePicker(); window.Show(); }
    internal static bool IsAdmin() { using var id=WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
    internal static Process? Elevate(string arguments) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!,arguments) {UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});
    private static bool RunWorker(bool deep,AppSettings settings,bool manualRepair=false)
    {
        string sid=WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        using var mutex=new Mutex(false,"Local\\SistemPusulasi-Scan-"+sid);
        bool acquired=false;
        ScanTracker? tracker=null;
        ScanReport? report=null;
        try {
            try { acquired=mutex.WaitOne(0); } catch(AbandonedMutexException) { acquired=true; }
            if(!acquired) return false;
            string dir=Path.Combine(LocalStore.ReportsRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(dir);
            tracker=new ScanTracker(deep,manualRepair,snapshot=>LocalStore.Write(LocalStore.ProgressPath,snapshot),Path.GetFileName(dir));
            void Progress(string message) => tracker.Message(message);
            Progress(manualRepair ? "Onarım için güncel Windows bütünlüğü yeniden denetleniyor…" : "Kontrol hazırlanıyor…");
            try { report=new DiagnosticsEngine().RunAsync(deep,settings,dir,Progress,manualRepair,tracker).GetAwaiter().GetResult(); }
            catch(Exception ex) { report=new ScanReport {DeepScan=deep,ReportDirectory=dir,FinishedUtc=DateTimeOffset.UtcNow,Summary="Kontrol tamamlanamadı",Findings=new() {new Finding {Category="Uygulama",Title="Kontrol hatası",Status="Unknown",Detail=ex.Message}}}; tracker.Attach(report); tracker.FailActive(ex.Message); }
            if(deep && !manualRepair) {
                foreach(var kind in new[]{"windows","drivers","software"}) {
                    tracker.Start("updates-"+kind,"Güncelleme envanteri kontrol ediliyor: "+kind);
                    try {
                        var inventory=UpdateInventory.ScanAsync(kind).GetAwaiter().GetResult();
                        LocalStore.Write(Path.Combine(LocalStore.Root,"updates-"+kind+".json"),inventory);
                        report.Findings.Add(new Finding {Category="Güncellemeler",Title=kind=="windows"?"Windows Update":kind=="drivers"?"Sürücü güncellemeleri":"Yazılım güncellemeleri",Status=inventory.Status.StartsWith("Unknown:")?"Unknown":"Info",Detail=inventory.Status.Replace("Unknown:","Doğrulanamadı:").Replace("Checked:","Kontrol edildi:")+" Ayrıntılar Güncelleme merkezi ekranında."});
                    } catch(Exception ex){report.Findings.Add(new Finding {Category="Güncellemeler",Title=kind,Status="Unknown",Detail=ex.Message});}
                    tracker.Finish("updates-"+kind);
                }
            }
            else tracker.SkipPending(new[]{"updates-windows","updates-drivers","updates-software"},manualRepair?"İstenen onarımın kapsamı dışında.":"Güncelleme envanteri ayrıntılı taramada kontrol edilir.");
            tracker.Start("antivirus","Antivirüs koruma durumu okunuyor…");
            try {var security=AntivirusService.ReadAsync().GetAwaiter().GetResult(); LocalStore.Write(Path.Combine(LocalStore.Root,"antivirus.json"),security);report.Findings.AddRange(security.Findings);}
            catch(Exception ex){report.Findings.Add(new Finding {Category="Antivirüs",Title="Koruma durumu",Status="Unknown",Detail=ex.Message});}
            tracker.Finish("antivirus");
            report.ReportDirectory=dir;
            report.AppVersion=ReleaseFeed.Current.ToString(3);
            report.FinishedUtc = DateTimeOffset.UtcNow;
            int critical=report.Findings.Count(f=>f.Status=="Critical"), warnings=report.Findings.Count(f=>f.Status=="Warning"), unknown=report.Findings.Count(f=>f.Status=="Unknown");
            report.Summary=$"{critical} ciddi bulgu; {warnings} uyarı; {unknown} belirsiz kontrol.";
            tracker.ResolveUnfinished("Önceki kontrolün tamamlandığı doğrulanamadı.","report");
            tracker.Start("report","Rapor kaydediliyor…");
            LocalStore.SaveReport(report);
            WriteReadableReport(report);
            tracker.Finish("report","Info","Tarama raporu ve okunabilir kayıt kaydedildi.");
            // Include the final execution timeline in the saved report as well as live progress.
            try { LocalStore.SaveReport(report); }
            catch { tracker.Start("report","Rapor kaydı tamamlanamadı."); throw; }
            tracker.Stop(report.Summary);
            return true;
        } catch(Exception ex) {
            tracker?.FailActive("İşlem tamamlanamadı: "+ex.Message);
            if(report!=null) {
                report.Findings.Add(new Finding {Category="Uygulama",Title="Rapor kaydı tamamlanamadı",Status="Unknown",Detail=ex.Message});
                report.FinishedUtc=DateTimeOffset.UtcNow; report.Summary="Kontrol raporu tamamlanamadı.";
            }
            tracker?.Stop("Kontrol tamamlanamadı: "+ex.Message,true);
            if(report!=null) { try { LocalStore.SaveReport(report); } catch { } }
            throw;
        }
        finally { if(acquired) mutex.ReleaseMutex(); }
    }
    private static void WriteReadableReport(ScanReport report)
    {
        var b=new StringBuilder();
        b.AppendLine("SİSTEM PUSULASI — KONTROL RAPORU"); b.AppendLine(report.StartedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
        b.AppendLine(report.Summary); b.AppendLine("Bu rapor bir fiziksel donanım testi değildir.");
        foreach(var f in report.Findings) { b.AppendLine(); b.AppendLine($"[{f.Status}] {f.Category} / {f.Title}"); b.AppendLine(f.Detail); }
        File.WriteAllText(Path.Combine(report.ReportDirectory,"RAPOR.txt"),b.ToString(),Encoding.UTF8);
    }
    private static async Task RunAntivirusWorker(string action)
    {
        FileStream gate;
        try {gate=new FileStream(Path.Combine(LocalStore.Root,"antivirus-action.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){UiDialogs.Show("Başka bir antivirüs işlemi sürüyor.","Sistem Pusulası");return;}
        using(gate) {
            var path=Path.Combine(LocalStore.Root,"antivirus-progress.json");
            LocalStore.Write(path,new ScanProgress {Running=true,ProcessId=Environment.ProcessId,Message=action=="update"?"Tehdit verileri güncelleniyor…":"Tehdit verileri güncelleniyor ve virüs taraması çalışıyor…"});
            string result="İşlem tamamlanamadı; koruma durumunu yenileyin.";
            try {
                var report=await AntivirusService.RunAsync(action);
                LocalStore.Write(Path.Combine(LocalStore.Root,"antivirus.json"),report);
                LocalStore.Write(Path.Combine(LocalStore.Root,"AntivirusHistory",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json"),report);
                result=report.Summary;
            } catch(Exception ex){result="Antivirüs işlemi tamamlanamadı: "+ex.Message;}
            finally {LocalStore.Write(path,new ScanProgress {Running=false,ProcessId=Environment.ProcessId,Message=result});}
        }
    }
}
