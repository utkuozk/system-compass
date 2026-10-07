using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;
public partial class MainWindow
{
    private bool installLaunching,updateInstalling,readingInstallProgress,installAwaiting;
    private bool installQueueRunning;
    private readonly ObservableCollection<UpdateQueueItem> installQueue=new();
    private UpdateQueueItem? currentInstall;
    private UpdateProgressWindow? installProgressWindow;
    private int completedInstalls;
    private readonly System.Windows.Threading.DispatcherTimer actionTimer=new(){Interval=TimeSpan.FromSeconds(2)};
    private DateTime repairPendingUntil=DateTime.MinValue;
    private void InitializeResultActions()
    {
        UpdateUiTranslations.Register();
        SelectAllUpdates.Content=T("Görünen uygun kayıtları seç");
        ShowInstallProgressButton.Content=T("Kurulum durumunu göster");
        Closed+=(_,_)=>installProgressWindow?.CloseMonitor();
        actionTimer.Tick+=(_,_)=>{if(updateInstalling || installLaunching)_=ReadInstallProgress();};
        if(!demo)actionTimer.Start();
        RepairActionTitle.Text=T("Sonuçlara göre işlem yap");
        RepairButton.Content=T("Windows bozulmasını onar");
        InstallSelectedButton.Content=T("Seçilileri güncelle");
        RenderRepairAction();RenderInstallAction();
    }
    private void RenderRepairAction()
    {
        bool eligible=RepairPolicy.HasActionableFinding(shownReport);
        RepairButton.IsEnabled=!demo && eligible && !workerRunning && !updateInstalling && !installLaunching && !installQueueRunning && DateTime.UtcNow>=repairPendingUntil;
        RepairActionDetail.Text=T(eligible?"Bu raporda Windows dosya bozulması bulundu. Onar düğmesi yeni bir kapsamlı doğrulama yapar; bozulma sürüyorsa DISM/SFC ile onarır. Yönetici izni gerekir; otomatik yeniden başlatma yapılmaz.":shownReport==null?"Önce bilgisayar durumunu kontrol edin. Sonuçlara uygun işlemler burada gösterilir.":"Bu raporda düğmeyle onarılabilecek doğrulanmış Windows bozulması yok. Donanım uyarıları, geçmiş olaylar ve belirsiz kontroller otomatik onarılmış sayılmaz. Eksik Windows kontrolleri için kapsamlı taramayı kullanın.");
    }
    private async void Repair_Click(object sender,RoutedEventArgs e)
    {
        if(!RepairButton.IsEnabled || demo)return;
        if(MessageBox.Show(T("Windows dosyaları yeniden denetlenecek; doğrulanan bozulma onarılacak. Bu işlem uzun sürebilir ve yönetici izni ister. Otomatik onarım tercihiniz değişmez. Bilgisayar otomatik yeniden başlatılmaz. Başlatılsın mı?"),"System Compass",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        repairPendingUntil=DateTime.UtcNow.AddMinutes(2);RenderRepairAction();
        try {
            selectedReportId=null;
            ProgressPanel.Visibility=Visibility.Visible;ProgressText.Text=T("Yönetici izni ve kontrol başlangıcı bekleniyor…");
            await Task.Run(()=>App.Elevate("--repair"));
        } catch(Exception ex){repairPendingUntil=DateTime.MinValue;MessageBox.Show(ex.Message,"System Compass");RenderRepairAction();}
    }
    private void RenderInstallAction()
    {
        if(InstallSelectedButton==null)return;
        var selected=SnapshotUpdateQueue(updateRows);
        bool busy=installLaunching || updateInstalling || installQueueRunning || workerRunning;
        InstallSelectedButton.Content=F("Seçilileri güncelle ({0})",selected.Length);
        InstallSelectedButton.IsEnabled=!demo && selected.Length>0 && !busy;
        UpdateCheckedCount.Text=F("{0} kayıt seçili",selected.Length);
        InstallEligibilityText.Text=busy?T("Başka bir kurulum veya bakım sürüyor. Menüler kullanılabilir; yeni kurulum için tamamlanmasını bekleyin."):T("Kurulum için kutuları işaretleyin. Seçimler arama sırasında korunur; yalnızca bu sayfadaki seçili kayıtlar sırayla kurulur. Otomatik yeniden başlatma yapılmaz.");
        ShowInstallProgressButton.Visibility=installQueue.Count>0 || updateInstalling?Visibility.Visible:Visibility.Collapsed;
        QuickButton.IsEnabled=DeepButton.IsEnabled=!busy;
    }
    private void ShowInstallProgress_Click(object sender,RoutedEventArgs e)=>ShowInstallProgress();
    private void ShowInstallProgress()
    {
        if(installQueue.Count==0) {MessageBox.Show(InstallResultText.Text,"System Compass");return;}
        installProgressWindow??=new UpdateProgressWindow(installQueue){Owner=this};
        installProgressWindow.Show();installProgressWindow.Activate();
    }
    private async void InstallSelected_Click(object sender,RoutedEventArgs e)
    {
        if(demo || !InstallSelectedButton.IsEnabled)return;
        var entries=SnapshotUpdateQueue(updateRows);
        if(entries.Length==0)return;
        var names=string.Join("\n",entries.Take(10).Select(x=>F("{0} · {1} → {2}",x.Name,x.InstalledVersion,x.AvailableVersion)));
        if(entries.Length>10)names+="\n"+F("… ve {0} kayıt daha",entries.Length-10);
        var message=F("{0} güncelleme sırayla kurulacak:\n{1}\n\nHer kayıt yeniden doğrulanıp indirilecek ve kurulacak. Gereken lisans koşullarını kabul ederek devam edersiniz. Sürücü kurulumu ilgili aygıtı kısa süre kesintiye uğratabilir. Otomatik yeniden başlatma yapılmaz. Devam edilsin mi?",entries.Length,names);
        if(MessageBox.Show(message,"System Compass",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        installQueueRunning=true;completedInstalls=0;installQueue.Clear();
        foreach(var entry in entries)installQueue.Add(new(entry));
        ShowInstallProgress();InstallResultText.Text="";RenderInstallAction();RenderRepairAction();
        bool stopQueue=false;
        string stoppedDetail=T("Önceki işlemin sonucunu doğrulayın; kalan kayıtlar başlatılmadı.");
        try {
            foreach(var item in installQueue) {
                if(windowClosed || stopQueue)break;
                currentInstall=item;installLaunching=true;installAwaiting=false;
                item.State=T("Başlatılıyor");item.Detail=T("Kurulum başlangıcı ve gerekirse yönetici izni bekleniyor…");
                installProgressWindow?.Update(completedInstalls,installQueue.Count,item.Name,item.Detail,true);
                string request=App.UpdateRequestPath(item.Id);
                try {
                    await Task.Run(()=>LocalStore.Write(request,item.Entry));
                    using var process=await Task.Run(()=>item.Entry.Kind=="software"?Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden,ArgumentList={"--install-update",item.Id}}):App.Elevate("--install-update "+item.Id));
                    if(process==null)throw new InvalidOperationException(T("Kurulum başlatılamadı."));
                    updateInstalling=true;installAwaiting=true;installLaunching=false;
                    item.State=T("Çalışıyor");item.Detail=T("Kurulum koşulları denetleniyor…");
                    InstallProgressBar.Visibility=Visibility.Visible;RenderInstallAction();
                    await process.WaitForExitAsync();
                    installAwaiting=false;
                    var result=await Task.Run(()=>LocalStore.Read<UpdateInstallResult>(Path.Combine(LocalStore.Root,"UpdateHistory",item.Id+".json")));
                    item.State=InstallStateLabel(result);
                    item.Detail=InstallDetail(item.Entry,result);
                    // An unknown outcome or a cross-process lock conflict must not start another install.
                    stopQueue=ShouldStopUpdateQueue(result);
                    if(result?.RebootRequired==true)stoppedDetail=T("Yeniden başlatma gerekiyor; kalan kayıtlar başlatılmadı. Bilgisayar otomatik yeniden başlatılmadı.");
                } catch(Exception ex) {item.State=T("Başlatılmadı");item.Detail=F("İşlem başlatılmadı: {0}",ex.Message);stopQueue=true;}
                finally {installLaunching=false;installAwaiting=false;try{File.Delete(request);}catch{}completedInstalls++;}
                if(windowClosed)return;
                InstallResultText.Text=F("{0}: {1}\n{2}",item.Name,item.State,item.Detail);
                installProgressWindow?.Update(completedInstalls,installQueue.Count,item.Name,item.Detail,true);
            }
            if(stopQueue)foreach(var item in installQueue.Skip(completedInstalls)){item.State=T("Başlatılmadı");item.Detail=stoppedDetail;}
        } finally {
            currentInstall=null;installQueueRunning=false;installLaunching=false;installAwaiting=false;updateInstalling=false;
            if(!windowClosed) {
                InstallProgressBar.Visibility=Visibility.Collapsed;
                InstallResultText.Text=F("{0} / {1} işlem tamamlandı",completedInstalls,installQueue.Count)+"\n"+T("Kurulumdan sonra listeyi güncellemeleri denetle düğmesiyle yeniden kontrol edin.");
                installProgressWindow?.Update(completedInstalls,installQueue.Count,"",T(stopQueue?"Kuyruk durdu. Başlatılmayan kayıtları ve işlem sonuçlarını kontrol edin.":"Her kaydın sonucunu aşağıda kontrol edin. Güncelleme listesini yeniden tarayarak doğrulayın."),false);
                RenderInstallAction();RenderRepairAction();
            }
        }
    }
    internal static string InstallStateLabel(UpdateInstallResult? result)=>T(result?.Status switch {"Installed"=>"Tamamlandı","Rejected"=>"Kurulmadı","Failed"=>"Başarısız","Busy"=>"Başka işlem sürüyor",_=>"Sonuç doğrulanamadı"});
    internal static bool ShouldStopUpdateQueue(UpdateInstallResult? result)=>result==null || result.Status is "Unknown" or "Busy" || result.RebootRequired;
    internal static string InstallDetail(UpdateEntry entry,UpdateInstallResult? result)
    {
        if(result==null)return T("Önceki kurulum kesilmiş olabilir. Sonucu yeniden tarayarak doğrulayın.");
        string prefix=entry.Name+" — ";
        string detail=result.Detail.StartsWith(prefix,StringComparison.Ordinal)?result.Detail[prefix.Length..]:result.Detail;
        detail=DynamicTranslations.KnownText(detail);
        if(result.RebootRequired)detail+="\n"+T("Yeniden başlatma gerekiyor; bilgisayar otomatik yeniden başlatılmadı.");
        return detail;
    }
    private void RefreshResultActions()
    {
        if(workerRunning)repairPendingUntil=DateTime.MinValue;
        RenderRepairAction();RenderInstallAction();
        QuickButton.IsEnabled=DeepButton.IsEnabled=!workerRunning && !installLaunching && !updateInstalling;
        _=ReadInstallProgress();
    }
    private async Task ReadInstallProgress()
    {
        if(demo || readingInstallProgress || installLaunching)return;
        readingInstallProgress=true;
        try {
            if(installQueueRunning) {
                var item=currentInstall;
                if(item==null)return;
                var progress=await Task.Run(()=>LocalStore.Read<ScanProgress>(App.UpdateProgressPath(item.Id)));
                if(windowClosed || !ReferenceEquals(currentInstall,item) || !installAwaiting)return;
                if(progress?.Running==true) {
                    item.Detail=DynamicTranslations.KnownText(progress.Message);
                    InstallResultText.Text=item.Name+"\n"+item.Detail;
                    installProgressWindow?.Update(completedInstalls,installQueue.Count,item.Name,item.Detail,true);
                }
                return;
            }
            // Global state is used only for an installation owned by another UI session.
            // Historical global results are never attached to the currently selected row.
            var state=await Task.Run(()=>{var progress=LocalStore.Read<ScanProgress>(App.UpdateInstallProgressPath);return(progress,active:progress?.Running==true && IsAlive(progress.ProcessId));});
            if(windowClosed || installQueueRunning)return;
            updateInstalling=state.active;
            InstallProgressBar.Visibility=updateInstalling?Visibility.Visible:Visibility.Collapsed;
            if(state.active)InstallResultText.Text=T("Başka bir güncelleme kurulumu çalışıyor; tamamlanmasını bekleyin.")+"\n"+DynamicTranslations.KnownText(state.progress!.Message);
            else if(installQueue.Count==0)InstallResultText.Text="";
            RenderRepairAction();RenderInstallAction();
        } catch(Exception ex) {if(!windowClosed && currentInstall!=null)currentInstall.Detail=F("Kurulum durumu okunamadı: {0}",ex.Message);}
        finally {readingInstallProgress=false;}
    }
}

