using System.Diagnostics;
using System.IO;
using System.Windows;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;

public partial class MainWindow
{
    private GitHubRelease? githubRelease;
    private string checkedRepository="";
    private DateTime nextGithubCheck=DateTime.MinValue;
    private bool githubBusy;
    private void InitializeGitHubSettings()
    {
        GitHubRepositoryBox.Text=string.IsNullOrWhiteSpace(settings.GitHubRepository)?"utkuozk/system-compass":settings.GitHubRepository;
        GitHubAutoCheck.IsChecked=settings.CheckAppUpdates;
        AppVersionText.Text=F("Kurulu sürüm: {0}",ReleaseFeed.Current.ToString(3));
    }
    private async void AutomaticGitHubCheck()
    {
        if(demo || githubBusy || DateTime.UtcNow<nextGithubCheck)return;
        nextGithubCheck=DateTime.UtcNow.AddHours(1);
        var current=await Task.Run(LocalStore.LoadSettings);
        if(windowClosed)return;
        if(current.CheckAppUpdates)_ = CheckGitHub(string.IsNullOrWhiteSpace(current.GitHubRepository)?"utkuozk/system-compass":current.GitHubRepository);
    }
    private async void GitHubCheck_Click(object sender,RoutedEventArgs e)
    {
        if(demo || githubBusy)return;
        var current=LocalStore.LoadSettings();
        current.GitHubRepository=GitHubRepositoryBox.Text.Trim();current.CheckAppUpdates=GitHubAutoCheck.IsChecked==true;
        LocalStore.SaveSettings(current);
        await CheckGitHub(current.GitHubRepository);
    }
    private async Task CheckGitHub(string repository)
    {
        githubBusy=true;GitHubCheckButton.IsEnabled=false;GitHubInstallButton.IsEnabled=false;
        GitHubStatus.Text=T("GitHub sürümü kontrol ediliyor…");
        try {
            var result=await GitHubReleaseClient.CheckAsync(repository);
            githubRelease=result.Release;checkedRepository=repository;
            GitHubStatus.Text=DynamicTranslations.KnownText(result.Message);
            if(githubRelease!=null && !string.IsNullOrWhiteSpace(githubRelease.Notes))
                GitHubStatus.Text+="\n\n"+T("Sürüm notları (kaynağın özgün dilinde):")+"\n"+githubRelease.Notes;
            GitHubInstallButton.IsEnabled=githubRelease!=null;
            if(githubRelease!=null) {ReleaseBanner.Visibility=Visibility.Visible;ReleaseText.Text=F("Yeni sürüm: {0} — ayrıntılar Bakım ayarlarında.",githubRelease.Version);}
        } catch(Exception ex){GitHubStatus.Text=F("Güncelleme kontrolü tamamlanamadı: {0}",DynamicTranslations.KnownText(ex.Message));githubRelease=null;}
        finally{githubBusy=false;GitHubCheckButton.IsEnabled=true;}
    }
    private async void GitHubInstall_Click(object sender,RoutedEventArgs e)
    {
        if(demo || githubBusy || githubRelease==null)return;
        var maintenance=LocalStore.Read<ScanProgress>(LocalStore.ProgressPath);
        var antivirus=LocalStore.Read<ScanProgress>(Path.Combine(LocalStore.Root,"antivirus-progress.json"));
        if(maintenance?.Running==true && IsAlive(maintenance.ProcessId) || antivirus?.Running==true && IsAlive(antivirus.ProcessId)) {GitHubStatus.Text=T("Çalışan bakım tamamlandıktan sonra güncelleyin.");return;}
        if(MessageBox.Show(F("{0} sürümü GitHub'dan indirilecek ve doğrulanacak. Ardından uygulama kapanıp kurulum açılacak; Windows yönetici izni isteyebilir.\n\nDevam edilsin mi?",githubRelease.Version),T("SystemCompass güncellemesi"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        githubBusy=true;GitHubInstallButton.IsEnabled=false;GitHubCheckButton.IsEnabled=false;
        AppUpdateProgress.Visibility=Visibility.Visible;AppUpdateProgressText.Visibility=Visibility.Visible;
        AppUpdateProgress.Value=0;AppUpdateProgressText.Text=T("İndirme hazırlanıyor…");
        try {
            GitHubStatus.Text=T("Paket indiriliyor ve bütünlüğü doğrulanıyor…");
            var progress=new Progress<PackageProgress>(p=>{
                AppUpdateProgress.IsIndeterminate=p.Stage=="verifying";
                AppUpdateProgress.Value=p.Total>0?Math.Clamp(100d*p.Bytes/p.Total,0,100):0;
                AppUpdateProgressText.Text=p.Stage=="verifying"?T("Paketin bütünlüğü doğrulanıyor…"):p.Stage=="ready"?T("Paket doğrulandı. Kurulum açılıyor…"):F("İndiriliyor: %{0:0} ({1:0.0} / {2:0.0} MB)",AppUpdateProgress.Value,p.Bytes/1048576d,p.Total/1048576d);
            });
            var directory=await GitHubReleaseClient.DownloadAsync(githubRelease,checkedRepository,progress);
            Process.Start(new ProcessStartInfo(Path.Combine(directory,"SistemPusulasi-Kur.exe")) {UseShellExecute=true});
            Application.Current.Shutdown();
        } catch(Exception ex){GitHubStatus.Text=F("Güncelleme kurulmadı: {0}",DynamicTranslations.KnownText(ex.Message));}
        finally{githubBusy=false;GitHubCheckButton.IsEnabled=true;GitHubInstallButton.IsEnabled=githubRelease!=null;AppUpdateProgress.Visibility=Visibility.Collapsed;AppUpdateProgressText.Visibility=Visibility.Collapsed;}
    }
}
