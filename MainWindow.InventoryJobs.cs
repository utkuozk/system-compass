using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;

public partial class MainWindow
{
    private sealed class InventoryJob
    {
        public CancellationTokenSource Cancel {get;}=new();
        public Stopwatch Elapsed {get;}=Stopwatch.StartNew();
        public string Phase {get;set;}="Kontrol hazırlanıyor…";
    }
    private readonly Dictionary<string,InventoryJob> inventoryJobs=new();
    private readonly Dictionary<string,UpdateInventoryResult> inventoryResults=new();
    private readonly Dictionary<string,string> inventoryMessages=new();
    private readonly DispatcherTimer inventoryTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private void InitializeInventoryProgress()
    {
        InventoryCancelButton.Content=T("Sorguyu iptal et");
        inventoryTimer.Tick+=(_,_)=>RenderInventoryProgress();
    }
    private void RenderInventoryProgress()
    {
        bool active=inventoryJobs.TryGetValue(updateKind,out var job);
        UpdateScanButton.IsEnabled=!active;
        InventoryProgressPanel.Visibility=active?Visibility.Visible:Visibility.Collapsed;
        if(!active)return;
        InventoryCancelButton.IsEnabled=!job!.Cancel.IsCancellationRequested;
        InventoryProgressText.Text=F("{0} · Geçen süre: {1:0} sn",T(job.Phase),job.Elapsed.Elapsed.TotalSeconds);
        UpdateStatus.Text=T("Diğer menüleri kullanabilirsiniz. Önceki sonuçlar tarama tamamlanana kadar korunur.");
    }
    private void InventoryCancel_Click(object sender,RoutedEventArgs e)
    {
        if(inventoryJobs.TryGetValue(updateKind,out var job)) {job.Phase="Sorgu durduruluyor…";job.Cancel.Cancel();RenderInventoryProgress();}
    }
    private async void UpdateScan_Click(object sender,RoutedEventArgs e)
    {
        string kind=updateKind;
        if(demo || inventoryJobs.ContainsKey(kind))return;
        var job=new InventoryJob();inventoryJobs.Add(kind,job);inventoryTimer.Start();RenderInventoryProgress();
        try {
            var progress=new Progress<string>(phase=>{if(!windowClosed && inventoryJobs.TryGetValue(kind,out var current) && ReferenceEquals(current,job) && !job.Cancel.IsCancellationRequested){job.Phase=phase;RenderInventoryProgress();}});
            var result=await UpdateInventory.ScanAsync(kind,job.Cancel.Token,progress);
            job.Cancel.Token.ThrowIfCancellationRequested();
            await Task.Run(()=>LocalStore.Write(Path.Combine(LocalStore.Root,"updates-"+kind+".json"),result));
            inventoryResults[kind]=result;
            inventoryMessages[kind]=F("{0:g} — {1}",result.CheckedUtc.ToLocalTime(),DisplayUpdateStatus(result.Status));
            if(!windowClosed && updateKind==kind)PopulateUpdates(result);
        } catch(OperationCanceledException) {inventoryMessages[kind]=T("Sorgu iptal edildi. Önceki sonuçlar korundu.");}
        catch(Exception ex) {inventoryMessages[kind]=F("Kontrol tamamlanamadı: {0}",DynamicTranslations.KnownText(ex.Message));}
        finally {
            inventoryJobs.Remove(kind);job.Cancel.Dispose();
            if(inventoryJobs.Count==0)inventoryTimer.Stop();
            if(!windowClosed) {RenderInventoryProgress();if(updateKind==kind)UpdateStatus.Text=inventoryMessages.GetValueOrDefault(kind,"");}
        }
    }
    internal static void CheckInventoryUi(Action<bool,string> check)
    {
        var window=new MainWindow(true);
        var software=new InventoryJob();var windows=new InventoryJob();
        try {
            window.inventoryJobs.Add("software",software);
            window.ShowUpdates("software");
            check(!window.UpdateScanButton.IsEnabled && window.InventoryProgressPanel.Visibility==Visibility.Visible,"Running query shows progress and prevents duplicate scan");
            window.ShowUpdates("windows");
            check(window.UpdateScanButton.IsEnabled && window.InventoryProgressPanel.Visibility==Visibility.Collapsed,"Independent update page remains available");
            window.inventoryJobs.Add("windows",windows);
            window.ShowUpdates("software");
            check(window.InventoryProgressText.Text.Length>0 && !window.UpdateScanButton.IsEnabled,"Returning to running query restores progress");
            window.InventoryCancel_Click(window,new RoutedEventArgs());
            check(software.Cancel.IsCancellationRequested && !windows.Cancel.IsCancellationRequested,"Cancel affects only the selected read-only query");
            window.Page("settings");
            check(window.SettingsPanel.Visibility==Visibility.Visible && window.inventoryJobs.Count==2,"Navigation does not discard active queries");
        } finally {window.Close();software.Cancel.Dispose();windows.Cancel.Dispose();}
    }

}
