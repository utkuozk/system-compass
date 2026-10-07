using System.Diagnostics;
using System.IO;
using System.Windows;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;
public partial class MainWindow
{
    private bool installLaunching,updateInstalling,readingInstallProgress,installAwaiting;
    private readonly System.Windows.Threading.DispatcherTimer actionTimer=new(){Interval=TimeSpan.FromSeconds(2)};
    private DateTime repairPendingUntil=DateTime.MinValue;
    private void InitializeResultActions()
    {
        actionTimer.Tick+=(_,_)=>{if(updateInstalling || installLaunching)_=ReadInstallProgress();};
        if(!demo)actionTimer.Start();
        RepairActionTitle.Text=T("Sonuçlara göre işlem yap");
        RepairButton.Content=T("Windows bozulmasını onar");
        InstallSelectedButton.Content=T("Seçileni güncelle");
        RenderRepairAction();RenderInstallAction();
    }
    private void RenderRepairAction()
    {
        bool eligible=RepairPolicy.HasActionableFinding(shownReport);
        RepairButton.IsEnabled=!demo && eligible && !workerRunning && !updateInstalling && !installLaunching && DateTime.UtcNow>=repairPendingUntil;
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
        var row=UpdatesList.SelectedItem as UpdateRow;
        string? reason=row==null?T("Güncellenecek kaydı listeden seçin."):!row.HasUpdate?T("Yeni sürüm doğrulanmadığı için kurulum yapılamaz."):UpdateInstaller.Validate(row.Entry);
        bool busy=installLaunching || updateInstalling || workerRunning;
        InstallSelectedButton.IsEnabled=!demo && reason==null && !busy;
        InstallEligibilityText.Text=busy?T("Başka bir kurulum veya bakım sürüyor. Menüler kullanılabilir; yeni kurulum için tamamlanmasını bekleyin."):reason!=null?T(reason):T(row!.Entry.Kind=="software"?"Doğrudan yazılım kurulumu yalnızca desteklenen MSIX paketleriyle sınırlıdır. Paket türü kurulumdan önce doğrulanır; desteklenmiyorsa işlem yapılmaz. Diğer yazılımlar için uygulamanın kendi güncelleyicisini kullanın.":"Yalnızca seçili Windows Update kaydı kurulacak. Yeniden başlatma gerekiyorsa raporda gösterilir; uygulama yeniden başlatmaz.");
    }
    private async void InstallSelected_Click(object sender,RoutedEventArgs e)
    {
        if(demo || !InstallSelectedButton.IsEnabled || UpdatesList.SelectedItem is not UpdateRow row)return;
        var entry=row.Entry;
        var message=F("{0}\nMevcut: {1} · Yeni: {2}\n\nSeçili güncelleme yeniden doğrulanıp indirilecek ve kurulacak. Gereken lisans koşullarını kabul ederek devam edersiniz. Sürücü kurulumu ilgili aygıtı kısa süre kesintiye uğratabilir. Otomatik yeniden başlatma yapılmaz. Devam edilsin mi?",entry.Name,entry.InstalledVersion,entry.AvailableVersion);
        if(MessageBox.Show(message,"System Compass",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        installLaunching=true;RenderInstallAction();RenderRepairAction();
        string id=Guid.NewGuid().ToString("N");string request=App.UpdateRequestPath(id);
        try {
            await Task.Run(()=>LocalStore.Write(request,entry));
            using var process=await Task.Run(()=> entry.Kind=="software"?Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden,ArgumentList={"--install-update",id}}):App.Elevate("--install-update "+id));
            if(process==null)throw new InvalidOperationException(T("Kurulum başlatılamadı."));
            updateInstalling=true;installAwaiting=true;installLaunching=false;InstallProgressBar.Visibility=Visibility.Visible;
            InstallResultText.Text=T("Kurulum koşulları denetleniyor…");RenderInstallAction();
            await process.WaitForExitAsync();
            if(windowClosed)return;
            installAwaiting=false;
            await ReadInstallProgress();
            var ownResult=await Task.Run(()=>LocalStore.Read<UpdateInstallResult>(Path.Combine(LocalStore.Root,"UpdateHistory",id+".json")));
            InstallResultText.Text=ownResult==null?T("Önceki kurulum kesilmiş olabilir. Sonucu yeniden tarayarak doğrulayın."):DynamicTranslations.KnownText(ownResult.Detail)+(ownResult.RebootRequired?"\n"+T("Yeniden başlatma gerekiyor; bilgisayar otomatik yeniden başlatılmadı."):"");
            InstallResultText.Text+="\n"+T("Kurulumdan sonra listeyi güncellemeleri denetle düğmesiyle yeniden kontrol edin.");
        } catch(Exception ex){if(!windowClosed)InstallResultText.Text=F("İşlem başlatılmadı: {0}",ex.Message);}
        finally {installLaunching=false;installAwaiting=false;updateInstalling=false;try{File.Delete(request);}catch{}if(!windowClosed){InstallProgressBar.Visibility=Visibility.Collapsed;RenderInstallAction();RenderRepairAction();}}
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
            var state=await Task.Run(()=>{var progress=LocalStore.Read<ScanProgress>(App.UpdateInstallProgressPath);var result=LocalStore.Read<UpdateInstallResult>(App.UpdateInstallResultPath);return(progress,result,active:progress?.Running==true && IsAlive(progress.ProcessId));});
            if(windowClosed)return;
            updateInstalling=state.active || installAwaiting;
            InstallProgressBar.Visibility=updateInstalling?Visibility.Visible:Visibility.Collapsed;
            if(state.progress!=null)InstallResultText.Text=state.active?DynamicTranslations.KnownText(state.progress.Message):state.progress.Running?T("Önceki kurulum kesilmiş olabilir. Sonucu yeniden tarayarak doğrulayın."):DynamicTranslations.KnownText(state.result?.Detail??state.progress.Message)+(state.result?.RebootRequired==true?"\n"+T("Yeniden başlatma gerekiyor; bilgisayar otomatik yeniden başlatılmadı."):"");
            RenderRepairAction();RenderInstallAction();
        } finally {readingInstallProgress=false;}
    }
}
